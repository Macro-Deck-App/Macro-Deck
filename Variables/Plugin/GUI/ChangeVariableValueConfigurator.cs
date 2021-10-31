using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Plugins;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
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
        }

        private void MethodChanged(object sender, EventArgs e)
        {
            this.value.Visible = this.radioSet.Checked;
            this.lblValue.Visible = this.value.Visible;
            this.LoadVariables();
            this.UpdateConfig();
        }

        private void LoadVariables()
        {
            this.variables.Items.Clear();
            if (this.radioIncrement.Checked || this.radioDecrement.Checked)
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

        private void UpdateConfig()
        {
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

            if (this.radioIncrement.Checked)
            {
                jObject["method"] = "increment";
            } else if (this.radioDecrement.Checked)
            {
                jObject["method"] = "decrement";
            }
            else if (this.radioSet.Checked)
            {
                jObject["method"] = "set";
            }
            else if (this.radioToggle.Checked)
            {
                jObject["method"] = "toggle";
            }
            jObject["variable"] = variables.Text;
            jObject["value"] = this.radioSet.Checked ? this.value.Text : "";

            this._macroDeckAction.Configuration = jObject.ToString();
            this._macroDeckAction.DisplayName = this._macroDeckAction.Name + " -> " + jObject["method"].ToString() + " -> " + variables.Text + (this.radioSet.Checked ? " -> " + this.value.Text : "");
        }

        private void variables_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.UpdateConfig();
        }

        private void value_TextChanged(object sender, EventArgs e)
        {
            this.UpdateConfig();
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

            this.radioIncrement.CheckedChanged -= this.MethodChanged;
            this.radioDecrement.CheckedChanged -= this.MethodChanged;
            this.radioSet.CheckedChanged -= this.MethodChanged;
            this.radioToggle.CheckedChanged -= this.MethodChanged;
            this.variables.SelectedIndexChanged -= this.variables_SelectedIndexChanged;
            this.value.TextChanged -= this.value_TextChanged;

            switch (jObject["method"].ToString())
            {
                case "increment":
                    this.radioIncrement.Checked = true;
                    break;
                case "decrement":
                    this.radioDecrement.Checked = true;
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
            this.lblValue.Visible = this.value.Visible;




            this.radioIncrement.CheckedChanged += this.MethodChanged;
            this.radioDecrement.CheckedChanged += this.MethodChanged;
            this.radioSet.CheckedChanged += this.MethodChanged;
            this.radioToggle.CheckedChanged += this.MethodChanged;
            this.variables.SelectedIndexChanged += this.variables_SelectedIndexChanged;
            this.value.TextChanged += this.value_TextChanged;

        }
    }
}
