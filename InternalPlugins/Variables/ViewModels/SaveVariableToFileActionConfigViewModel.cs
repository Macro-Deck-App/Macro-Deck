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
    public class SaveVariableToFileActionConfigViewModel : ISerializableConfigViewModel
    {
        private readonly PluginAction _pluginAction;

        public SaveVariableToFileActionConfigModel Configuration { get; set; }

        ISerializableConfiguration ISerializableConfigViewModel.SerializableConfiguration => Configuration;

        public string FilePath
        {
            get => Configuration.FilePath;
            set => Configuration.FilePath = value;
        }

        public string Variable
        {
            get => Configuration.Variable;
            set => Configuration.Variable = value;
        }

        public SaveVariableToFileActionConfigViewModel(PluginAction pluginAction)
        {
            this._pluginAction = pluginAction;
            this.Configuration = SaveVariableToFileActionConfigModel.Deserialize(pluginAction.Configuration);
        }

        public bool SaveConfig()
        {
            if (string.IsNullOrWhiteSpace(this.FilePath) || string.IsNullOrWhiteSpace(this.Variable))
            {
                return false;
            }
            try
            {
                SetConfig();
                MacroDeckLogger.Info(typeof(SaveVariableToFileActionConfigViewModel), "config saved");
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Error(typeof(SaveVariableToFileActionConfigViewModel), $"Error while saving config: { ex.Message + Environment.NewLine + ex.StackTrace }");
            }
            return true;
        }

        public void SetConfig()
        {
            this._pluginAction.ConfigurationSummary = $"{ Configuration.FilePath }";
            this._pluginAction.Configuration = Configuration.Serialize();
        }
    }
}
