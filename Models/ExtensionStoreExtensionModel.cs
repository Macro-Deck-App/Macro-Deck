using Newtonsoft.Json;
using SuchByte.MacroDeck.ExtensionStore;
using System;
using System.Collections.Generic;
using System.Text;

namespace SuchByte.MacroDeck.Model
{
    public class ExtensionStoreExtensionModel
    {

        [JsonProperty("package-id")]
        public string PackageId { get; set; } = "";

        [JsonProperty("type")]
        public ExtensionType Type { get; set; } = ExtensionType.Plugin;

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("version")]
        public string Version { get; set; } = "";

        [JsonProperty("author")]
        public string Author { get; set; } = "";

        [JsonProperty("repository")]
        public string Repository { get; set; } = "";

        [JsonProperty("filename")]
        public string Filename { get; set; } = "";

        [JsonProperty("target-api")]
        public int TargetAPI { get; set; } = 0;

        [JsonProperty("md5")]
        public string Md5Checksum { get; set; } = "";

    }
}
