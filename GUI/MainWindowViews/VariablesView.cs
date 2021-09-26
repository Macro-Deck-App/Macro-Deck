using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.Variables;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.MainWindowContents
{
    public partial class VariablesView : UserControl
    {
        public VariablesView()
        {
            InitializeComponent();
            this.UpdateTranslation();
        }

        public void UpdateTranslation()
        {
            this.Name = Language.LanguageManager.Strings.VariablesTitle;
            this.lblName.Text = Language.LanguageManager.Strings.Name;
            this.lblType.Text = Language.LanguageManager.Strings.Type;
            this.lblValue.Text = Language.LanguageManager.Strings.Value;
            this.lblCreator.Text = Language.LanguageManager.Strings.Creator;
            this.btnCreateVariable.Text = Language.LanguageManager.Strings.CreateVariable;
        }

        private void VariablesPage_Load(object sender, EventArgs e)
        {
            LoadVariables();
            VariableManager.OnVariableChanged += this.VariableChanged;
            VariableManager.OnVariableRemoved += this.VariableRemoved;
        }

        private void VariableRemoved(object sender, EventArgs e) {
            Task.Run(() =>
            {
                foreach (VariableItem variableItem in this.variablesPanel.Controls)
                {
                    if (sender.Equals(variableItem.Variable.Name))
                    {
                        this.Invoke(new Action(() =>
                        {
                            this.variablesPanel.Controls.Remove(variableItem);
                        }));
                        return;
                    }
                }
            });
        }

        private void VariableChanged(object sender, EventArgs e)
        {
            Variable variable = sender as Variable;
            Task.Run(() =>
            {
                foreach (VariableItem variableItem in this.variablesPanel.Controls)
                {
                    if (variable.Name.Equals(variableItem.Variable.Name))
                    {
                        this.Invoke(new Action(() =>
                        {
                            if (this == null || this.Disposing || this.IsDisposed) return;
                            variableItem.Variable = variable;
                            variableItem.Update();
                        }));
                        return;
                    }
                }
                VariableItem newVariableItem = new VariableItem(variable);
                this.Invoke(new Action(() =>
                {
                    this.variablesPanel.Controls.Add(newVariableItem);
                }));
            });
        }

        private void LoadVariables()
        {
            this.variablesPanel.Controls.Clear();
            foreach (Variable variable in VariableManager.Variables)
            {
                VariableItem variableItem = new VariableItem(variable);
                this.variablesPanel.Controls.Add(variableItem);
            }
        }

        private void BtnCreateVariable_Click(object sender, EventArgs e)
        {
            using (var variableDialog = new VariableDialog())
            {
                if (variableDialog.ShowDialog() == DialogResult.OK)
                {
                    VariableManager.Variables.Add(variableDialog.Variable);
                }
            }
        }
    }
}
