using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;

namespace PRNPusher
{
    public class ObservableDictionary<TKey, TValue> : Dictionary<TKey, TValue>, INotifyCollectionChanged, INotifyPropertyChanged
    {
        public event NotifyCollectionChangedEventHandler CollectionChanged;
        public event PropertyChangedEventHandler PropertyChanged;

        public new void Add(TKey key, TValue value)
        {
            base.Add(key, value);
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, new KeyValuePair<TKey, TValue>(key, value)));
            OnPropertyChanged("Count");
            OnPropertyChanged("Keys");
            OnPropertyChanged("Values");
        }

        public new bool Remove(TKey key)
        {
            if (TryGetValue(key, out TValue value) && base.Remove(key))
            {
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, new KeyValuePair<TKey, TValue>(key, value), getindex(key)));
                OnPropertyChanged("Count");
                OnPropertyChanged("Keys");
                OnPropertyChanged("Values");
                return true;
            }
            return false;
        }

        public new TValue this[TKey key]
        {
            get => base[key];
            set
            {
                bool exists = ContainsKey(key);
                TValue oldValue = exists ? base[key] : default(TValue);
                base[key] = value;
                if (exists)
                {
                    OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace,
                        new KeyValuePair<TKey, TValue>(key, value),
                        new KeyValuePair<TKey, TValue>(key, oldValue),
                        getindex(key)));
                }
                else
                {
                    OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, new KeyValuePair<TKey, TValue>(key, value)));
                    OnPropertyChanged("Count");
                    OnPropertyChanged("Keys");
                    OnPropertyChanged("Values");
                }
            }
        }

        public new void Clear()
        {
            base.Clear();
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            OnPropertyChanged("Count");
            OnPropertyChanged("Keys");
            OnPropertyChanged("Values");
        }

        protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            CollectionChanged?.Invoke(this, e);
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private int getindex(TKey key)
        {
            int index = 0;
            foreach (var k in Keys)
            {
                if (EqualityComparer<TKey>.Default.Equals(k, key))
                    return index;
                index++;
            }
            return -1; // Key not found
        }
    }
}
