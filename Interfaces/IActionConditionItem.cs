using SuchByte.MacroDeck.Plugins;
using System;
using System.Collections.Generic;
using System.Text;

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
