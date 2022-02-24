using SuchByte.MacroDeck.ExtensionStore;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Plugins;
using System;
using System.Collections.Generic;
using System.Text;

namespace SuchByte.MacroDeck.Extension
{
    public class PluginExtension : IMacroDeckExtension
    {
        public ExtensionType ExtensionType => ExtensionType.Plugin;
        public string ExtensionTypeDisplayName => LanguageManager.Strings.Plugin;
        public object ExtensionObject { get; set; }

        public bool Configurable => this.ExtensionObject != null && (this.ExtensionObject as MacroDeckPlugin).CanConfigure;

        public PluginExtension(MacroDeckPlugin macroDeckPlugin)
        {
            this.ExtensionObject = macroDeckPlugin;
        }

        public void Uninstall()
        {

        }
    }
}
