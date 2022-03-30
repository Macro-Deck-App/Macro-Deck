using SuchByte.MacroDeck.Plugins;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    public partial class ActionConfiguratorActionItem : RoundedUserControl
    {
        public PluginAction PluginAction { get; set; }

        public MacroDeckPlugin Plugin { get; set; }


        public ActionConfiguratorActionItem(MacroDeckPlugin plugin, PluginAction pluginAction)
        {
            this.Plugin ??= plugin;
            this.PluginAction ??= pluginAction;
            InitializeComponent();
            this.DoubleBuffered = true;
            this.lblActionName.MouseClick += Control_MouseClick;
        }

        private void Control_MouseClick(object sender, MouseEventArgs e)
        {
            this.OnMouseClick(e);
        }

        private void ActionConfiguratorActionItem_Load(object sender, EventArgs e)
        {
            this.lblActionName.Text = this.PluginAction.Name;
        }
    }
}
