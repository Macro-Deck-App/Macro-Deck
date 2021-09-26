using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI
{
    public partial class ActionConfigurator : DialogForm
    {
        public IMacroDeckAction Action { get { return this._action; } }

        private IMacroDeckAction _action = null;

        public event EventHandler ActionSave;
        public ActionConfigurator()
        {
            InitializeComponent();
            this.lblSelectToBegin.Text = Language.LanguageManager.Strings.SelectAPluginAndActionToBegin;
            this.btnApply.Text = Language.LanguageManager.Strings.Ok;
            this.AddPlugins();
        }

       

        public ActionConfigurator(IMacroDeckAction action)
        {
            this._action = action;
            this.InitializeComponent();


            this.AddPlugins();


            foreach (IMacroDeckPlugin plugin in PluginManager.Plugins.Values)
            {
                foreach (IMacroDeckAction macroDeckAction in plugin.Actions)
                {
                    if (macroDeckAction.GetType().Equals(this._action.GetType()))
                    {
                        this.pluginList.FindItemWithText(plugin.Name).Selected = true;
                        this.pluginList.Select();
                        //this.pluginList.SetSelected(this.pluginList.Items.IndexOf(plugin.Name), true);
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
            foreach (IMacroDeckPlugin plugin in PluginManager.Plugins.Values)
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
            //if (this._action.CanConfigure && this._action.Configuration.Length < 1) return;
            if (this._action != null)
            {
                if (this.ActionSave != null)
                {
                    this.ActionSave(this._action, EventArgs.Empty);
                }
                this.DialogResult = DialogResult.OK;
            }
            this.Close();
        }

        private void ActionList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (actionList.SelectedItem == null) return;
            IMacroDeckAction newAction = PluginManager.GetActionByName(PluginManager.Plugins[pluginList.SelectedItems[0].Text.ToString()], this.actionList.SelectedItem.ToString());
            if (this._action == null || this._action.GetType() != newAction.GetType())
            {
                this._action = newAction;
            }
            if (this._action == null) return;
            this.labelDescription.Text = this._action.Description;
            this.configurationPanel.Controls.Clear();
            if (this._action.CanConfigure)
            {
                this.configurationPanel.Controls.Add(this._action.GetActionConfigurator(this));
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
            foreach (IMacroDeckAction action in PluginManager.Plugins[pluginList.SelectedItems[0].Text.ToString()].Actions)
            {
                this.actionList.Items.Add(action.Name);
            }
        }
    }
}
