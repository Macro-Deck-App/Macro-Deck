﻿using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Variables.Plugin.ViewModels;
using System;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.Variables.Plugin.Views
{
    public partial class SaveVariableToFileActionConfigView : ActionConfigControl
    {
        private readonly SaveVariableToFileActionConfigViewModel _viewModel;


        public SaveVariableToFileActionConfigView(PluginAction pluginAction)
        {
            InitializeComponent();
            this.lblVariable.Text = LanguageManager.Strings.Variable;
            this.lblPath.Text = LanguageManager.Strings.Path;

            this._viewModel = new SaveVariableToFileActionConfigViewModel(pluginAction);
        }

        private void SaveVariableToFileActionConfigView_Load(object sender, EventArgs e)
        {
            foreach (var variable in VariableManager.Variables)
            {
                this.variable.Items.Add(variable.Name);
            }

            this.variable.Text = this._viewModel.Variable;
            this.path.Text = this._viewModel.FilePath;
        }

        public override bool OnActionSave()
        {
            this._viewModel.Variable = this.variable.Text;
            this._viewModel.FilePath = this.path.Text;
            return this._viewModel.SaveConfig();
        }

        private void BtnChoosePath_Click(object sender, EventArgs e)
        {
            using (var saveFileDialog = new SaveFileDialog()
            {
                AddExtension = true,
                DefaultExt = ".txt",
                Filter = "Text file (*.txt)|*.txt|Any (*.*)|*.*",
            })
            {
                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    this.path.Text = saveFileDialog.FileName;
                }
            }
        }
    }
}
