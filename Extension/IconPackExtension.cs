using SuchByte.MacroDeck.ExtensionStore;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Language;
using System;
using System.Collections.Generic;
using System.Text;

namespace SuchByte.MacroDeck.Extension
{
    public class IconPackExtension : IMacroDeckExtension
    {
        public ExtensionType ExtensionType => ExtensionType.IconPack;
        public string ExtensionTypeDisplayName => LanguageManager.Strings.IconPack;
        public object ExtensionObject { get; set; }
        public bool Configurable => false;

        public IconPackExtension(IconPack iconPack)
        {
            this.ExtensionObject = iconPack;
        }

        public void Uninstall()
        {

        }
    }
}
