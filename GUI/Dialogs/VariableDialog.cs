using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Variables;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    public partial class VariableDialog : DialogForm
    {
        public Variable Variable;

        private readonly bool _protected = false;
        private readonly bool _edit = false;

        public VariableDialog(Variable variable = null)
        {
            InitializeComponent();
            this.lblType.Text = Language.LanguageManager.Strings.Type;
            this.lblName.Text = Language.LanguageManager.Strings.Name;
            this.lblValue.Text = Language.LanguageManager.Strings.Value;
            this.btnDelete.Text = Language.LanguageManager.Strings.DeleteVariable;
            this.btnOk.Text = Language.LanguageManager.Strings.Ok;
            if (variable == null)
            {
                this.Variable = new Variable();
                this.variableName.Enabled = true;
                this._edit = false;
            } else
            {
                this.Variable = variable;
                this.variableName.Enabled = false;
                this._edit = true;
            }
            
            this._protected = (this.Variable.Creator != "User");
            this.variableType.Enabled = !this._protected;
            this.variableValue.Enabled = !this._protected;
        }


        private void VariableName_TextChanged(object sender, System.EventArgs e)
        {
            
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            if (this._protected)
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
                return;
            }

            if (!this._edit)
            {
                if (this.variableName.Text.Length == 0)
                {
                    this.variableName.Text = "new_variable";
                }
                int variableCount = VariableManager.ListVariables.ToList().FindAll(v => v.Name.Equals(this.variableName.Text)).Count;
                if (variableCount > 0)
                {
                    variableName.Text = string.Format(variableName.Text + " _{0}", variableCount);
                }
                this.Variable.Name = VariableManager.ConvertNameString(this.variableName.Text);
            }

            this.Variable.Type = this.variableType.Text;
            switch (this.Variable.Type)
            {
                case "Bool":
                    this.Variable.Value = (variableValue.Text.ToLower().Equals("true") ? true : false).ToString();
                    break;
                case "Integer":
                    Int32.TryParse(variableValue.Text, out int intVal);
                    this.Variable.Value = intVal.ToString();
                    break;
                case "Float":
                    float.TryParse(variableValue.Text, out float floatVal);
                    this.Variable.Value = floatVal.ToString();
                    break;
                case "String":
                    this.Variable.Value = variableValue.Text;
                    break;
            }

            VariableManager.SetValue(this.Variable.Name, this.Variable.Value, this.Variable.VariableType, this.Variable.Creator);

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void VariableDialog_Load(object sender, EventArgs e)
        {
            foreach (string name in Enum.GetNames(typeof(Variables.VariableType)))
            {
                this.variableType.Items.Add(name);
            }
            this.variableType.Text = this.Variable.Type;
            this.variableName.Text = this.Variable.Name;
            this.variableValue.Text = this.Variable.Value;
            CenterToScreen();
        }

        private void BtnDelete_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            using (var msgBox = new CustomControls.MessageBox())
            {
                if(msgBox.ShowDialog(Language.LanguageManager.Strings.AreYouSure, String.Format(Language.LanguageManager.Strings.VariableXGetsDeleted, this.Variable.Name), MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    VariableManager.DeleteVariable(this.Variable.Name);
                    this.DialogResult = DialogResult.Cancel;
                    this.Close();
                }
            }
        }

        private void variableType_SelectedIndexChanged(object sender, EventArgs e)
        {
            switch (this.variableType.Text)
            {
                case "Bool":
                    this.variableValue.Text = "false";
                    break;
                case "Integer":
                    this.variableValue.Text = "0";
                    break;
                case "Float":
                    this.variableValue.Text = "0.0";
                    break;
                case "String":
                    this.variableValue.Text = "";
                    break;
            }
        }
    }
}
