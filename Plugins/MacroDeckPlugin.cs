using Newtonsoft.Json;
using SuchByte.MacroDeck.GUI;
using SuchByte.MacroDeck.GUI.CustomControls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text.Json.Serialization;

namespace SuchByte.MacroDeck.Plugins
{
    public abstract class MacroDeckPlugin
    {
        Assembly executingAssembly = Assembly.GetCallingAssembly();
        FileVersionInfo versionInfo;

        private string _name = "", _version = "";

        public MacroDeckPlugin()
        {
            this.versionInfo = FileVersionInfo.GetVersionInfo(executingAssembly.Location);
            this._name = this.executingAssembly.GetName().Name;
            this._version = this.versionInfo.ProductVersion;
        }

        /// <summary>
        /// Name of the plugin
        /// </summary>
        internal virtual string Name
        {
            get
            {
                return this._name;
            }
            set
            {
                this._name = value;
            }
        }

        /// <summary>
        /// Version of the plugin
        /// </summary>
        internal virtual string Version
        {
            get
            {
                return this._version;
            }
            set
            {
                this._version = value;
            }
        }

        /// <summary>
        /// Author of the plugin
        /// </summary>
        internal virtual string Author { get; set; }

        /// <summary>
        /// This list contains all the actions of the plugin. If your plugin does not contain any actions, you can delete this.
        /// </summary>
        public List<PluginAction> Actions { get; set; } = new List<PluginAction>();
        /// <summary>
        /// Icon of the plugin
        /// </summary>
        internal virtual Image PluginIcon { get; set; } = Properties.Resources.Macro_Deck_2021;
        /// <summary>
        /// true = the plugin can be configured. A button to open the plugin's configurator will appear in the package manager. If your plugin cannot be configured, you can delete this.
        /// </summary>
        public virtual bool CanConfigure { get; } = false;
        /// <summary>
        /// Gets called when the user clicks on the configure button in the package manager. If your plugin cannot be configured, you can delete this.
        /// </summary>
        public virtual void OpenConfigurator() { }
        /// <summary>
        /// Gets called when the plugin is enabled. Initialize your actions list here if your plugin contains any actions.
        /// </summary>
        public abstract void Enable();


        [Obsolete("Will be removed soon")]
        public virtual string Description { get; set; }

        [Obsolete("Will be removed soon")]
        public virtual Image Icon { get; }
    }

    public abstract class PluginAction
    {
        ActionButton.ActionButton actionButton;
        internal void SetActionButton(ActionButton.ActionButton actionButton)
        {
            if (actionButton == null) return;
            if (this.actionButton != null && this.actionButton.Equals(actionButton))
            {
                this.OnActionButtonDelete();
            }
            this.actionButton = actionButton;
            if (actionButton != null)
            {
                this.OnActionButtonLoaded();
            }
        }

        /// <summary>
        /// Gets the ActionButton which contains this action
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public ActionButton.ActionButton ActionButton
        {
            get
            {
                return this.actionButton;
            }
        }

        /// <summary>
        /// Gets called when the action button gets deleted
        /// </summary>
        public virtual void OnActionButtonDelete(){ }
        /// <summary>
        /// Gets called when the action button is loaded
        /// </summary>
        public virtual void OnActionButtonLoaded() { }
        /// <summary>
        /// Name of the action
        /// </summary>
        public abstract string Name { get; }
        /// <summary>
        /// Use ConfigurationSummary instead
        /// </summary>
        [Obsolete("Use ConfigurationSummary instead")]
        public virtual string DisplayName { get; set; }
        /// <summary>
        /// Description what the action does
        /// </summary>
        public abstract string Description { get; }
        /// <summary>
        /// Gets called when the user presses a button with this action configured
        /// </summary>
        /// <param name="clientId"></param>
        /// <param name="actionButton"></param>
        public abstract void Trigger(string clientId, ActionButton.ActionButton actionButton);
        /// <summary>
        /// Configuration of the action
        /// </summary>
        public string Configuration { get; set; }
        /// <summary>
        /// Can be used when this action is configurable to show a short summary of the configuration in the item in the ButtonEditor
        /// e.G. "Example -> example value"
        /// </summary>
        public string ConfigurationSummary { get; set; }
        /// <summary>
        /// true = the action can be configured. The ActionConfigControl needs to be implemented!
        /// </summary>
        public virtual bool CanConfigure { get; } = false;
        /// <summary>
        /// The user control for configuring the action
        /// </summary>
        /// <param name="actionConfigurator"></param>
        /// <returns></returns>
        public virtual ActionConfigControl GetActionConfigControl(ActionConfigurator actionConfigurator) { return null; }


        /// <summary>
        /// Returns a new instance of a plugin action using serilization
        /// </summary>
        /// <param name="pluginAction"></param>
        /// <returns></returns>
        public static PluginAction GetNewInstance(PluginAction pluginAction)
        {
            JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                NullValueHandling = NullValueHandling.Ignore,
                Error = (sender, args) => { args.ErrorContext.Handled = true; }
            };

            // Create a copy of the action button instance
            return JsonConvert.DeserializeObject<PluginAction>(JsonConvert.SerializeObject(pluginAction, jsonSerializerSettings), jsonSerializerSettings);
        }
    } 
}
