using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using SuchByte.MacroDeck.ExtensionStore;

namespace SuchByte.MacroDeck.Models;

public class ExtensionManifestModel
{
    [JsonProperty("type")]
    public ExtensionType Type { get; set; } = ExtensionType.Plugin;

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("author")]
    public string Author { get; set; }

    [JsonProperty("repository")]
    public string Repository { get; set; } = "";

    [JsonProperty("packageId")]
    public string PackageId { get; set; }

    [JsonProperty("version")]
    public string Version { get; set; }

    [JsonProperty("target-plugin-api-version")]
    public int TargetPluginAPIVersion { get; set; } = 31;

    [JsonProperty("dll")]
    public string Dll { get; set; } = "";

    public static string Serialize(ExtensionManifestModel extensionManifestModel)
    {
        return JsonConvert.SerializeObject(extensionManifestModel);
    }

    public static ExtensionManifestModel? FromManifestFile(string path)
    {
        using var s = File.OpenRead(path);
        return FromJsonStream(s);
    }

    public static ExtensionManifestModel? FromZipFilePath(string zipFilePath)
    {
        var stream = new StreamReader(
            ZipFile.OpenRead(zipFilePath)
                .Entries.Where(x => x.Name.Equals("ExtensionManifest.json", StringComparison.InvariantCulture))
                .FirstOrDefault()
                .Open(), Encoding.UTF8).BaseStream;
        return FromJsonStream(stream);
    }

    public static ExtensionManifestModel? FromJsonStream(Stream stream)
    {
        var serializer = new JsonSerializer();
        using var sr = new StreamReader(stream);
        using var jsonReader = new JsonTextReader(sr);
        while (!sr.EndOfStream)
        {
            return serializer.Deserialize<ExtensionManifestModel>(jsonReader);
        }

        return null;
    }
}