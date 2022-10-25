using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Models;
using SuchByte.MacroDeck.Properties;
using SuchByte.MacroDeck.Variables;

namespace SuchByte.MacroDeck.GUI.MainWindowContents
{
    public partial class VariablesView : UserControl
    {

        public VariablesView()
        {
            InitializeComponent();
            Dock = DockStyle.Fill;
            UpdateTranslation();
        }

        public void UpdateTranslation()
        {
            SuspendLayout();
            Name = LanguageManager.Strings.VariablesTitle;
            lblName.Text = LanguageManager.Strings.Name;
            lblType.Text = LanguageManager.Strings.Type;
            lblValue.Text = LanguageManager.Strings.Value;
            lblCreator.Text = LanguageManager.Strings.Creator;
            btnCreateVariable.Text = LanguageManager.Strings.CreateVariable;
            ResumeLayout();
        }

        private void LoadCreators()
        {
            var variableCreators = new List<string>();
            foreach (var variable in VariableManager.ListVariables)
            {
                if (!variableCreators.Contains(variable.Creator)) {
                    variableCreators.Add(variable.Creator);
                }
            }

            var filterModel = VariableViewCreatorFilterModel.Deserialize(Settings.Default.VariableViewSelectedFilter);            

            foreach (var creator in variableCreators)
            {
                if (creatorFilter.Controls.OfType<CheckBox>().Where(x => x.Name.Equals(creator)).Count() > 0)
                    continue;

                var creatorCheckBox = new CheckBox
                {
                    Checked = !filterModel.HiddenCreators.Contains(creator),
                    Text = creator,
                    Name = creator,
                    AutoSize = false,
                    Size = new Size(creatorFilter.Width - 30, 20),
                };
                creatorFilter.Controls.Add(creatorCheckBox);
                creatorCheckBox.CheckedChanged += CreatorCheckBox_CheckedChanged;
            }
        }

        private void CreatorCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            var checkBox = sender as CheckBox;
            Parallel.ForEach(variablesPanel.Controls.OfType<VariableItem>().Where(x => x.Variable.Creator == checkBox.Name).ToArray(),
                variableItem =>
                {
                    if (IsDisposed) return;
                    Invoke(new Action(() => variableItem.Visible = checkBox.Checked));
                });

            var filterModel = new VariableViewCreatorFilterModel
            {
                HiddenCreators = (from creator in creatorFilter.Controls.OfType<CheckBox>()
                                    where !creator.Checked
                                    select creator.Name).ToList()
            };
            Settings.Default.VariableViewSelectedFilter = filterModel.Serialize();
            Settings.Default.Save();
        }

        private void VariablesPage_Load(object sender, EventArgs e)
        {
            LoadCreators();
            LoadVariables();
            VariableManager.OnVariableChanged += VariableChanged;
            VariableManager.OnVariableRemoved += VariableRemoved;
        }

        private void VariableRemoved(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(() => VariableRemoved(sender, e));
                return;
            }
            var variableName = sender as string;
            var variableItemView = variablesPanel.Controls.OfType<VariableItem>().Where(x => x.Variable.Name.Equals(variableName)).FirstOrDefault();
            if (variableItemView != null)
            {
                variablesPanel.Controls.Remove(variableItemView);
            }
        }

        private void VariableChanged(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(() => VariableChanged(sender, e));
                return;
            }
            var variable = sender as Variable;
            if (IsDisposed) return;
            var variableItemView = variablesPanel.Controls.OfType<VariableItem>().Where(x => x.Variable.Name.Equals(variable.Name)).FirstOrDefault();
            if (variableItemView == null)
            {
                var newVariableItem = new VariableItem(variable);
                variablesPanel.Controls.Add(newVariableItem);
                LoadCreators();
            } else
            {
                variableItemView.Variable = variable;
                variableItemView.Update();
            }                        
        }

        private void LoadVariables()
        {
            variablesPanel.Controls.Clear();
            foreach (var variable in VariableManager.ListVariables)
            {
                if (IsDisposed) return;
                var variableItem = new VariableItem(variable)
                {
                    Visible = creatorFilter.Controls.OfType<CheckBox>().Where(x => variable.Creator == x.Name).FirstOrDefault().Checked
                };
                variablesPanel.Controls.Add(variableItem);
            }
        }

        private void BtnCreateVariable_Click(object sender, EventArgs e)
        {
            using (var variableDialog = new VariableDialog())
            {
                if (variableDialog.ShowDialog() == DialogResult.OK)
                {
                    VariableManager.InsertVariable(variableDialog.Variable);
                }
            }
        }
    }
}
