using System;
using System.Windows.Forms;
using SuchByte.MacroDeck.Plugins;

namespace SuchByte.MacroDeck.GUI.CustomControls;

public partial class ActionConfiguratorActionItem : RoundedUserControl
{
    public PluginAction PluginAction { get; set; }

    public MacroDeckPlugin Plugin { get; set; }


    public ActionConfiguratorActionItem(MacroDeckPlugin plugin, PluginAction pluginAction)
    {
        Plugin ??= plugin;
        PluginAction ??= pluginAction;
        InitializeComponent();
        DoubleBuffered = true;
        lblActionName.MouseClick += Control_MouseClick;
    }

    private void Control_MouseClick(object sender, MouseEventArgs e)
    {
        OnMouseClick(e);
    }

    private void ActionConfiguratorActionItem_Load(object sender, EventArgs e)
    {
        lblActionName.Text = PluginAction.Name;
    }
}