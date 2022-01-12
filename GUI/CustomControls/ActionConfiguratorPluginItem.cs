using SuchByte.MacroDeck.Language;
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
    public partial class ActionConfiguratorPluginItem : RoundedUserControl
    {
        private bool selected = false;

        public MacroDeckPlugin Plugin { get; set; }

        public bool Selected 
        { 
            get
            {
                return this.selected;
            }
            set
            {
                this.selected = value;
                this.chevron.BackgroundImage = this.selected ? Properties.Resources.Chevron_Down : Properties.Resources.Chevron_Right;
            }
        }

        public ActionConfiguratorPluginItem(MacroDeckPlugin macroDeckPlugin)
        {
            this.Plugin ??= macroDeckPlugin;
            InitializeComponent();
            this.DoubleBuffered = true;

            this.pluginIcon.MouseClick += Control_MouseClick;
            this.pluginName.MouseClick += Control_MouseClick;
            this.lblCountActions.MouseClick += Control_MouseClick;
            this.chevron.MouseClick += Control_MouseClick;
        }


        private void Control_MouseClick(object sender, MouseEventArgs e)
        {
            this.OnMouseClick(e);
        }

        private void ActionConfiguratorPluginItem_Load(object sender, EventArgs e)
        {
            if (this.Plugin == null) return;
            this.pluginIcon.BackgroundImage = this.Plugin.Icon ?? Properties.Resources.Icon;
            this.pluginName.Text = this.Plugin.Name;
            this.lblCountActions.Text = String.Format((this.Plugin.Actions.Count == 1 ? LanguageManager.Strings.XAction : LanguageManager.Strings.XActions), this.Plugin.Actions.Count);
        }


    }
}
