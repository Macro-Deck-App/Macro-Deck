using System;
using SuchByte.MacroDeck.Plugins;

namespace SuchByte.MacroDeck.Interfaces
{
    public interface IActionConditionItem
    {
        PluginAction Action { get; set; }
        event EventHandler OnRemoveClick;
        event EventHandler OnEditClick;
        event EventHandler OnMoveUpClick;
        event EventHandler OnMoveDownClick;
    }
}
