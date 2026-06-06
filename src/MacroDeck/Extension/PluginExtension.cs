using SuchByte.MacroDeck.ExtensionStore;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Plugins;

namespace SuchByte.MacroDeck.Extension;

public class PluginExtension : IMacroDeckExtension
{
    public ExtensionType ExtensionType => ExtensionType.Plugin;
    public string ExtensionTypeDisplayName => LanguageManager.Strings.Plugin;
    public object ExtensionObject { get; set; }

    public bool Configurable => ExtensionObject != null && (ExtensionObject as MacroDeckPlugin).CanConfigure;

    public PluginExtension(MacroDeckPlugin macroDeckPlugin)
    {
        ExtensionObject = macroDeckPlugin;
    }

    public void Uninstall()
    {

    }
}