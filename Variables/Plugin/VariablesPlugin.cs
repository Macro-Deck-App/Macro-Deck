using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.Events;
using SuchByte.MacroDeck.Folders;
using SuchByte.MacroDeck.GUI;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Profiles;
using SuchByte.MacroDeck.Variables.Plugin.GUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace SuchByte.MacroDeck.Variables.Plugin
{
    public class VariablesPlugin : MacroDeckPlugin
    {
        public override string Name => LanguageManager.Strings.PluginMacroDeckVariables;
        public override string Author => "Macro Deck";

        private VariableChangedEvent variableChangedEvent = new VariableChangedEvent();

        Timer timeDateTimer;

        public override void Enable()
        {
            this.Actions = new List<PluginAction>
            {
                new ChangeVariableValueAction(),
            };
            EventManager.RegisterEvent(this.variableChangedEvent);
            VariableManager.OnVariableChanged += VariableChanged;

            this.timeDateTimer = new Timer(1000)
            {
                Enabled = true
            };
            this.timeDateTimer.Elapsed += this.OnTimerTick;
            this.timeDateTimer.Start();
        }
        private void OnTimerTick(object sender, EventArgs e)
        {
            Task.Run(() =>
            {
                VariableManager.SetValue("time", DateTime.Now.ToString("t"), VariableType.String, "Macro Deck", false);
                VariableManager.SetValue("date", DateTime.Now.ToString("d"), VariableType.String, "Macro Deck", false);
            });
        }
        private void VariableChanged(object sender, EventArgs e)
        {
            this.variableChangedEvent.Trigger(sender);
        }
    }

    public class VariableChangedEvent : IMacroDeckEvent
    {
        public string Name => "Variable changed";

        public EventHandler<MacroDeckEventArgs> OnEvent { get; set; }
        public List<string> ParameterSuggestions {
            get 
            {
                List<string> variables = new List<string>();
                foreach (Variable variable in VariableManager.Variables)
                {
                    variables.Add(variable.Name);
                }
                return variables;
            } set { }
        }

        public void Trigger(object sender)
        {
            if (this.OnEvent != null)
            {
                try
                {
                    foreach (MacroDeckProfile macroDeckProfile in ProfileManager.Profiles)
                    {
                        foreach (MacroDeckFolder folder in macroDeckProfile.Folders)
                        {
                            if (folder.ActionButtons == null) continue;
                            foreach (ActionButton.ActionButton actionButton in folder.ActionButtons.FindAll(actionButton => actionButton.EventListeners != null && actionButton.EventListeners.Find(x => x.EventToListen != null && x.EventToListen.Equals(this.Name)) != null))
                            {
                                MacroDeckEventArgs macroDeckEventArgs = new MacroDeckEventArgs
                                {
                                    ActionButton = actionButton,
                                    Parameter = ((Variable)sender).Name,
                                };
                                this.OnEvent(this, macroDeckEventArgs);
                            }
                        }
                    }
                }
                catch { }
            }
        }
    }

    public class ChangeVariableValueAction : PluginAction
    {
        public override string Name => LanguageManager.Strings.ActionChangeVariableValue;

        public override string Description => LanguageManager.Strings.ActionChangeVariableValue;

        public override bool CanConfigure => true;

        public override ActionConfigControl GetActionConfigControl(ActionConfigurator actionConfigurator)
        {
            return new ChangeVariableValueConfigurator(this);
        }

        public override void Trigger(string clientId, ActionButton.ActionButton actionButton)
        {
            if (this.Configuration.Length == 0) return;
            try
            {
                JObject jsonObject = JObject.Parse(this.Configuration);
                Variable variable = VariableManager.Variables.Find(v => v.Name.Equals(jsonObject["variable"].ToString()));
                if (variable == null) return;
                switch (jsonObject["method"].ToString())
                {
                    case "countUp": case "increase":
                        VariableManager.SetValue(variable.Name, float.Parse(variable.Value) + 1, (VariableType)Enum.Parse(typeof(VariableType), variable.Type), variable.Creator);
                        break;
                    case "countDown": case "decrease":
                        VariableManager.SetValue(variable.Name, float.Parse(variable.Value) - 1, (VariableType)Enum.Parse(typeof(VariableType), variable.Type), variable.Creator);
                        break;
                    case "set":
                        if (jsonObject["value"] != null && !String.IsNullOrWhiteSpace(jsonObject["value"].ToString()))
                        {
                            var value = VariableManager.RenderTemplate(jsonObject["value"].ToString());
                            VariableManager.SetValue(variable.Name, value, (VariableType)Enum.Parse(typeof(VariableType), variable.Type), variable.Creator);
                        }
                        break;
                    case "toggle":
                        VariableManager.SetValue(variable.Name, !Boolean.Parse(variable.Value.Replace("on", "true")), (VariableType)Enum.Parse(typeof(VariableType), variable.Type), variable.Creator);
                        break;
                }
            } catch { }
        }
    }
}
