using System;
using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Properties;
using SuchByte.MacroDeck.Utils;

namespace SuchByte.MacroDeck.GUI;

public partial class ActionConfigurator : DialogForm
{
    public PluginAction Action => _action;

    private PluginAction _action;

    public ActionConfigurator()
    {
        InitializeComponent();
        lblSelectToBegin.Text = LanguageManager.Strings.SelectAPluginAndActionToBegin;
        btnApply.Text = LanguageManager.Strings.Ok;
        pluginSearch.PlaceHolderText = LanguageManager.Strings.Search;
        AddPlugins();
    }

       

    public ActionConfigurator(PluginAction action)
    {
        _action = action;
        InitializeComponent();

        AddPlugins();


        foreach (var plugin in PluginManager.Plugins.Values)
        {
            foreach (var macroDeckAction in plugin.Actions)
            {
                if (macroDeckAction.GetType().Equals(_action.GetType()))
                {
                    SetExpand(plugin, true);
                    foreach (Control item in pluginsList.Controls)
                    {
                        if (!(item is ActionConfiguratorActionItem)) continue;
                        if ((item as ActionConfiguratorActionItem).PluginAction.GetType() == _action.GetType())
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
            foreach (Control item in pluginsList.Controls)
            {
                if (!(item is ActionConfiguratorPluginItem)) continue;
                item.Visible = StringSearch.StringContains(((item as ActionConfiguratorPluginItem).Plugin.Name), filter);
            }
            foreach (Control item in pluginsList.Controls)
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
            foreach (Control item in pluginsList.Controls)
            {
                if (!(item is ActionConfiguratorPluginItem)) continue;
                item.Visible = true;
                SetExpand(item as ActionConfiguratorPluginItem, false);
            }
        }
    }
        
    private void AddPlugins()
    {
        SuspendLayout();
        foreach (Control item in pluginsList.Controls)
        {
            if (item is ActionConfiguratorActionItem)
            {
                item.MouseClick -= ActionConfiguratorActionItem_MouseClick;
            } else if (item is ActionConfiguratorPluginItem)
            {
                item.MouseClick -= ActionConfiguratorPluginItem_MouseClick;
            }
        }

        pluginsList.Controls.Clear();

        foreach (var plugin in PluginManager.Plugins.Values)
        {
            if (plugin.Actions.Count > 0)
            {
                var actionConfiguratorPluginItem = new ActionConfiguratorPluginItem(plugin);
                actionConfiguratorPluginItem.MouseClick += ActionConfiguratorPluginItem_MouseClick;
                pluginsList.Controls.Add(actionConfiguratorPluginItem);
                foreach (var action in plugin.Actions)
                {
                    var actionConfiguratorActionItem = new ActionConfiguratorActionItem(plugin, PluginManager.GetNewActionInstance(action))
                    {
                        Visible = false
                    };
                    actionConfiguratorActionItem.MouseClick += ActionConfiguratorActionItem_MouseClick;
                    pluginsList.Controls.Add(actionConfiguratorActionItem);
                }
            }
        }
        ResumeLayout();
    }

    private void ActionConfiguratorActionItem_MouseClick(object sender, MouseEventArgs e)
    {
        var actionConfiguratorActionItem = sender as ActionConfiguratorActionItem;

        if (actionConfiguratorActionItem.PluginAction == null) return;
        if (_action == null || _action.GetType() != actionConfiguratorActionItem.PluginAction.GetType())
        {
            _action = actionConfiguratorActionItem.PluginAction;
        }

        selectedPluginIcon.BackgroundImage = actionConfiguratorActionItem.Plugin.PluginIcon ?? Resources.Icon;
        lblSelectedActionName.Text = _action.Name;
        labelDescription.Text = _action.Description;
        foreach (Control control in configurationPanel.Controls)
        {
            control.Dispose();
        }
        configurationPanel.Controls.Clear();
        if (_action.CanConfigure)
        {
            configurationPanel.Controls.Add(_action.GetActionConfigControl(this));
        }
        else
        {
            var noConfigure = new Label
            {
                Text = LanguageManager.Strings.ActionNeedsNoConfiguration,
                Size = configurationPanel.Size,
                TextAlign = ContentAlignment.MiddleCenter
            };
            configurationPanel.Controls.Add(noConfigure);
        }
    }

    private void ActionConfiguratorPluginItem_MouseClick(object sender, MouseEventArgs e)
    {
        var actionConfiguratorPluginItem = sender as ActionConfiguratorPluginItem;
        SetExpand(actionConfiguratorPluginItem, !actionConfiguratorPluginItem.Selected);
            
    }

    private void SetExpand(ActionConfiguratorPluginItem actionConfiguratorPluginItem, bool expand)
    {
        actionConfiguratorPluginItem.Selected = expand;
        actionConfiguratorPluginItem.Visible = true;
        pluginsList.ScrollControlIntoView(actionConfiguratorPluginItem);
        foreach (var actionConfiguratorActionItem in pluginsList.Controls)
        {
            if (!(actionConfiguratorActionItem is ActionConfiguratorActionItem) || !(actionConfiguratorActionItem as ActionConfiguratorActionItem).Plugin.Equals(actionConfiguratorPluginItem.Plugin)) continue;
            (actionConfiguratorActionItem as ActionConfiguratorActionItem).Visible = actionConfiguratorPluginItem.Selected;
        }
    }

    private void SetExpand(MacroDeckPlugin plugin, bool expand)
    {
        ActionConfiguratorPluginItem actionConfiguratorPluginItem = null;

        foreach (Control control in pluginsList.Controls)
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
        if (_action != null)
        {
            var actionConfigControl = configurationPanel.Controls[0] as ActionConfigControl;
            if (_action.CanConfigure && actionConfigControl != null)
            {
                if (!actionConfigControl.OnActionSave())
                {
                    return;
                }
            }
            if (_action.CanConfigure && _action.Configuration == null && string.IsNullOrWhiteSpace(_action.Configuration)) return;
            DialogResult = DialogResult.OK;
        }
        Close();
    }

    private void PluginSearch_TextChanged(object sender, EventArgs e)
    {
        Filter(pluginSearch.Text);
    }
}