using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CocosGallery
{
    public static class AppStorage
    {
        public static string LocalFolderPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CocosGallery");
        public static string TemporaryFolderPath { get; } = Path.Combine(Path.GetTempPath(), "CocosGallery");
        public static string LocalCacheFolderPath { get; } = Path.Combine(LocalFolderPath, "Cache");

        public static LocalSettings LocalSettings { get; } = new LocalSettings();
        
        static AppStorage()
        {
            Directory.CreateDirectory(LocalFolderPath);
            Directory.CreateDirectory(TemporaryFolderPath);
            Directory.CreateDirectory(LocalCacheFolderPath);
        }
    }

    public class LocalSettings
    {
        private Dictionary<string, object> _values = new Dictionary<string, object>();
        private string SettingsFilePath = Path.Combine(AppStorage.LocalFolderPath, "settings.json");

        public LocalSettingsValues Values { get; }

        public LocalSettings()
        {
            if (File.Exists(SettingsFilePath))
            {
                try
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                    if (dict != null)
                    {
                        foreach (var kvp in dict)
                        {
                            if (kvp.Value.ValueKind == JsonValueKind.True || kvp.Value.ValueKind == JsonValueKind.False)
                                _values[kvp.Key] = kvp.Value.GetBoolean();
                            else if (kvp.Value.ValueKind == JsonValueKind.String)
                            {
                                var strVal = kvp.Value.GetString();
                                if (strVal != null) _values[kvp.Key] = strVal;
                            }
                            else if (kvp.Value.ValueKind == JsonValueKind.Number)
                                _values[kvp.Key] = kvp.Value.GetInt32();
                        }
                    }
                }
                catch { }
            }
            Values = new LocalSettingsValues(this);
        }

        public void Save()
        {
            try {
                var json = JsonSerializer.Serialize(_values);
                File.WriteAllText(SettingsFilePath, json);
            } catch { }
        }

        public class LocalSettingsValues
        {
            private LocalSettings _parent;
            public LocalSettingsValues(LocalSettings parent) => _parent = parent;

            public object? this[string key]
            {
                get => _parent._values.TryGetValue(key, out var val) ? val : null;
                set
                {
                    if (value != null) _parent._values[key] = value;
                    else _parent._values.Remove(key);
                    _parent.Save();
                }
            }

            public bool TryGetValue(string key, out object? value)
            {
                bool found = _parent._values.TryGetValue(key, out var v);
                value = v;
                return found;
            }
        }
    }
}
