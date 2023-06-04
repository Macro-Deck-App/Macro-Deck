using System.IO;
using System.IO.Compression;
using System.Xml.Serialization;

namespace SuchByte.MacroDeck.Plugins;

[Obsolete("Will be removed in version 2.11.0")]
public class PluginManifest
{

    public string PackageId = ""; // PackageId of the plugin
    public string Author = ""; // Author of the plugin
    public string Repository = ""; // Repository of the plugin
    public string MainFile = ""; // Main file e.g. "MyPlugin.dll"

    public static PluginManifest FromManifestFile(string path)
    {
        using var s = File.OpenRead(path);
        return FromXmlStream(s);
    }

    public static PluginManifest FromZipFilePath(string zipFilePath)
    {
        var stream = new StreamReader(
            ZipFile.OpenRead(zipFilePath)
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