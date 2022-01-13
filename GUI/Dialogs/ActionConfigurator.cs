using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Logging;
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
                        SetExpand(plugin, true);
                        foreach (Control item in this.pluginsList.Controls)
                        {
                            if (!(item is ActionConfiguratorActionItem)) continue;
                            if ((item as ActionConfiguratorActionItem).PluginAction.GetType() == this._action.GetType())
                            {
                                ActionConfiguratorActionItem_MouseClick(item, new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                            }
                        }
                    }
                }
            }
        }

        private void Filter(string filter)
        {
            if (pluginSearch.Text.Length > 1)
            {
                foreach (Control item in this.pluginsList.Controls)
                {
                    if (!(item is ActionConfiguratorPluginItem)) continue;
                    item.Visible = StringSearch.StringContains(((item as ActionConfiguratorPluginItem).Plugin.Name), filter);
                }
                foreach (Control item in this.pluginsList.Controls)
                {
                    if (!(item is ActionConfiguratorActionItem)) continue;
                    item.Visible = StringSearch.StringContains((item as ActionConfiguratorActionItem).PluginAction.Name, filter);
                    if (item.Visible)
                    {
                        SetExpand((item as ActionConfiguratorActionItem).Plugin, true);
                    }
                }
            } else
            {
                foreach (Control item in this.pluginsList.Controls)
                {
                    if (!(item is ActionConfiguratorPluginItem)) continue;
                    item.Visible = true;
                    SetExpand(item as ActionConfiguratorPluginItem, false);
                }
            }
        }
        
        private void AddPlugins()
        {
            this.SuspendLayout();
            foreach (Control item in this.pluginsList.Controls)
            {
                if (item is ActionConfiguratorActionItem)
                {
                    item.MouseClick -= ActionConfiguratorActionItem_MouseClick;
                } else if (item is ActionConfiguratorPluginItem)
                {
                    item.MouseClick -= ActionConfiguratorPluginItem_MouseClick;
                }
            }

            this.pluginsList.Controls.Clear();

            foreach (MacroDeckPlugin plugin in PluginManager.Plugins.Values)
            {
                if (plugin.Actions.Count > 0)
                {
                    ActionConfiguratorPluginItem actionConfiguratorPluginItem = new ActionConfiguratorPluginItem(plugin);
                    actionConfiguratorPluginItem.MouseClick += ActionConfiguratorPluginItem_MouseClick;
                    this.pluginsList.Controls.Add(actionConfiguratorPluginItem);
                    foreach (var action in plugin.Actions)
                    {
                        ActionConfiguratorActionItem actionConfiguratorActionItem = new ActionConfiguratorActionItem(plugin, PluginManager.GetNewActionInstance(action))
                        {
                            Visible = false
                        };
                        actionConfiguratorActionItem.MouseClick += ActionConfiguratorActionItem_MouseClick;
                        this.pluginsList.Controls.Add(actionConfiguratorActionItem);
                    }
                }
            }
            this.ResumeLayout();
        }

        private void ActionConfiguratorActionItem_MouseClick(object sender, MouseEventArgs e)
        {
            ActionConfiguratorActionItem actionConfiguratorActionItem = sender as ActionConfiguratorActionItem;

            if (actionConfiguratorActionItem.PluginAction == null) return;
            if (this._action == null || this._action.GetType() != actionConfiguratorActionItem.PluginAction.GetType())
            {
                this._action = actionConfiguratorActionItem.PluginAction;
            }

            this.selectedPluginIcon.BackgroundImage = actionConfiguratorActionItem.Plugin.Icon ?? Properties.Resources.Icon;
            this.lblSelectedActionName.Text = this._action.Name;
            this.labelDescription.Text = this._action.Description;
            foreach (Control control in this.configurationPanel.Controls)
            {
                control.Dispose();
            }
            this.configurationPanel.Controls.Clear();
            if (this._action.CanConfigure)
            {
                this.configurationPanel.Controls.Add(this._action.GetActionConfigControl(this));
            }
            else
            {
                Label noConfigure = new Label
                {
                    Text = Language.LanguageManager.Strings.ActionNeedsNoConfiguration,
                    Size = this.configurationPanel.Size,
                    TextAlign = ContentAlignment.MiddleCenter
                };
                this.configurationPanel.Controls.Add(noConfigure);
            }
        }

        private void ActionConfiguratorPluginItem_MouseClick(object sender, MouseEventArgs e)
        {
            ActionConfiguratorPluginItem actionConfiguratorPluginItem = sender as ActionConfiguratorPluginItem;
            SetExpand(actionConfiguratorPluginItem, !actionConfiguratorPluginItem.Selected);
            
        }

        private void SetExpand(ActionConfiguratorPluginItem actionConfiguratorPluginItem, bool expand)
        {
            actionConfiguratorPluginItem.Selected = expand;
            actionConfiguratorPluginItem.Visible = true;
            this.pluginsList.ScrollControlIntoView(actionConfiguratorPluginItem);
            foreach (var actionConfiguratorActionItem in this.pluginsList.Controls)
            {
                if (!(actionConfiguratorActionItem is ActionConfiguratorActionItem) || !(actionConfiguratorActionItem as ActionConfiguratorActionItem).Plugin.Equals(actionConfiguratorPluginItem.Plugin)) continue;
                (actionConfiguratorActionItem as ActionConfiguratorActionItem).Visible = actionConfiguratorPluginItem.Selected;
            }
        }

        private void SetExpand(MacroDeckPlugin plugin, bool expand)
        {
            ActionConfiguratorPluginItem actionConfiguratorPluginItem = null;

            foreach (Control control in this.pluginsList.Controls)
            {
                if (!(control is ActionConfiguratorPluginItem)) continue;
                if ((control as ActionConfiguratorPluginItem).Plugin.Equals(plugin))
                {
                    actionConfiguratorPluginItem = (ActionConfiguratorPluginItem)control;
                }
            }
            if (actionConfiguratorPluginItem != null)
            {
                SetExpand(actionConfiguratorPluginItem, expand);
            }
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            if (this._action != null)
            {
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

        private void PluginSearch_TextChanged(object sender, EventArgs e)
        {
            this.Filter(this.pluginSearch.Text);
        }
    }
}
