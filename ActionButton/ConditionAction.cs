using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.GUI;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Variables;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace SuchByte.MacroDeck.ActionButton
{
    public class ConditionAction : PluginAction
    {
        public override string Name => "Condition";

        private List<PluginAction> _actions;
        private List<PluginAction> _actionsElse;
        private string _conditionValue1Source = "";
        private ConditionType _conditionType = ConditionType.Variable;
        private ConditionMethod _conditionMethod = ConditionMethod.Equals;
        private string _conditionValue2 = "";
        public List<PluginAction> Actions { get { return _actions; } set { _actions = value; UpdateConfiguration(); } }
        public List<PluginAction> ActionsElse { get { return _actionsElse; } set { _actionsElse = value; UpdateConfiguration(); } }
        public string ConditionValue1Source { get { return _conditionValue1Source; } set { _conditionValue1Source = value; UpdateConfiguration(); } }
        public ConditionType ConditionType { get { return _conditionType; } set { _conditionType = value; UpdateConfiguration(); } }
        public ConditionMethod ConditionMethod { get { return _conditionMethod; } set { _conditionMethod = value; UpdateConfiguration(); } }
        public string ConditionValue2 { get { return _conditionValue2; } set { _conditionValue2 = value; UpdateConfiguration(); } }

        public ConditionAction()
        {
            if (!String.IsNullOrEmpty(Configuration))
            {
                JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                    NullValueHandling = NullValueHandling.Ignore,
                    Error = (sender, args) => { args.ErrorContext.Handled = true; }
                };

                this._actions = JsonConvert.DeserializeObject<List<PluginAction>>(JObject.Parse(Configuration)["actions"].ToString(), jsonSerializerSettings);
                this._actionsElse = JsonConvert.DeserializeObject<List<PluginAction>>(JObject.Parse(Configuration)["actionsElse"].ToString(), jsonSerializerSettings);
                this._conditionValue1Source = JsonConvert.DeserializeObject<string>(JObject.Parse(Configuration)["source"].ToString(), jsonSerializerSettings);
                this._conditionType = JsonConvert.DeserializeObject<ConditionType>(JObject.Parse(Configuration)["conditionType"].ToString(), jsonSerializerSettings);
                this._conditionMethod = JsonConvert.DeserializeObject<ConditionMethod>(JObject.Parse(Configuration)["contitionMethod"].ToString(), jsonSerializerSettings);
                this._conditionValue2 = JsonConvert.DeserializeObject<string>(JObject.Parse(Configuration)["conditionValue2"].ToString(), jsonSerializerSettings);

            }
            if (this._actions == null)
            {
                this._actions = new List<PluginAction>();
            }
            if (this._actionsElse == null)
            {
                this._actionsElse = new List<PluginAction>();
            }
        }

        private void UpdateConfiguration()
        {
            JObject configurationString;
            try
            {
                configurationString = JObject.Parse(Configuration);
            } catch
            {
                configurationString = new JObject();
            }
            

            JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                NullValueHandling = NullValueHandling.Ignore,
                Error = (sender, args) => { args.ErrorContext.Handled = true; }
            };

            configurationString["actions"] = JsonConvert.SerializeObject(this._actions, jsonSerializerSettings);
            configurationString["actionsElse"] = JsonConvert.SerializeObject(this._actions, jsonSerializerSettings);
            configurationString["source"] = JsonConvert.SerializeObject(this._conditionValue1Source, jsonSerializerSettings);
            configurationString["conditionType"] = JsonConvert.SerializeObject(this._conditionType, jsonSerializerSettings);
            configurationString["contitionMethod"] = JsonConvert.SerializeObject(this._conditionMethod, jsonSerializerSettings);
            configurationString["conditionValue2"] = JsonConvert.SerializeObject(this._conditionValue2, jsonSerializerSettings);

            Configuration = configurationString.ToString();
        }

        public override string Description => "";

        public ActionConfigControl GetActionConfigurator(ActionConfigurator actionConfigurator) { return null; }

        public override void Trigger(string clientId, ActionButton actionButton) {
            bool result = false;
            string conditionValue2 = this.ConditionValue2.ToString();
            Variable variable = VariableManager.ListVariables.ToList().Find(v => v.Name.Equals(this._conditionValue1Source));

            foreach (Variable v in VariableManager.ListVariables)
            {
                if (conditionValue2.ToLower().Contains("{" + v.Name.ToLower() + "}"))
                {
                    conditionValue2 = conditionValue2.Replace("{" + v.Name + "}", v.Value.ToString(), StringComparison.OrdinalIgnoreCase);
                }
            }

            switch (this._conditionType)
            {
                case ConditionType.Variable:
                    switch (this._conditionMethod)
                    {
                        case ConditionMethod.Equals:
                            result = !(variable == null || !variable.Value.ToLower().Equals(conditionValue2.ToLower()));
                            break;
                        case ConditionMethod.Not:
                            result = (variable == null || !variable.Value.ToLower().Equals(conditionValue2.ToLower()));
                            break;
                        case ConditionMethod.Bigger:
                            if (variable != null && !((Variables.VariableType)Enum.Parse(typeof(Variables.VariableType), variable.Type) != Variables.VariableType.Integer && (Variables.VariableType)Enum.Parse(typeof(Variables.VariableType), variable.Type) != Variables.VariableType.Float))
                            {
                                result = (float.Parse(variable.Value) > float.Parse(conditionValue2));
                            }
                            break;
                        case ConditionMethod.Smaller:
                            if (variable != null && !((Variables.VariableType)Enum.Parse(typeof(Variables.VariableType), variable.Type) != Variables.VariableType.Integer && (Variables.VariableType)Enum.Parse(typeof(Variables.VariableType), variable.Type) != Variables.VariableType.Float))
                            {
                                result = (float.Parse(variable.Value) < float.Parse(conditionValue2));
                            }
                            break;
                    }
                    break;
                case ConditionType.Button_State:
                    bool value2 = false;
                    switch (this._conditionMethod)
                    {
                        case ConditionMethod.Equals:
                            if (conditionValue2.ToLower().Equals("on") || conditionValue2.ToLower().Equals("true"))
                            {
                                value2 = true;
                            }
                            result = value2.Equals(actionButton.State);
                            break;
                        case ConditionMethod.Not:
                            if (conditionValue2.ToLower().Equals("on") || conditionValue2.ToLower().Equals("true"))
                            {
                                value2 = true;
                            }
                            result = !value2.Equals(actionButton.State);
                            break;
                    }
                    break;
                case ConditionType.Template:
                    if (bool.TryParse(VariableManager.RenderTemplate(_conditionValue1Source), out bool boolResult))
                    {
                        result = boolResult;
                    }
                    break;
            }

            if (result == true) {
                foreach (PluginAction action in this._actions)
                {
                    action.Trigger(clientId, actionButton);
                }
            } else
            {
                foreach (PluginAction action in this._actionsElse)
                {
                    action.Trigger(clientId, actionButton);
                }
            }
        }
    }

    public enum ConditionType
    {
        Variable,
        Button_State,
        Template,
    }

    public enum ConditionMethod
    {
        Equals,
        Bigger,
        Smaller,
        Not,
    }



}
