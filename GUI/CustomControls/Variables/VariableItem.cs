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
            this.SuspendLayout();
            this.lblName.Text = Language.LanguageManager.Strings.Name;
            this.lblType.Text = Language.LanguageManager.Strings.Type;
            this.lblValue.Text = Language.LanguageManager.Strings.Value;
            this.lblCreator.Text = Language.LanguageManager.Strings.Creator;
            this.Update();
            this.ResumeLayout();
        }

        public new void Update()
        {
            this.lblName.Text = this.Variable.Name;
            this.lblType.Text = this.Variable.Type.ToString();
            this.lblValue.Text = this.Variable.Value;
            this.lblCreator.Text = this.Variable.Creator;
        }

        private void BtnEdit_Click(object sender, EventArgs e)
        {
            using (var variableDialog = new VariableDialog(this.Variable))
            {
                variableDialog.ShowDialog();
            }
        }
    }
}
