using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Variables.Plugin.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.Variables.Plugin.Views
{
    public partial class ReadVariableFromFileActionConfigView : ActionConfigControl
    {
        private readonly ReadVariableFromFileActionConfigViewModel _viewModel;


        public ReadVariableFromFileActionConfigView(PluginAction pluginAction)
        {
            InitializeComponent();
            this.lblVariable.Text = LanguageManager.Strings.Variable;
            this.lblPath.Text = LanguageManager.Strings.Path;

            this._viewModel = new ReadVariableFromFileActionConfigViewModel(pluginAction);
        }

        private void ReadVariableFromFileActionConfigView_Load(object sender, EventArgs e)
        {
            LoadVariables();
            this.variable.Text = this._viewModel.Variable;
            this.path.Text = this._viewModel.FilePath;
        }

        private void LoadVariables()
        {
            this.variable.Items.Clear();
            foreach (var variable in VariableManager.Variables.FindAll(x => x.Creator == "User"))
            {
                this.variable.Items.Add(variable.Name);
            }
        }

        public override bool OnActionSave()
        {
            this._viewModel.Variable = this.variable.Text;
            this._viewModel.FilePath = this.path.Text;
            return this._viewModel.SaveConfig();
        }

        private void BtnChoosePath_Click(object sender, EventArgs e)
        {
            using (var openFileDialog = new OpenFileDialog()
            {
                AddExtension = true,
                DefaultExt = ".txt",
                Filter = "Text file (*.txt)|*.txt|Any (*.*)|*.*",
            })
            {
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    this.path.Text = openFileDialog.FileName;
                }
            }
        }

        private void BtnCreateVariable_Click(object sender, EventArgs e)
        {
            using (var variableDialog = new VariableDialog())
            {
                if (variableDialog.ShowDialog() == DialogResult.OK)
                {
                    VariableManager.Variables.Add(variableDialog.Variable);
                    LoadVariables();
                    this.variable.Text = variableDialog.Variable.Name;
                }
            }
        }

    }
}
