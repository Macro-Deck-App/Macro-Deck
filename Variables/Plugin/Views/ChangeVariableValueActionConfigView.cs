using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Plugins;
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
        PluginAction _macroDeckAction;

        public ChangeVariableValueActionConfigView(PluginAction macroDeckAction)
        {
            this._macroDeckAction = macroDeckAction;
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
                Variable variable = VariableManager.Variables.Find(v => v.Name.Equals(this.variables.Text));
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
            if (String.IsNullOrWhiteSpace(this.variables.Text))
            {
                return false;
            }
            JObject jObject = null;
            try
            {
                jObject = JObject.Parse(this._macroDeckAction.Configuration);
            }
            catch { }
            if (jObject == null)
            {
                jObject = new JObject();
            }

            if (this.radioCountUp.Checked)
            {
                jObject["method"] = "countUp";
            }
            else if (this.radioCountDown.Checked)
            {
                jObject["method"] = "countDown";
            }
            else if (this.radioSet.Checked)
            {
                if (String.IsNullOrWhiteSpace(this.value.Text))
                {
                    return false;
                }
                jObject["method"] = "set";
            }
            else if (this.radioToggle.Checked)
            {
                jObject["method"] = "toggle";
            }
            jObject["variable"] = variables.Text;
            jObject["value"] = this.radioSet.Checked ? this.value.Text : String.Empty;

            this._macroDeckAction.Configuration = jObject.ToString();
            this._macroDeckAction.ConfigurationSummary = jObject["variable"].ToString() + " -> " + GetMethodName(jObject["method"].ToString()) + (this.radioSet.Checked ? " -> " + jObject["value"].ToString() : "");
            return true;
        }

        private void LoadVariables()
        {
            this.variables.Items.Clear();
            foreach (Variable variable in VariableManager.Variables.FindAll(v => v.Creator.Equals("User")))
            {
                this.variables.Items.Add(variable.Name);
            }


            /*if (this.radioCountUp.Checked || this.radioCountDown.Checked)
            {
                foreach (Variable variable in VariableManager.Variables.FindAll(v => v.Creator.Equals("User") && (v.Type.Equals(VariableType.Integer.ToString()) || v.Type.Equals(VariableType.Float.ToString()))))
                {
                    this.variables.Items.Add(variable.Name);
                }
            } else if (this.radioToggle.Checked)
            {
                foreach (Variable variable in VariableManager.Variables.FindAll(v => v.Creator.Equals("User") && (v.Type.Equals(VariableType.Bool.ToString()))))
                {
                    this.variables.Items.Add(variable.Name);
                }
            }
            else if (this.radioSet.Checked)
            {
                
            }*/
        }

        private string GetMethodName(string method)
        {
            switch (method)
            {
                case "countUp":
                    return LanguageManager.Strings.CountUp;
                case "countDown":
                    return LanguageManager.Strings.CountDown;
                case "set":
                    return LanguageManager.Strings.Set;
                case "toggle":
                    return LanguageManager.Strings.Toggle;
            }
            return "";
        }

        private void ChangeVariableValueConfigurator_Load(object sender, EventArgs e)
        {
            this.LoadVariables();
            JObject jObject = null;
            try
            {
                jObject = JObject.Parse(this._macroDeckAction.Configuration);
            }
            catch { }
            if (jObject == null) return;

            switch (jObject["method"].ToString())
            {
                case "countUp":
                    this.radioCountUp.Checked = true;
                    break;
                case "countDown":
                    this.radioCountDown.Checked = true;
                    break;
                case "set":
                    this.radioSet.Checked = true;
                    break;
                case "toggle":
                    this.radioToggle.Checked = true;
                    break;
            }



            this.variables.Text = jObject["variable"].ToString();
            this.value.Text = jObject["value"].ToString();
            this.value.Visible = this.radioSet.Checked;

        }

        private void btnTemplateEditor_Click(object sender, EventArgs e)
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
