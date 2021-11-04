using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI
{
    public partial class ActionConfigurator : DialogForm
    {
        public PluginAction Action { get { return this._action; } }

        private PluginAction _action = null;

        [Obsolete("Replaced with the OnActionSave boolean of the PluginAction class; Will be removed soon")]
        public event EventHandler ActionSave;

        public ActionConfigurator()
        {
            InitializeComponent();
            this.lblSelectToBegin.Text = Language.LanguageManager.Strings.SelectAPluginAndActionToBegin;
            this.btnApply.Text = Language.LanguageManager.Strings.Ok;
            this.pluginSearch.PlaceHolderText = Language.LanguageManager.Strings.Search;
            this.AddPlugins();
        }

       

        public ActionConfigurator(PluginAction action)
        {
            this._action = action;
            this.InitializeComponent();

            this.AddPlugins();


            foreach (MacroDeckPlugin plugin in PluginManager.Plugins.Values)
            {
                foreach (PluginAction macroDeckAction in plugin.Actions)
                {
                    if (macroDeckAction.GetType().Equals(this._action.GetType()))
                    {
                        this.pluginList.FindItemWithText(plugin.Name).Selected = true;
                        this.pluginList.Select();
                        this.actionList.SetSelected(this.actionList.Items.IndexOf(macroDeckAction.Name), true);
                    }
                }
            }
        }
        
        private void AddPlugins(string search = "")
        {
            this.pluginList.Items.Clear();
            ImageList imageList = new ImageList();
            imageList.ImageSize = new Size(32, 32);
            this.pluginList.SmallImageList = imageList;
            int iconIndex = 0;
            foreach (MacroDeckPlugin plugin in PluginManager.Plugins.Values)
            {
                if (search.Length > 1 && !StringSearch.StringContains(plugin.Name, search)) continue;
                if (plugin.Actions.Count > 0)
                {
                    ListViewItem pluginItem = new ListViewItem();
                    pluginItem.Text = plugin.Name;
                    if (plugin.Icon == null)
                    {
                        imageList.Images.Add(Properties.Resources.Icon);
                    } else
                    {
                        imageList.Images.Add(plugin.Icon);
                    }
                    pluginItem.ImageIndex = iconIndex;
                    iconIndex++;
                    this.pluginList.Items.Add(pluginItem);
                }
            }
        }

        private void ActionConfigurator_Load(object sender, EventArgs e)
        {

        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            if (this._action != null)
            {
                if (this.ActionSave != null)
                {
                    this.ActionSave(this._action, EventArgs.Empty);
                }
                ActionConfigControl actionConfigControl = this.configurationPanel.Controls[0] as ActionConfigControl;
                if (this._action.CanConfigure && actionConfigControl != null)
                {
                    if (!actionConfigControl.OnActionSave())
                    {
                        return;
                    }
                }
                if (this._action.CanConfigure && this._action.Configuration == null && String.IsNullOrWhiteSpace(this._action.Configuration)) return;
                this.DialogResult = DialogResult.OK;
            }
            this.Close();
        }

        private void ActionList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (actionList.SelectedItem == null) return;
            PluginAction newAction = PluginManager.GetActionByName(PluginManager.GetPluginByName(pluginList.SelectedItems[0].Text.ToString()), this.actionList.SelectedItem.ToString());
            if (this._action == null || this._action.GetType() != newAction.GetType())
            {
                this._action = newAction;
            }
            if (this._action == null) return;
            this.labelDescription.Text = this._action.Description;
            foreach (Control control in this.configurationPanel.Controls)
            {
                control.Dispose();
            }
            this.configurationPanel.Controls.Clear();
            if (this._action.CanConfigure)
            {
                this.configurationPanel.Controls.Add(this._action.GetActionConfigControl(this));
            } else
            {
                Label noConfigure = new Label();
                noConfigure.Text = Language.LanguageManager.Strings.ActionNeedsNoConfiguration;
                noConfigure.Size = this.configurationPanel.Size;
                noConfigure.TextAlign = ContentAlignment.MiddleCenter;
                this.configurationPanel.Controls.Add(noConfigure);
            }
        }

        private void PluginSearch_TextChanged(object sender, EventArgs e)
        {
            if (pluginSearch.Text.Length > 1)
            {
                AddPlugins(this.pluginSearch.Text);
            } else
            {
                AddPlugins();
            }
        }

        private void PluginList_SelectedIndexChanged (object sender, EventArgs e)
        {
            if (this.pluginList.SelectedItems == null || pluginList.SelectedItems.Count == 0 || pluginList.SelectedItems[0] == null) return;
            actionList.Items.Clear();


            foreach (PluginAction action in PluginManager.GetPluginByName(pluginList.SelectedItems[0].Text.ToString()).Actions)
            {
                this.actionList.Items.Add(action.Name);
            }
        }
    }
}
