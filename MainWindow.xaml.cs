using System;
using System.IO;
using System.Windows;
using System.Windows.Forms; // For FolderBrowserDialog
using System.Collections.Generic;
using System.Windows.Controls;
using System.ComponentModel;
using System.Linq;

namespace PRNPusher
{
    public partial class MainWindow : Window
    {
        private PrnScanner _scanner;

        public MainWindow()
        {
            InitializeComponent();
            _scanner = new PrnScanner();
            LoadSettings();
            DataContext = _scanner;
            _scanner.PropertyChanged += Scanner_PropertyChanged;
        }

        private void LoadSettings()
        {
            _scanner.FolderPath = Properties.Settings.Default.FolderPath;
            _scanner.InfluxUrl = Properties.Settings.Default.InfluxUrl;
            _scanner.InfluxOrg = Properties.Settings.Default.InfluxOrg;
            _scanner.InfluxBucket = Properties.Settings.Default.InfluxBucket;
            _scanner.Measurement = Properties.Settings.Default.Measurement;
            _scanner.InfluxToken = Properties.Settings.Default.InfluxToken;
            _scanner.SelectedFields = Properties.Settings.Default.SelectedFields;
        }

        private void SaveSettings()
        {
            Properties.Settings.Default.FolderPath = _scanner.FolderPath;
            Properties.Settings.Default.InfluxUrl = _scanner.InfluxUrl;
            Properties.Settings.Default.InfluxOrg = _scanner.InfluxOrg;
            Properties.Settings.Default.InfluxBucket = _scanner.InfluxBucket;
            Properties.Settings.Default.Measurement = _scanner.Measurement;
            Properties.Settings.Default.InfluxToken = _scanner.InfluxToken;
            Properties.Settings.Default.SelectedFields = _scanner.SelectedFields;
            Properties.Settings.Default.Save();
        }

        private void Scanner_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_scanner.FolderPath) ||
                e.PropertyName == nameof(_scanner.InfluxUrl) ||
                e.PropertyName == nameof(_scanner.InfluxOrg) ||
                e.PropertyName == nameof(_scanner.InfluxBucket) ||
                e.PropertyName == nameof(_scanner.Measurement) ||
                e.PropertyName == nameof(_scanner.InfluxToken) ||
                e.PropertyName == nameof(_scanner.SelectedFields)
                )
            {
                SaveSettings();
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select a folder";
                dialog.ShowNewFolderButton = true;
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    FolderPathTextBox.Text = dialog.SelectedPath;
                    _scanner.FolderPath = dialog.SelectedPath;
                }
            }
        }

        private void StartScanner_Click(object sender, RoutedEventArgs e)
        {
            _scanner.Start();
        }

        private void StopScanner_Click(object sender, RoutedEventArgs e)
        {
            _scanner.Stop();
        }

        private void DataFieldCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            var checkBox = sender as System.Windows.Controls.CheckBox;
            if (checkBox?.Tag is string fieldName && _scanner.DataFields.ContainsKey(fieldName))
            {
                _scanner.SetDataField(fieldName, checkBox.IsChecked == true);
            }
        }

        private void FileCompletionStatusCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            var checkBox = sender as System.Windows.Controls.CheckBox;
            if (checkBox?.Tag is string fileName && _scanner.FileCompletionStatus.ContainsKey(fileName))
            {
                if (checkBox.IsChecked == true)
                {
                    _scanner.FileCompletionStatus[fileName] = 2;
                }
                else
                {
                    _scanner.FileCompletionStatus[fileName] = 0;
                }
            }
        }
    }
}
