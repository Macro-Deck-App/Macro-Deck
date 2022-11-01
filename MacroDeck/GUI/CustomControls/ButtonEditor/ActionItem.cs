using System;
using System.Windows.Forms;
using SuchByte.MacroDeck.Interfaces;
using SuchByte.MacroDeck.Plugins;

namespace SuchByte.MacroDeck.GUI.CustomControls;

public partial class ActionItem : UserControl, IActionConditionItem
{
    public PluginAction Action { get; set; }

    public event EventHandler OnRemoveClick;
    public event EventHandler OnEditClick;
    public event EventHandler OnMoveUpClick;
    public event EventHandler OnMoveDownClick;


    public ActionItem(PluginAction macroDeckAction)
    {
        Action = macroDeckAction;
        InitializeComponent();
        lblPlugin.Text = PluginManager.GetPluginByAction(Action).Name;
        lblAction.Text = Action.Name;
        lblConfigurationSummary.Text = Action.ConfigurationSummary;
    }


    private void BtnRemove_Click(object sender, EventArgs e)
    {
        OnRemoveClick?.Invoke(this, EventArgs.Empty);
    }

    private void BtnEdit_Click(object sender, EventArgs e)
    {
        OnEditClick?.Invoke(this, EventArgs.Empty);
    }

    private void btnUp_Click(object sender, EventArgs e)
    {
        OnMoveUpClick?.Invoke(this, EventArgs.Empty);
    }

    private void btnDown_Click(object sender, EventArgs e)
    {
        OnMoveDownClick?.Invoke(this, EventArgs.Empty);
    }
}