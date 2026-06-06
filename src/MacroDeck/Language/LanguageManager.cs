using System.IO;
using Newtonsoft.Json;
using Serilog;
using SuchByte.MacroDeck.StartupConfig;

namespace SuchByte.MacroDeck.Language;

public static class LanguageManager
{
    private static readonly ILogger logger = Log.ForContext(typeof(LanguageManager));

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
        logger.Information("Loading language files...");
        var assembly = typeof(Strings).Assembly;
        foreach (var manifestResource in assembly.GetManifestResourceNames())
        {
            try
            {
                if (!manifestResource.StartsWith("SuchByte.MacroDeck.Resources.Languages.") ||
                    !manifestResource.EndsWith(".json"))
                {
                    continue;
                }

                logger.Information("Loading ${ManifestResource}...", manifestResource);
                using var resourceStream = assembly.GetManifestResourceStream(manifestResource);

                var serializer = new JsonSerializer();
                using var sr = new StreamReader(resourceStream);
                using var jsonReader = new JsonTextReader(sr);
                while (!sr.EndOfStream)
                {
                    var language = serializer.Deserialize<Strings>(jsonReader);
                    if (_languages.FindAll(l =>
                            l.__Language__.Equals(language.__Language__) &&
                            l.__LanguageCode__.Equals(language.__LanguageCode__) &&
                            l.__Author__.Equals(language.__Author__)).Count >
                        0)
                    {
                        continue;
                    }

                    _languages.Add(language);
                }
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Failed to load language resource");
            }
        }

        _languages = _languages.OrderBy(x => x.__Language__).ToList();
    }

    private static void SaveDefault()
    {
        var path = Path.Combine(ApplicationPaths.MainDirectoryPath, "Language", _strings.__Language__ + ".json");
        var serializer = new JsonSerializer
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented
        };

        try
        {
            using var sw = new StreamWriter(path);
            using JsonWriter writer = new JsonTextWriter(sw);
            serializer.Serialize(writer, _strings);

            logger.Information("{Language} saved", _strings.__Language__);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to save {Language}", _strings.__Language__);
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
        logger.Information("Set language to {Language}", language.__Language__);
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
