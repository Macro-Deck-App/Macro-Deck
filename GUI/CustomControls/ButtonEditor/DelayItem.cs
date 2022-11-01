using System;
using System.Windows.Forms;
using SuchByte.MacroDeck.ActionButton.Plugin;
using SuchByte.MacroDeck.Interfaces;
using SuchByte.MacroDeck.Plugins;

namespace SuchByte.MacroDeck.GUI.CustomControls.ButtonEditor;

public partial class DelayItem : UserControl, IActionConditionItem
{
    public PluginAction Action { get; set; }

    public event EventHandler OnRemoveClick;
    public event EventHandler OnEditClick;
    public event EventHandler OnMoveUpClick;
    public event EventHandler OnMoveDownClick;

    public DelayItem(PluginAction macroDeckAction = null)
    {
        Action = macroDeckAction;
        InitializeComponent();
        if (Action != null)
        {
            millis.ValueChanged -= DelayValueChanged;
            seconds.ValueChanged -= DelayValueChanged;
            minutes.ValueChanged -= DelayValueChanged;
            try
            {
                var t = TimeSpan.FromMilliseconds(double.Parse(Action.Configuration));
                millis.Value = t.Milliseconds;
                seconds.Value = t.Seconds;
                minutes.Value = t.Minutes;
            }
            catch { }

            millis.ValueChanged += DelayValueChanged;
            seconds.ValueChanged += DelayValueChanged;
            minutes.ValueChanged += DelayValueChanged;
        } else
        {
            Action = new DelayAction
            {
                Configuration = (1000).ToString(),
            };
        }
    }

    private void DelayValueChanged(object sender, EventArgs e)
    {
        Action.Configuration = (millis.Value + seconds.Value * 1000 + minutes.Value * 1000 * 60).ToString();
    }

    private void BtnRemove_Click(object sender, EventArgs e)
    {
        OnRemoveClick?.Invoke(this, EventArgs.Empty);
    }


    private void BtnUp_Click(object sender, EventArgs e)
    {
        OnMoveUpClick?.Invoke(this, EventArgs.Empty);
    }

    private void BtnDown_Click(object sender, EventArgs e)
    {
        OnMoveDownClick?.Invoke(this, EventArgs.Empty);
    }
}