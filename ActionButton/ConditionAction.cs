using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.GUI;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Variables;

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
        public List<PluginAction> Actions { get => _actions;
            set { _actions = value; UpdateConfiguration(); } }
        public List<PluginAction> ActionsElse { get => _actionsElse;
            set { _actionsElse = value; UpdateConfiguration(); } }
        public string ConditionValue1Source { get => _conditionValue1Source;
            set { _conditionValue1Source = value; UpdateConfiguration(); } }
        public ConditionType ConditionType { get => _conditionType;
            set { _conditionType = value; UpdateConfiguration(); } }
        public ConditionMethod ConditionMethod { get => _conditionMethod;
            set { _conditionMethod = value; UpdateConfiguration(); } }
        public string ConditionValue2 { get => _conditionValue2;
            set { _conditionValue2 = value; UpdateConfiguration(); } }

        public ConditionAction()
        {
            if (!string.IsNullOrEmpty(Configuration))
            {
                var jsonSerializerSettings = new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                    NullValueHandling = NullValueHandling.Ignore,
                    Error = (sender, args) => { args.ErrorContext.Handled = true; }
                };

                _actions = JsonConvert.DeserializeObject<List<PluginAction>>(JObject.Parse(Configuration)["actions"].ToString(), jsonSerializerSettings);
                _actionsElse = JsonConvert.DeserializeObject<List<PluginAction>>(JObject.Parse(Configuration)["actionsElse"].ToString(), jsonSerializerSettings);
                _conditionValue1Source = JsonConvert.DeserializeObject<string>(JObject.Parse(Configuration)["source"].ToString(), jsonSerializerSettings);
                _conditionType = JsonConvert.DeserializeObject<ConditionType>(JObject.Parse(Configuration)["conditionType"].ToString(), jsonSerializerSettings);
                _conditionMethod = JsonConvert.DeserializeObject<ConditionMethod>(JObject.Parse(Configuration)["contitionMethod"].ToString(), jsonSerializerSettings);
                _conditionValue2 = JsonConvert.DeserializeObject<string>(JObject.Parse(Configuration)["conditionValue2"].ToString(), jsonSerializerSettings);

            }
            if (_actions == null)
            {
                _actions = new List<PluginAction>();
            }
            if (_actionsElse == null)
            {
                _actionsElse = new List<PluginAction>();
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
            

            var jsonSerializerSettings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                NullValueHandling = NullValueHandling.Ignore,
                Error = (sender, args) => { args.ErrorContext.Handled = true; }
            };

            configurationString["actions"] = JsonConvert.SerializeObject(_actions, jsonSerializerSettings);
            configurationString["actionsElse"] = JsonConvert.SerializeObject(_actions, jsonSerializerSettings);
            configurationString["source"] = JsonConvert.SerializeObject(_conditionValue1Source, jsonSerializerSettings);
            configurationString["conditionType"] = JsonConvert.SerializeObject(_conditionType, jsonSerializerSettings);
            configurationString["contitionMethod"] = JsonConvert.SerializeObject(_conditionMethod, jsonSerializerSettings);
            configurationString["conditionValue2"] = JsonConvert.SerializeObject(_conditionValue2, jsonSerializerSettings);

            Configuration = configurationString.ToString();
        }

        public override string Description => "";

        public ActionConfigControl GetActionConfigurator(ActionConfigurator actionConfigurator) { return null; }

        public override void Trigger(string clientId, ActionButton actionButton) {
            var result = false;
            var conditionValue2 = ConditionValue2;
            var variable = VariableManager.ListVariables.ToList().Find(v => v.Name.Equals(_conditionValue1Source));

            foreach (var v in VariableManager.ListVariables)
            {
                if (conditionValue2.ToLower().Contains("{" + v.Name.ToLower() + "}"))
                {
                    conditionValue2 = conditionValue2.Replace("{" + v.Name + "}", v.Value, StringComparison.OrdinalIgnoreCase);
                }
            }

            switch (_conditionType)
            {
                case ConditionType.Variable:
                    switch (_conditionMethod)
                    {
                        case ConditionMethod.Equals:
                            result = !(variable == null || !variable.Value.ToLower().Equals(conditionValue2.ToLower()));
                            break;
                        case ConditionMethod.Not:
                            result = (variable == null || !variable.Value.ToLower().Equals(conditionValue2.ToLower()));
                            break;
                        case ConditionMethod.Bigger:
                            if (variable != null && !((VariableType)Enum.Parse(typeof(VariableType), variable.Type) != VariableType.Integer && (VariableType)Enum.Parse(typeof(VariableType), variable.Type) != VariableType.Float))
                            {
                                result = (float.Parse(variable.Value) > float.Parse(conditionValue2));
                            }
                            break;
                        case ConditionMethod.Smaller:
                            if (variable != null && !((VariableType)Enum.Parse(typeof(VariableType), variable.Type) != VariableType.Integer && (VariableType)Enum.Parse(typeof(VariableType), variable.Type) != VariableType.Float))
                            {
                                result = (float.Parse(variable.Value) < float.Parse(conditionValue2));
                            }
                            break;
                    }
                    break;
                case ConditionType.Button_State:
                    var value2 = false;
                    switch (_conditionMethod)
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
                    if (bool.TryParse(VariableManager.RenderTemplate(_conditionValue1Source), out var boolResult))
                    {
                        result = boolResult;
                    }
                    break;
            }

            if (result) {
                foreach (var action in _actions)
                {
                    action.Trigger(clientId, actionButton);
                }
            } else
            {
                foreach (var action in _actionsElse)
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
