using SuchByte.MacroDeck.Plugins;
using System;
using System.Collections.Generic;
using System.Text;

namespace SuchByte.MacroDeck.Events
{
    public class EventListener
    {
        public string EventToListen { get; set; }
        public string Parameter { get; set; } = "";

        public List<PluginAction> Actions = new List<PluginAction>();

    }
}
