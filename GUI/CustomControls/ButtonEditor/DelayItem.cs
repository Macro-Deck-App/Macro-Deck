using SuchByte.MacroDeck.ActionButton.Plugin;
using SuchByte.MacroDeck.Interfaces;
using SuchByte.MacroDeck.Plugins;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls.ButtonEditor
{
    public partial class DelayItem : UserControl, IActionConditionItem
    {
        public PluginAction Action { get; set; }

        public event EventHandler OnRemoveClick;
        public event EventHandler OnEditClick;
        public event EventHandler OnMoveUpClick;
        public event EventHandler OnMoveDownClick;

        public DelayItem(PluginAction macroDeckAction = null)
        {
            this.Action = macroDeckAction;
            InitializeComponent();
            if (this.Action != null)
            {
                this.millis.ValueChanged -= DelayValueChanged;
                this.seconds.ValueChanged -= DelayValueChanged;
                this.minutes.ValueChanged -= DelayValueChanged;
                try
                {
                    TimeSpan t = TimeSpan.FromMilliseconds(Double.Parse(this.Action.Configuration));
                    this.millis.Value = t.Milliseconds;
                    this.seconds.Value = t.Seconds;
                    this.minutes.Value = t.Minutes;
                }
                catch { }

                this.millis.ValueChanged += DelayValueChanged;
                this.seconds.ValueChanged += DelayValueChanged;
                this.minutes.ValueChanged += DelayValueChanged;
            } else
            {
                this.Action = new DelayAction
                {
                    Configuration = (millis.Value + seconds.Value * 1000 + minutes.Value * 1000 * 60).ToString(),
                };
            }
        }

        private void DelayValueChanged(object sender, EventArgs e)
        {
            this.Action.Configuration = (millis.Value + seconds.Value * 1000 + minutes.Value * 1000 * 60).ToString();
        }

        private void BtnRemove_Click(object sender, EventArgs e)
        {
            if (this.OnRemoveClick != null)
            {
                this.OnRemoveClick(this, EventArgs.Empty);
            }
        }


        private void BtnUp_Click(object sender, EventArgs e)
        {
            if (this.OnMoveUpClick != null)
            {
                this.OnMoveUpClick(this, EventArgs.Empty);
            }
        }

        private void BtnDown_Click(object sender, EventArgs e)
        {
            if (this.OnMoveDownClick != null)
            {
                this.OnMoveDownClick(this, EventArgs.Empty);
            }
        }
    }
}
