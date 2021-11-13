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
            this.Dock = DockStyle.Fill;
            this.UpdateTranslation();
            this.LoadCreators();
        }

        public void UpdateTranslation()
        {
            this.SuspendLayout();
            this.Name = Language.LanguageManager.Strings.VariablesTitle;
            this.lblName.Text = Language.LanguageManager.Strings.Name;
            this.lblType.Text = Language.LanguageManager.Strings.Type;
            this.lblValue.Text = Language.LanguageManager.Strings.Value;
            this.lblCreator.Text = Language.LanguageManager.Strings.Creator;
            this.btnCreateVariable.Text = Language.LanguageManager.Strings.CreateVariable;
            this.ResumeLayout();
        }

        private void LoadCreators()
        {
            foreach (CheckBox checkBox in this.creatorFilter.Controls)
            {
                checkBox.CheckedChanged -= CreatorCheckBox_CheckedChanged;
            }
            this.creatorFilter.Controls.Clear();
            List<string> variableCreators = new List<string>();
            foreach (Variable variable in VariableManager.Variables)
            {
                if (!variableCreators.Contains(variable.Creator)) {
                    variableCreators.Add(variable.Creator);
                }
            }
            foreach (string creator in variableCreators)
            {
                CheckBox creatorCheckBox = new CheckBox
                {
                    Checked = true,
                    Text = creator,
                    Name = creator,
                    AutoSize = true,
                    Padding = new Padding(10, 0, 10, 0),
                };
                this.creatorFilter.Controls.Add(creatorCheckBox);
                creatorCheckBox.CheckedChanged += CreatorCheckBox_CheckedChanged;
            }
        }

        private void CreatorCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            this.SuspendLayout();
            CheckBox checkBox = sender as CheckBox;

            foreach (VariableItem variableItem in this.variablesPanel.Controls)
            {
                if (variableItem.Variable.Creator.Equals(checkBox.Name))
                {
                    variableItem.Visible = checkBox.Checked;
                }
            }
            this.ResumeLayout();
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
                this.Invoke(new Action(() => this.SuspendLayout()));
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

                this.Invoke(new Action(() => this.ResumeLayout()));
            });
        }

        private void VariableChanged(object sender, EventArgs e)
        {
            Variable variable = sender as Variable;
            Task.Run(() =>
            {
                this.Invoke(new Action(() => this.SuspendLayout()));
                foreach (VariableItem variableItem in this.variablesPanel.Controls)
                {
                    if (variable.Name.Equals(variableItem.Variable.Name))
                    {
                        this.Invoke(new Action(() =>
                        {
                            try
                            {
                                if (this == null || this.Disposing || this.IsDisposed) return;
                                variableItem.Variable = variable;
                                variableItem.Update();
                            } catch { }
                        }));
                        return;
                    }
                }
                VariableItem newVariableItem = new VariableItem(variable);
                this.Invoke(new Action(() =>
                {
                    this.variablesPanel.Controls.Add(newVariableItem);
                    this.ResumeLayout();
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
