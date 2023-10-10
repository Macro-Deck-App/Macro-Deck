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
    public PluginAction? Action { get; private set; }

    public ActionConfigurator(PluginAction? action = null)
    {
        Action = action;
        InitializeComponent();
        lblSelectToBegin.Text = LanguageManager.Strings.SelectAPluginAndActionToBegin;
        btnApply.Text = LanguageManager.Strings.Ok;
        pluginSearch.PlaceHolderText = LanguageManager.Strings.Search;
        Shown += OnShown;
    }

    private void OnShown(object? sender, EventArgs e)
    {
        Application.DoEvents();
        AddPlugins();
        if (Action is null)
        {
            
            return;
        }
        foreach (var plugin in from plugin in PluginManager.Plugins.Values
                 from macroDeckAction in plugin.Actions.Where(macroDeckAction =>
                     macroDeckAction.GetType() == Action.GetType())
                 select plugin)
        {
            SetExpand(plugin, true);
            foreach (Control item in pluginsList.Controls)
            {
                if (item is not ActionConfiguratorActionItem actionItem)
                {
                    continue;
                }
                    
                if (actionItem.PluginAction.GetType() == Action.GetType())
                {
                    ActionConfiguratorActionItem_MouseClick(actionItem, new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
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
    }

    private void ActionConfiguratorActionItem_MouseClick(object sender, MouseEventArgs e)
    {
        var actionConfiguratorActionItem = sender as ActionConfiguratorActionItem;

        if (actionConfiguratorActionItem.PluginAction == null) return;
        if (Action == null || Action.GetType() != actionConfiguratorActionItem.PluginAction.GetType())
        {
            Action = actionConfiguratorActionItem.PluginAction;
        }

        selectedPluginIcon.BackgroundImage = actionConfiguratorActionItem.Plugin.PluginIcon ?? Resources.Icon;
        lblSelectedActionName.Text = Action.Name;
        labelDescription.Text = Action.Description;
        foreach (Control control in configurationPanel.Controls)
        {
            control.Dispose();
        }
        configurationPanel.Controls.Clear();
        if (Action.CanConfigure)
        {
            configurationPanel.Controls.Add(Action.GetActionConfigControl(this));
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
        if (Action != null)
        {
            var actionConfigControl = configurationPanel.Controls[0] as ActionConfigControl;
            if (Action.CanConfigure && actionConfigControl != null)
            {
                if (!actionConfigControl.OnActionSave())
                {
                    return;
                }
            }
            if (Action.CanConfigure && Action.Configuration == null && string.IsNullOrWhiteSpace(Action.Configuration)) return;
            DialogResult = DialogResult.OK;
        }
        Close();
    }

    private void PluginSearch_TextChanged(object sender, EventArgs e)
    {
        Filter(pluginSearch.Text);
    }
}