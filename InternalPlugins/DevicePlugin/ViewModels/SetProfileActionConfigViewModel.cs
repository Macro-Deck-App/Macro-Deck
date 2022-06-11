using SuchByte.MacroDeck.InternalPlugins.DevicePlugin.Models;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Models;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.ViewModels;
using System;
using System.Collections.Generic;
using System.Text;

namespace SuchByte.MacroDeck.InternalPlugins.DevicePlugin.ViewModels
{
    public class SetProfileActionConfigViewModel : ISerializableConfigViewModel
    {
        private readonly PluginAction _action;

        public SetProfileActionConfigModel Configuration { get; set; }

        ISerializableConfiguration ISerializableConfigViewModel.SerializableConfiguration => Configuration;

        public string ClientId
        {
            get => this.Configuration.ClientId;
            set => this.Configuration.ClientId = value;
        }

        public string ProfileId
        {
            get => this.Configuration.ProfileId;
            set => this.Configuration.ProfileId = value;
        }

        public SetProfileActionConfigViewModel(PluginAction action)
        {
            this.Configuration = SetProfileActionConfigModel.Deserialize(action.Configuration);
            this._action = action;
        }

        public bool SaveConfig()
        {
            try
            {
                SetConfig();
                MacroDeckLogger.Info($"{GetType().Name}: config saved");
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Error( $"{GetType().Name}: Error while saving config: { ex.Message + Environment.NewLine + ex.StackTrace }");
                return false;
            }
            return true;
        }

        public void SetConfig()
        {
            _action.ConfigurationSummary = $"{ClientId} -> {ProfileId}";
            _action.Configuration = Configuration.Serialize();
        }

    }
}
