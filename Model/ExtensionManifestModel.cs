using Newtonsoft.Json;
using SuchByte.MacroDeck.ExtensionStore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SuchByte.MacroDeck.Model
{
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


        public static ExtensionManifestModel FromZipFilePath(string zipFilePath)
        {
            Stream stream = new StreamReader(
                System.IO.Compression.ZipFile.OpenRead(zipFilePath)
                                                .Entries.Where(x => x.Name.Equals("ExtensionManifest.json", StringComparison.InvariantCulture))
                                                .FirstOrDefault()
                                                .Open(), Encoding.UTF8).BaseStream;
            return FromJsonStream(stream);
        }

        public static ExtensionManifestModel FromJsonStream(Stream stream)
        {
            JsonSerializer serializer = new JsonSerializer();
            using (var sr = new StreamReader(stream))
            using (var jsonReader = new JsonTextReader(sr))
            {
                while (!sr.EndOfStream)
                {
                    return serializer.Deserialize<ExtensionManifestModel>(jsonReader);
                }
            }
            return null;
        }
    }
}
