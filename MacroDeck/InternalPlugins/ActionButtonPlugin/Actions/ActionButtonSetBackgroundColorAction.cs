using System;
using System.Drawing;
using SuchByte.MacroDeck.GUI;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.InternalPlugins.ActionButtonPlugin.Enums;
using SuchByte.MacroDeck.InternalPlugins.ActionButtonPlugin.Models;
using SuchByte.MacroDeck.InternalPlugins.ActionButtonPlugin.Views;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Profiles;

namespace SuchByte.MacroDeck.InternalPlugins.ActionButtonPlugin.Actions;

public class ActionButtonSetBackgroundColorAction : PluginAction
{
    public override string Name => LanguageManager.Strings.ActionSetBackgroundColor;
    public override string Description => LanguageManager.Strings.ActionSetBackgroundColorDescription;
    public override bool CanConfigure => true;

    private Random random;

    public override void Trigger(string clientId, ActionButton.ActionButton actionButton)
    {
        var configModel = ActionButtonSetBackgroundColorActionConfigModel.Deserialize(Configuration);
        var color = Color.FromArgb(35,35,35);
        switch (configModel.Method)
        {
            case SetBackgroundColorMethod.Fixed:
                color = configModel.Color;
                break;
            case SetBackgroundColorMethod.Random:
                if (random == null) random = new Random();
                color = Color.FromArgb(random.Next(256), random.Next(256), random.Next(256));
                break;
        }
        if (actionButton.State)
        {
            actionButton.BackColorOn = color;
        } else
        {
            actionButton.BackColorOff = color;
        }
        ProfileManager.Save();
    }

    public override ActionConfigControl GetActionConfigControl(ActionConfigurator actionConfigurator)
    {
        return new ActionButtonSetBackgroundColorActionConfigView(this);
    }
}