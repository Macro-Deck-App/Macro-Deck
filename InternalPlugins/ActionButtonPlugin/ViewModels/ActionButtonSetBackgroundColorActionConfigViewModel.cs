using SuchByte.MacroDeck.InternalPlugins.ActionButtonPlugin.Enums;
using SuchByte.MacroDeck.InternalPlugins.ActionButtonPlugin.Models;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Models;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.ViewModels;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Text.Json;

namespace SuchByte.MacroDeck.InternalPlugins.ActionButtonPlugin.ViewModels
{
    public class ActionButtonSetBackgroundColorActionConfigViewModel : ISerializableConfigViewModel
    {
        private readonly PluginAction _pluginAction;

        public ActionButtonSetBackgroundColorActionConfigModel Configuration { get; set; }

        ISerializableConfiguration ISerializableConfigViewModel.SerializableConfiguration => Configuration;

        public Color Color
        {
            get => Configuration.Color;
            set => Configuration.Color = value;
        }

        public SetBackgroundColorMethod Method
        {
            get => Configuration.Method;
            set => Configuration.Method = value;
        }


        public ActionButtonSetBackgroundColorActionConfigViewModel(PluginAction pluginAction)
        {
            this._pluginAction = pluginAction;
            this.Configuration = ActionButtonSetBackgroundColorActionConfigModel.Deserialize(pluginAction.Configuration);
        }

        public bool SaveConfig()
        {
            try
            {
                SetConfig();
                MacroDeckLogger.Info(typeof(ActionButtonSetBackgroundColorActionConfigViewModel), "config saved");
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Error(typeof(ActionButtonSetBackgroundColorActionConfigViewModel), $"Error while saving config: { ex.Message + Environment.NewLine + ex.StackTrace }");
            }
            return true;
        }

        public void SetConfig()
        {
            this._pluginAction.ConfigurationSummary = this.Method switch
            {
                SetBackgroundColorMethod.Fixed => $"#{this.Color.R:X2}{this.Color.G:X2}{this.Color.B:X2}",
                SetBackgroundColorMethod.Random => LanguageManager.Strings.Random,
                _ => "",
            };
            this._pluginAction.Configuration = Configuration.Serialize();
        }
    }
}
