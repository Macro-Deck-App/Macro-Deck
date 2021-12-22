using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace SuchByte.MacroDeck.Language
{
    public static class LanguageManager
    {
        public static event EventHandler LanguageChanged;
        public static List<Strings> Languages { get { return _languages; } }
        private static List<Strings> _languages = new List<Strings>();

        private static Strings _strings = new Strings();
        public static Strings Strings { get { return _strings; } }


        public static void Load(bool exportDefaultStrings = false)
        {
            _languages.Clear();
            _languages.Add(_strings);
            if (exportDefaultStrings)
            {
                SaveDefault();
            }

            // Loading languages from resources
            var assembly = typeof(Strings).Assembly;
            foreach (var manifestResource in assembly.GetManifestResourceNames())
            {
                try
                {
                    if (!manifestResource.StartsWith("SuchByte.MacroDeck.Resources.Languages.") ||! manifestResource.EndsWith(".xml")) continue;

                    using var resourceStream = assembly.GetManifestResourceStream(manifestResource);
                    using var streamReader = new StreamReader(resourceStream);

                    using (TextReader reader = new StringReader(streamReader.ReadToEnd()))
                    {
                        Strings language = (Strings)new XmlSerializer(typeof(Strings)).Deserialize(reader);
                        if (_languages.FindAll(l => l.__Language__.Equals(language.__Language__) && l.__LanguageCode__.Equals(language.__LanguageCode__) && l.__Author__.Equals(language.__Author__)).Count > 0) continue;
                        _languages.Add(language);
                    }
                } catch { }
            }

            _languages = _languages.OrderBy(x => x.__Language__).ToList();
          
            foreach (Strings languageString in _languages)
            {
                Debug.WriteLine(languageString.__Language__);
            }
        }


        private static void SaveDefault()
        {
            try
            {
                XmlSerializer writer = new XmlSerializer(typeof(Strings));

                var path = Path.Combine(MacroDeck.MainDirectoryPath, "Language", _strings.__Language__ + ".xml");
                Directory.CreateDirectory(Path.Combine(MacroDeck.MainDirectoryPath, "Language"));
                using (FileStream fileStream = File.Create(path))
                {
                    writer.Serialize(fileStream, _strings);
                    fileStream.Close();
                }
                Debug.WriteLine("Default language file saved");
            } catch { }
        }

        public static void SetLanguage(string languageName)
        {
            Strings strings = _languages.Find(l => l.__Language__.Equals(languageName));
            if (strings != null)
            {
                SetLanguage(strings);
            }
        }

        public static void SetLanguage(Strings language)
        {
            Debug.WriteLine("Set language to " + language.__Language__);
            _strings = language;
            if (LanguageChanged != null)
            {
                LanguageChanged(language, EventArgs.Empty);
            }
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
