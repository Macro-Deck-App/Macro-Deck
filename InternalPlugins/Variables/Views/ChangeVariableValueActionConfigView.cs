using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.InternalPlugins.Variables.Enums;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Variables.Plugin.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.Variables.Plugin.GUI
{
    public partial class ChangeVariableValueActionConfigView : ActionConfigControl
    {
        private readonly ChangeVariableValueActionConfigViewModel _viewModel;

        public ChangeVariableValueActionConfigView(PluginAction pluginAction)
        {
            this._viewModel = new ChangeVariableValueActionConfigViewModel(pluginAction);
            InitializeComponent();

            this.radioCountUp.Text = LanguageManager.Strings.CountUp;
            this.radioCountDown.Text = LanguageManager.Strings.CountDown;
            this.radioSet.Text = LanguageManager.Strings.Set;
            this.radioToggle.Text = LanguageManager.Strings.Toggle;
            this.lblVariable.Text = LanguageManager.Strings.Variable;
            this.lblOnlyUserCreatedVariablesVisible.Text = LanguageManager.Strings.OnlyUserCreatedVariablesVisible;

            this.radioCountUp.Visible = false;
            this.radioCountDown.Visible = false;
            this.radioSet.Visible = false;
            this.radioToggle.Visible = false;
            this.value.Visible = false;
            this.btnTemplateEditor.Visible = false;

            this.variables.SelectedIndexChanged += Variables_SelectedIndexChanged;
        }


        private void Variables_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                Variable variable = VariableManager.ListVariables.Where(v => v.Name == this.variables.Text).FirstOrDefault();
                if (variable != null)
                {
                    switch (variable.Type)
                    {
                        case nameof(VariableType.String):
                            this.radioCountUp.Visible = false;
                            this.radioCountDown.Visible = false;
                            this.radioSet.Visible = true;
                            this.radioToggle.Visible = false;
                            this.radioSet.Checked = true;
                            this.btnTemplateEditor.Visible = true;
                            break;
                        case nameof(VariableType.Bool):
                            this.radioCountUp.Visible = false;
                            this.radioCountDown.Visible = false;
                            this.radioSet.Visible = true;
                            this.radioToggle.Visible = true;
                            this.radioSet.Checked = true;
                            this.btnTemplateEditor.Visible = true;
                            break;
                        case nameof(VariableType.Integer):
                        case nameof(VariableType.Float):
                            this.radioCountUp.Visible = true;
                            this.radioCountDown.Visible = true;
                            this.radioSet.Visible = true;
                            this.btnTemplateEditor.Visible = true;
                            this.radioToggle.Visible = false;
                            break;
                    }

                    if (this.value != null)
                    {
                        this.value.SetAutoCompleteMode(AutoCompleteMode.Suggest);
                        this.value.SetAutoCompleteSource(AutoCompleteSource.CustomSource);
                        AutoCompleteStringCollection suggestions = new AutoCompleteStringCollection();
                        suggestions.AddRange(variable.Suggestions);
                        this.value.SetAutoCompleteCustomSource(suggestions);
                    }
                   

                }
            }
            catch { }
        }

        private void MethodChanged(object sender, EventArgs e)
        {
            this.value.Visible = this.radioSet.Checked;
        }

        public override bool OnActionSave()
        {
            this._viewModel.Variable = this.variables.Text;

            if (this.radioCountUp.Checked)
            {
                this._viewModel.Method = InternalPlugins.Variables.Enums.ChangeVariableMethod.countUp;
            }
            else if (this.radioCountDown.Checked)
            {
                this._viewModel.Method = InternalPlugins.Variables.Enums.ChangeVariableMethod.countDown;
            }
            else if (this.radioSet.Checked)
            {
                this._viewModel.Method = InternalPlugins.Variables.Enums.ChangeVariableMethod.set;
            }
            else if (this.radioToggle.Checked)
            {
                this._viewModel.Method = InternalPlugins.Variables.Enums.ChangeVariableMethod.toggle;
            }

            this._viewModel.Value = this.radioSet.Checked ? this.value.Text : String.Empty;

            return this._viewModel.SaveConfig();
        }

        private void LoadVariables()
        {
            this.variables.Items.Clear();
            foreach (Variable variable in VariableManager.ListVariables.Where(v => v.Creator.Equals("User")))
            {
                this.variables.Items.Add(variable.Name);
            }
        }

        

        private void ChangeVariableValueConfigurator_Load(object sender, EventArgs e)
        {
            this.LoadVariables();

            switch (this._viewModel.Method)
            {
                case ChangeVariableMethod.countUp:
                    this.radioCountUp.Checked = true;
                    break;
                case ChangeVariableMethod.countDown:
                    this.radioCountDown.Checked = true;
                    break;
                case ChangeVariableMethod.set:
                    this.radioSet.Checked = true;
                    break;
                case ChangeVariableMethod.toggle:
                    this.radioToggle.Checked = true;
                    break;
            }

            this.variables.Text = this._viewModel.Variable;
            this.value.Text = this._viewModel.Value;
            this.value.Visible = this.radioSet.Checked;

        }

        private void BtnTemplateEditor_Click(object sender, EventArgs e)
        {
            using (var templateEditor = new TemplateEditor(this.value.Text))
            {
                if (templateEditor.ShowDialog() == DialogResult.OK)
                {
                    this.value.Text = templateEditor.Template;
                }
            }
        }
    }
}
