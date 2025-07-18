using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Xml.Serialization;

namespace PRNPusher
{
    public class PrnScanner : INotifyPropertyChanged
    {
        private DispatcherTimer _scanTimer;
        private static readonly HttpClient httpClient = new HttpClient();
        private DateTime _lastScanTime = DateTime.MinValue;

        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string _folderPath;
        public string FolderPath
        {
            get => _folderPath;
            set { if (_folderPath != value) { _folderPath = value; OnPropertyChanged(nameof(FolderPath)); } }
        }

        private string _influxUrl;
        public string InfluxUrl
        {
            get => _influxUrl;
            set { if (_influxUrl != value) { _influxUrl = value; OnPropertyChanged(nameof(InfluxUrl)); } }
        }

        private string _influxOrg;
        public string InfluxOrg
        {
            get => _influxOrg;
            set { if (_influxOrg != value) { _influxOrg = value; OnPropertyChanged(nameof(InfluxOrg)); } }
        }

        private string _influxBucket;
        public string InfluxBucket
        {
            get => _influxBucket;
            set { if (_influxBucket != value) { _influxBucket = value; OnPropertyChanged(nameof(InfluxBucket)); } }
        }

        private string _measurement = "measurement";
        public string Measurement
        {
            get => _measurement;
            set { if (_measurement != value) { _measurement = value; OnPropertyChanged(nameof(Measurement)); } }
        }

        private string _influxToken;
        public string InfluxToken
        {
            get => _influxToken;
            set { if (_influxToken != value) { _influxToken = value; OnPropertyChanged(nameof(InfluxToken)); } }
        }

        private bool _running;

        public bool Running
        {
            get => _running;
            private set { if (_running != value) { _running = value; OnPropertyChanged(nameof(Running)); } }
        }

        public string SelectedFields
        {
            get => string.Join("\t", DataFields.Where(f => f.Value).Select(f => f.Key).ToList());
            set
            {
                var fields = value.Split('\t');
                foreach (var field in fields)
                {
                    if (!string.IsNullOrWhiteSpace(field))
                        DataFields[field] = true; // Ensure the field is enabled
                }
                OnPropertyChanged(nameof(SelectedFields));
            }
        }

        public ObservableCollection<string> Messages { get; set; } = new ObservableCollection<string>();

        public string InfluxPrecision { get; set; } = "s"; // Default precision is seconds
        public event Action<string, List<Dictionary<string, string>>> PrnFileScanned;

        // Changed property to hold data fields as a dictionary
        public ObservableDictionary<string, bool> DataFields { get; set; } = new ObservableDictionary<string, bool>();

        // For each PRN file, the completion status starts with 2. Every time when the file is checked and no new data is found, it will be decremented by 1.
        // When it reaches 0, the file will not be opened in later cycles.
        // If new data is found, it will be reset to 2.
        // When the DataFields are updated, the completion status will be reset to 2.
        public ObservableDictionary<string, int> FileCompletionStatus { get; set; } = new ObservableDictionary<string, int>();

        // Add a private field to store sentRecords for all prn files
        private Dictionary<string, Dictionary<string, HashSet<string>>> _allSentRecords = new Dictionary<string, Dictionary<string, HashSet<string>>>();


        public void SetDataField(string fieldName, bool isEnabled)
        {
            if (DataFields.ContainsKey(fieldName))
            {
                DataFields[fieldName] = isEnabled;
            }
            else
            {
                DataFields.Add(fieldName, isEnabled);
            }
            if(isEnabled)
            {
                // Reset the completion status for this field if it is enabled
                foreach (var file in FileCompletionStatus.Keys.ToList())
                {
                    FileCompletionStatus[file] = 2; // Reset to 2 when a field is enabled
                }
            }
            OnPropertyChanged(nameof(DataFields));
            OnPropertyChanged(nameof(SelectedFields));
        }

        public PrnScanner()
        {
            _scanTimer = new DispatcherTimer();
            _scanTimer.Interval = TimeSpan.FromSeconds(15);
            _scanTimer.Tick += ScanTimer_Tick;
        }

        public void Start()
        {
            ScanTimer_Tick(null, null);
            _scanTimer.Start();
            Running = true;
        }

        public void Stop()
        {
            _scanTimer.Stop();
            Running = false;
        }

        private void ScanTimer_Tick(object sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(FolderPath) && Directory.Exists(FolderPath))
            {
                List<string> prnFiles = new List<string>();
                try
                {
                    prnFiles.AddRange(Directory.GetFiles(FolderPath, "*.prn", SearchOption.AllDirectories));
                    //System.Diagnostics.Debug.WriteLine($"Found {prnFiles.Count} PRN files in {FolderPath}");
                    
                    foreach (var prnFile in prnFiles)
                    {
                        //System.Diagnostics.Debug.WriteLine($"Processing PRN file: {prnFile}");
                        ReadPrnFile(prnFile);
                    }
                    _lastScanTime = DateTime.Now;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error scanning folder: {ex.Message}");
                    AddMessage($"Error scanning folder: {ex.Message}");
                }
            }
        }

        private async void ReadPrnFile(string filePath)
        {
            var lineProtocolRows = new List<string>();
            try
            {
                string logPath = Path.Combine(Path.GetDirectoryName(filePath), Path.GetFileNameWithoutExtension(filePath) + "_sent.xml");
                Dictionary<string, HashSet<string>> sentRecords;
                // Use the cache if available, otherwise load from XML
                if (_allSentRecords.ContainsKey(logPath))
                {
                    sentRecords = _allSentRecords[logPath];
                }
                else if (File.Exists(logPath))
                {
                    // deserialize the file to build sentRecords
                    var serializer = new XmlSerializer(typeof(List<SerializableSentRecord>));
                    var sentRecordList = serializer.Deserialize(new FileStream(logPath, FileMode.Open, FileAccess.Read)) as List<SerializableSentRecord>;
                    sentRecords = sentRecordList.ToDictionary(record => record.Timestamp, record => new HashSet<string>(record.FieldNames));
                    _allSentRecords[logPath] = sentRecords;
                }
                else
                {
                    sentRecords = new Dictionary<string, HashSet<string>>();
                    _allSentRecords[logPath] = sentRecords;
                }

                // Check if the file is already processed
                var filename = Path.GetFileName(filePath);
                if (FileCompletionStatus.ContainsKey(filename))
                {
                    // Check last modified time and update FileCompletionStatus if needed
                    var lastModified = File.GetLastWriteTime(filePath);
                    if (lastModified > _lastScanTime)
                    {
                        FileCompletionStatus[filename] = 1;
                    }
                    else if (FileCompletionStatus[filename] <= 0)
                    {
                        return; // Skip if already processed    
                    }
                    FileCompletionStatus[filename]--; // Decrement the completion status
                }
                // If the file is not in the completion status, initialize it
                else
                {
                    FileCompletionStatus[filename] = 1; // Initialize to 1 if not present
                }

                var lines = File.ReadAllLines(filePath);
                if (lines.Length < 2)
                    return; // No data
                
                var (headers, headersInfluxFormat) = GetHeaders(lines[0]);
                
                // go over each data line in the file
                for (int i = 1; i < lines.Length; i++)
                {
                    var values = lines[i].Split('\t');
                    if (values.Length < 4) continue; // Not enough data
                    if (values[1] == "Date") (headers, headersInfluxFormat) = GetHeaders(lines[i]);
                    string date = values[1];
                    string time = values[2];
                    string timestamp = GetInfluxTimestamp(date, time);
                    if (string.IsNullOrWhiteSpace(timestamp))
                        continue; // Skip if no valid timestamp
                    var influxFields = new List<string>();
                    if (!sentRecords.ContainsKey(timestamp))
                    {
                        sentRecords[timestamp] = new HashSet<string>();
                    }
                    // Process fields starting from index 3
                    for (int j = 3; j < headers.Length && j < values.Length; j++)
                    {
                        var field = headers[j];
                        var fieldInfluxFormat = headersInfluxFormat[j];
                        if (DataFields[field] && (!sentRecords[timestamp].Contains(field)) && !string.IsNullOrWhiteSpace(values[j]))
                        {
                            if (double.TryParse(values[j], out double num))
                                influxFields.Add($"{fieldInfluxFormat}={values[j]}");
                            else
                                influxFields.Add($"{fieldInfluxFormat}=\"{values[j]}\"");
                            sentRecords[timestamp].Add(field);
                        }
                    }
                    if (influxFields.Count == 0)
                        continue; // No valid fields to send
                    string line = $"{Measurement} {string.Join(",", influxFields)} {timestamp}";
                    lineProtocolRows.Add(line);
                }

                // After processing all rows, send to InfluxDB
                if (lineProtocolRows.Count > 0 && !string.IsNullOrWhiteSpace(InfluxUrl))
                {
                    // If new data was found, reset the completion status for this file
                    FileCompletionStatus[filename] = 2;
                    var allLines = string.Join("\n", lineProtocolRows);
                    //Task.Run(async () => {
                    //    await SendToInfluxAsync(allLines, sentRecords, logPath);
                    //    AddMessage($"Sucessfully uploaded {lineProtocolRows.Count} lines of data.");
                    //    });
                    if (await SendToInfluxAsync(allLines, sentRecords, logPath)) {
                        AddMessage($"Sucessfully uploaded {lineProtocolRows.Count} lines of data from {filename}.");
                    }

                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading prn file {filePath}: {ex.Message}");
                AddMessage($"Error reading PRN file {filePath}: {ex.Message}");
            }
        }

        private string GetInfluxTimestamp(string date, string time)
        {
            // Try to parse date and time to a UTC timestamp in seconds
            // Example: date = "2024-06-01", time = "12:34:56"
            if (DateTime.TryParse($"{date} {time}", out DateTime dt))
            {
                // InfluxDB expects Unix time in seconds if precision is set to 's'
                long unixTime = (long)(dt.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalSeconds;
                return unixTime.ToString();
            }
            return "";
        }

        private async Task<bool> SendToInfluxAsync(string lineProtocol, Dictionary<string, HashSet<string>> sentRecords, string logPath)
        {
            try
            {
                // Compose the InfluxDB 2.x write URL
                // Example: https://influx.susteon.cloud/api/v2/write?org=ORG&bucket=BUCKET&precision=s
                var url = $"{InfluxUrl.TrimEnd('/')}?org={Uri.EscapeDataString(InfluxOrg)}&bucket={Uri.EscapeDataString(InfluxBucket)}&precision={Uri.EscapeDataString(InfluxPrecision)}";

                var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(lineProtocol)
                };
                if (string.IsNullOrWhiteSpace(InfluxToken))
                {
                    // Test mode without token
                    System.Diagnostics.Debug.WriteLine("InfluxDB token is not set. Skipping upload.");
                    System.Diagnostics.Debug.WriteLine($"URL: {url}");
                    System.Diagnostics.Debug.WriteLine($"Data to upload: {lineProtocol}");
                    AddMessage("InfluxDB token is not set. Skipping upload.");
                }
                else
                {
                    request.Headers.Add("Authorization", $"Token {InfluxToken}");
                    var response = await httpClient.SendAsync(request);
                    response.EnsureSuccessStatusCode();
                }

                // Write record to file if upload is successful
                var serializer = new XmlSerializer(typeof(List<SerializableSentRecord>));
                var serializableList = sentRecords
                    .Select(kvp => new SerializableSentRecord
                    {
                        Timestamp = kvp.Key,
                        FieldNames = kvp.Value.ToList()
                    })
                    .ToList();
                using (var fs = new FileStream(logPath, FileMode.Create, FileAccess.Write))
                {
                    serializer.Serialize(fs, serializableList);
                }

                // Also update the cache
                if (!_allSentRecords.ContainsKey(logPath))
                    _allSentRecords[logPath] = new Dictionary<string, HashSet<string>>();
                _allSentRecords[logPath] = sentRecords;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error uploading to InfluxDB: {ex.Message}");
                AddMessage($"Error uploading to InfluxDB: {ex.Message}");
                return false;
            }
        }

        private void AddMessage(string message)
        {
            var dateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            message = $"{dateTime} : {message}";
            Messages.Add(message);
            while (Messages.Count > 100) // Limit to last 100 messages
            {
                Messages.RemoveAt(0);
            }
            OnPropertyChanged(nameof(Messages));
        }

        private (string[] headers, string[] headersInfluxFormat) GetHeaders(string line)
        {
            var headers = line.Trim().Split('\t');
            var headersInfluxFormat = line
                .Trim()
                .Replace(" ", "\\ ")
                .Replace(",", "\\,")
                .Replace("=", "\\=")
                .Split('\t');

            // Ensure DataFields contains all headers as keys, excluding the first 3
            var newFieldFound = false;
            for (int h = 3; h < headers.Length; h++)
            {
                if (!(DataFields.ContainsKey(headers[h]) || string.IsNullOrWhiteSpace(headers[h])))
                {
                    DataFields[headers[h]] = false;
                    newFieldFound = true;
                }
            }
            if (newFieldFound)
            {
                OnPropertyChanged(nameof(DataFields));
            }

            return (headers, headersInfluxFormat);
        }

    }


    [Serializable]
    [XmlRoot("SentRecord")]
    [XmlType("SentRecord")]
    public struct SerializableSentRecord
    {
        public string Timestamp { get; set; }
        public List<string> FieldNames { get; set; }
    }
}
