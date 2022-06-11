using SuchByte.MacroDeck.GUI.Dialogs;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    public partial class VariableItem : RoundedUserControl
    {

        public Variables.Variable Variable;

        public VariableItem(Variables.Variable variable)
        {
            this.Variable = variable;
            InitializeComponent();
        }

        public new void Update()
        {
            this.Invoke(new Action(() =>
            {
                this.lblName.Text = this.Variable.Name;
                this.lblType.Text = this.Variable.Type.ToString();
                this.lblValue.Text = this.Variable.Value;
                this.lblCreator.Text = this.Variable.Creator;
            }));
        }

        private void BtnEdit_Click(object sender, EventArgs e)
        {
            using (var variableDialog = new VariableDialog(this.Variable))
            {
                variableDialog.ShowDialog();
            }
        }

        private void VariableItem_Load(object sender, EventArgs e)
        {
            this.Update();
        }
    }
}
