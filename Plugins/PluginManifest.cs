using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Serialization;
using System.Linq;

namespace SuchByte.MacroDeck.Plugins
{
    public class PluginManifest
    {

        public string PackageId = ""; // PackageId of the plugin
        public string Author = ""; // Author of the plugin
        public string Repository = ""; // Repository of the plugin
        public string MainFile = ""; // Main file e.g. "MyPlugin.dll"
        public static PluginManifest FromZipFilePath(string zipFilePath)
        {
            Stream stream = new System.IO.StreamReader(
                System.IO.Compression.ZipFile.OpenRead(zipFilePath)
                                                .Entries.Where(x => x.Name.Equals("Plugin.xml", StringComparison.InvariantCulture))
                                                .FirstOrDefault()
                                                .Open(), Encoding.UTF8).BaseStream;
            return FromXmlStream(stream);
        }

        public static PluginManifest FromXmlStream(Stream stream)
        {
            return (PluginManifest)new XmlSerializer(typeof(PluginManifest)).Deserialize(stream);
        }
    }
}
