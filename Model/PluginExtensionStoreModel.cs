using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace SuchByte.MacroDeck.Model
{
    public class PluginExtensionStoreModel
    {

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("version")]
        public string Version { get; set; } = "";

        [JsonProperty("description")]
        public string Description { get; set; } = "";

        [JsonProperty("author")]
        public string Author { get; set; } = "";

        [JsonProperty("changelog")]
        public string Changelog { get; set; } = "";

        [JsonProperty("uploaded")]
        public string UploadDate { get; set; } = "";

        [JsonProperty("filename")]
        public string Filename { get; set; } = "";

        [JsonProperty("icon")]
        public string IconBase64 { get; set; } = "";

        [JsonProperty("downloads")]
        public int Downloads { get; set; } = 0;

        [JsonProperty("repository")]
        public string RepositoryUri { get; set; } = "";

        [JsonProperty("target-api")]
        public int TargetApi = 0;

        [JsonProperty("uuid")]
        public string PackageId { get; set; } = "";

        [JsonProperty("rating")]
        public float Rating = 0.0f;

        [JsonProperty("category")]
        public string Category { get; set; } = "";

        [JsonProperty("md5")]
        public string MD5Checksum { get; set; } = "";


    }
}
