using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.GUI.CustomControls;
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
    public partial class ChangeVariableValueConfigurator : ActionConfigControl
    {
        PluginAction _macroDeckAction;

        public ChangeVariableValueConfigurator(PluginAction macroDeckAction)
        {
            this._macroDeckAction = macroDeckAction;
            InitializeComponent();

            this.radioCountUp.Text = LanguageManager.Strings.CountUp;
            this.radioCountDown.Text = LanguageManager.Strings.CountDown;
            this.radioSet.Text = LanguageManager.Strings.Set;
            this.radioToggle.Text = LanguageManager.Strings.Toggle;
            this.lblVariable.Text = LanguageManager.Strings.Variable;

            this.variables.SelectedIndexChanged += Variables_SelectedIndexChanged;
        }


        private void Variables_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                Variable variable = VariableManager.Variables.Find(v => v.Name.Equals(this.variables.Text));
                if (variable != null)
                {
                    this.value.SetAutoCompleteMode(AutoCompleteMode.Suggest);
                    this.value.SetAutoCompleteSource(AutoCompleteSource.CustomSource);
                    AutoCompleteStringCollection suggestions = new AutoCompleteStringCollection();
                    suggestions.AddRange(variable.Suggestions);
                    this.value.SetAutoCompleteCustomSource(suggestions);
                }
            }
            catch { }
        }

        private void MethodChanged(object sender, EventArgs e)
        {
            this.value.Visible = this.radioSet.Checked;
            this.LoadVariables();
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
            if (this.radioCountUp.Checked || this.radioCountDown.Checked)
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
                foreach (Variable variable in VariableManager.Variables.FindAll(v => v.Creator.Equals("User")))
                {
                    this.variables.Items.Add(variable.Name);
                }
            }
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
    }
}
