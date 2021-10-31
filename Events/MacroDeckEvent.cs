using SuchByte.MacroDeck.Plugins;
using System;
using System.Collections.Generic;
using System.Text;

namespace SuchByte.MacroDeck.Events
{

    public class MacroDeckEventArgs : EventArgs
    {
        public ActionButton.ActionButton ActionButton { get; set; }
        
    }

    public interface IMacroDeckEvent
    {

        string Name { get; }
        EventHandler<MacroDeckEventArgs> OnEvent { get; set; }


    }
}
