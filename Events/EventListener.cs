using System.Collections.Generic;
using SuchByte.MacroDeck.Plugins;

namespace SuchByte.MacroDeck.Events;

public class EventListener
{
    public string EventToListen { get; set; }
    public string Parameter { get; set; } = "";

    public List<PluginAction> Actions = new();

}