using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.Events;
using SuchByte.MacroDeck.Folders;
using SuchByte.MacroDeck.GUI;
using SuchByte.MacroDeck.GUI.CustomControls;
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
        public override string Name => "Variables";
        public override string Author => "Macro Deck";
        public override string Description => "Increment, decrement, set variable values";

        private VariableChangedEvent variableChangedEvent = new VariableChangedEvent();

        Timer timeDateTimer;

        public override void Enable()
        {
            this.Actions = new List<PluginAction>
            {
                new ChangeVariableValueAction(),
            };
            MacroDeck.EventManager.RegisterEvent(this.variableChangedEvent);
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
                VariableManager.SetValue("Time", DateTime.Now.ToString("t"), VariableType.String, "Macro Deck", false);
                VariableManager.SetValue("Date", DateTime.Now.ToString("d"), VariableType.String, "Macro Deck", false);
            });
        }
        private void VariableChanged(object sender, EventArgs e)
        {
            this.variableChangedEvent.Trigger();
        }
    }

    public class VariableChangedEvent : IMacroDeckEvent
    {
        public string Name => "Variable changed";

        public EventHandler<MacroDeckEventArgs> OnEvent { get; set; }

        public void Trigger()
        {
            if (this.OnEvent != null)
            {
                foreach (MacroDeckProfile macroDeckProfile in MacroDeck.ProfileManager.Profiles)
                {
                    foreach (MacroDeckFolder folder in macroDeckProfile.Folders)
                    {
                        foreach (ActionButton.ActionButton actionButton in folder.ActionButtons.FindAll(actionButton => actionButton.EventActions != null && actionButton.EventActions.ContainsKey(this.Name)))
                        {
                            MacroDeckEventArgs macroDeckEventArgs = new MacroDeckEventArgs
                            {
                                ActionButton = actionButton
                            };
                            this.OnEvent(this, macroDeckEventArgs);
                        }
                    }
                }
            }
        }
    }

    public class ChangeVariableValueAction : PluginAction
    {
        public override string Name => "Change variable value";

        public override string Description => "Increment, decrement, set a variable value";

        public override string DisplayName { get; set; } = "Change variable value";
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
                    case "increment":
                        VariableManager.SetValue(variable.Name, float.Parse(variable.Value) + 1, (VariableType)Enum.Parse(typeof(VariableType), variable.Type), variable.Creator);
                        break;
                    case "decrement":
                        VariableManager.SetValue(variable.Name, float.Parse(variable.Value) - 1, (VariableType)Enum.Parse(typeof(VariableType), variable.Type), variable.Creator);
                        break;
                    case "set":
                        VariableManager.SetValue(variable.Name, jsonObject["value"].ToString(), (VariableType)Enum.Parse(typeof(VariableType), variable.Type), variable.Creator);
                        break;
                    case "toggle":
                        VariableManager.SetValue(variable.Name, !Boolean.Parse(variable.Value.Replace("on", "true")), (VariableType)Enum.Parse(typeof(VariableType), variable.Type), variable.Creator);
                        break;
                }
            } catch { }
        }
    }
}
