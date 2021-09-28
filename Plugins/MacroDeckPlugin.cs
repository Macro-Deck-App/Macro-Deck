using SuchByte.MacroDeck.GUI;
using SuchByte.MacroDeck.GUI.CustomControls;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace SuchByte.MacroDeck.Plugins
{
    

   public interface IMacroDeckPlugin
    {
        string Name { get; }
        string Version { get; }
        string Author { get; }
        string Description { get; }
        List<IMacroDeckAction> Actions { get; }
        Image Icon { get; }
        bool CanConfigure { get; }
        void OpenConfigurator();
        void Enable();

    }

    public interface IMacroDeckAction
    {
        string Name { get; }
        string Description { get; }
        string DisplayName { get; set; }
        void Trigger(string clientId, ActionButton.ActionButton actionButton);
        string Configuration { get; set; }
        bool CanConfigure { get; }
        ActionConfigControl GetActionConfigurator(ActionConfigurator actionConfigurator);
    }

 
}
