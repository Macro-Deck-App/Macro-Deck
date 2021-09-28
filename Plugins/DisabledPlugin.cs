using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace SuchByte.MacroDeck.Plugins
{
    public class DisabledPlugin : IMacroDeckPlugin
    {
        public string Name { get; set; }

        public string Version { get; set; }

        public string Author => "?";

        public string Description => Language.LanguageManager.Strings.PluginCouldNotBeLoaded;

        public List<IMacroDeckAction> Actions => null;

        public Image Icon => Properties.Resources.Macro_Deck_error;

        public bool CanConfigure => false;

        public void Enable() {}

        public void OpenConfigurator() {}
    }
}
