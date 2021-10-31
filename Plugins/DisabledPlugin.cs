using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace SuchByte.MacroDeck.Plugins
{
    public class DisabledPlugin : MacroDeckPlugin
    {
        public override string Name { get; set; }
        public override string Version { get; set; }
        public override string Author { get; set; }
        public override void Enable() {}
        public override string Description => Language.LanguageManager.Strings.PluginCouldNotBeLoaded;
        public override Image Icon => Properties.Resources.Macro_Deck_error;

    }
}
