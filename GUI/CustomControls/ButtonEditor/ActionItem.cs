using SuchByte.MacroDeck.Plugins;
using System;
using SuchByte.MacroDeck.Interfaces;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    public partial class ActionItem : UserControl, IActionConditionItem
    {
        public PluginAction Action { get; set; }

        public event EventHandler OnRemoveClick;
        public event EventHandler OnEditClick;
        public event EventHandler OnMoveUpClick;
        public event EventHandler OnMoveDownClick;


        public ActionItem(PluginAction macroDeckAction)
        {
            this.Action = macroDeckAction;
            InitializeComponent();
            this.lblPlugin.Text = PluginManager.GetPluginByAction(this.Action).Name;
            this.lblAction.Text = this.Action.Name;
            this.lblConfigurationSummary.Text = this.Action.ConfigurationSummary;
        }


        private void BtnRemove_Click(object sender, EventArgs e)
        {
            if (this.OnRemoveClick != null)
            {
                this.OnRemoveClick(this, EventArgs.Empty);
            }
        }

        private void BtnEdit_Click(object sender, EventArgs e)
        {
            if (this.OnEditClick != null)
            {
                this.OnEditClick(this, EventArgs.Empty);
            }
        }

        private void btnUp_Click(object sender, EventArgs e)
        {
            if (this.OnMoveUpClick != null)
            {
                this.OnMoveUpClick(this, EventArgs.Empty);
            }
        }

        private void btnDown_Click(object sender, EventArgs e)
        {
            if (this.OnMoveDownClick != null)
            {
                this.OnMoveDownClick(this, EventArgs.Empty);
            }
        }
    }
}
