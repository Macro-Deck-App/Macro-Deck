using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SuchByte.MacroDeck.Logging;

namespace SuchByte.MacroDeck.Language
{
    public static class LanguageManager
    {
        public static event EventHandler LanguageChanged;
        public static List<Strings> Languages => _languages;
        private static List<Strings> _languages = new();

        private static Strings _strings = new();
        public static Strings Strings => _strings;


        public static void Load(bool exportDefaultStrings = false)
        {
            _languages.Clear();
            _languages.Add(_strings);
            if (exportDefaultStrings)
            {
                SaveDefault();
            }

            // Loading languages from resources
            MacroDeckLogger.Info("Loading language files...");
            var assembly = typeof(Strings).Assembly;
            foreach (var manifestResource in assembly.GetManifestResourceNames())
            {
                try
                {
                    if (!manifestResource.StartsWith("SuchByte.MacroDeck.Resources.Languages.") || !manifestResource.EndsWith(".json")) continue;
                    MacroDeckLogger.Info(typeof(LanguageManager), $"Loading ${manifestResource}...");
                    using var resourceStream = assembly.GetManifestResourceStream(manifestResource);

                    var serializer = new JsonSerializer();
                    using (var sr = new StreamReader(resourceStream))
                    using (var jsonReader = new JsonTextReader(sr))
                    {
                        while (!sr.EndOfStream)
                        {
                            var language = serializer.Deserialize<Strings>(jsonReader);
                            if (_languages.FindAll(l => l.__Language__.Equals(language.__Language__) && l.__LanguageCode__.Equals(language.__LanguageCode__) && l.__Author__.Equals(language.__Author__)).Count > 0) continue;
                            _languages.Add(language);
                        }
                    }
                } catch (Exception ex) {

                    MacroDeckLogger.Warning("Failed to load language resource: " + ex.Message);
                }
            }

            _languages = _languages.OrderBy(x => x.__Language__).ToList();
        }

        private static void SaveDefault()
        {
            var path = Path.Combine(MacroDeck.MainDirectoryPath, "Language", _strings.__Language__ + ".json");
            var serializer = new JsonSerializer
            {
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.Indented,
            };

            try
            {
                using var sw = new StreamWriter(path);
                using JsonWriter writer = new JsonTextWriter(sw);
                serializer.Serialize(writer, _strings);

                MacroDeckLogger.Info(typeof(LanguageManager), $"{_strings.__Language__} saved");
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Error(typeof(LanguageManager), $"Failed to save {_strings.__Language__}: {ex.Message}");
            }
        }

        public static void SetLanguage(string languageName)
        {
            var strings = _languages.Find(l => l.__Language__.Equals(languageName));
            if (strings != null)
            {
                SetLanguage(strings);
            }
        }

        public static void SetLanguage(Strings language)
        {
            MacroDeckLogger.Info("Set language to " + language.__Language__);
            _strings = language;
            LanguageChanged?.Invoke(language, EventArgs.Empty);
        }

        public static string GetLanguageName()
        {
            return _strings.__Language__;
        }

        public static string GetLanguageCode()
        {
            return _strings.__LanguageCode__;
        }



    }
}
