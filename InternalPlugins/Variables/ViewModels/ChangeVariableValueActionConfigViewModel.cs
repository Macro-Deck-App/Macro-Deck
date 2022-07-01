using SuchByte.MacroDeck.InternalPlugins.Variables.Enums;
using SuchByte.MacroDeck.InternalPlugins.Variables.Models;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Models;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Variables.Plugin.Models;
using SuchByte.MacroDeck.ViewModels;
using System;
using System.Collections.Generic;
using System.Text;

namespace SuchByte.MacroDeck.Variables.Plugin.ViewModels
{
    public class ChangeVariableValueActionConfigViewModel : ISerializableConfigViewModel
    {
        private readonly PluginAction _pluginAction;

        public ChangeVariableValueActionConfigModel Configuration { get; set; }

        ISerializableConfiguration ISerializableConfigViewModel.SerializableConfiguration => Configuration;

        public ChangeVariableMethod Method
        {
            get => Configuration.Method;
            set => Configuration.Method = value;
        }

        public string Variable
        {
            get => Configuration.Variable;
            set => Configuration.Variable = value;
        }

        public string Value
        {
            get => Configuration.Value;
            set => Configuration.Value = value;
        }

        public ChangeVariableValueActionConfigViewModel(PluginAction pluginAction)
        {
            this._pluginAction = pluginAction;
            this.Configuration = ChangeVariableValueActionConfigModel.Deserialize(pluginAction.Configuration);
        }

        public bool SaveConfig()
        {
            if (string.IsNullOrWhiteSpace(this.Variable))
            {
                return false;
            }
            try
            {
                SetConfig();
                MacroDeckLogger.Info(typeof(ChangeVariableValueActionConfigViewModel), "config saved");
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Error(typeof(ChangeVariableValueActionConfigViewModel), $"Error while saving config: { ex.Message + Environment.NewLine + ex.StackTrace }");
            }
            return true;
        }

        public void SetConfig()
        {
            this._pluginAction.ConfigurationSummary = this.Variable + " -> " + GetMethodName(this.Method) + (this.Method == ChangeVariableMethod.set ? " -> " + this.Value : "");
            this._pluginAction.Configuration = Configuration.Serialize();
        }

        private string GetMethodName(ChangeVariableMethod method)
        {
            switch (method)
            {
                case ChangeVariableMethod.countUp:
                    return LanguageManager.Strings.CountUp;
                case ChangeVariableMethod.countDown:
                    return LanguageManager.Strings.CountDown;
                case ChangeVariableMethod.set:
                    return LanguageManager.Strings.Set;
                case ChangeVariableMethod.toggle:
                    return LanguageManager.Strings.Toggle;
            }
            return "";
        }
    }
}
