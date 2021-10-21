﻿using SuchByte.MacroDeck.GUI;
using SuchByte.MacroDeck.GUI.CustomControls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace SuchByte.MacroDeck.Plugins
{
    public abstract class MacroDeckPlugin
    {
        Assembly executingAssembly = Assembly.GetCallingAssembly();
        FileVersionInfo versionInfo;

        private string _name, _version, _description;


        public MacroDeckPlugin()
        {
            this.versionInfo = FileVersionInfo.GetVersionInfo(executingAssembly.Location);
            this._name = this.executingAssembly.GetName().Name;
            this._version = this.versionInfo.ProductVersion;
            this._description = this.versionInfo.FileDescription;
        }


        /// <summary>
        /// Please change only in a very special case! You can delete this from your class.
        /// </summary>
        public virtual string Name
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
        /// Please change only in a very special case! You can delete this from your class.
        /// </summary>
        public virtual string Version
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
        /// The author of the plugin
        /// </summary>
        public virtual string Author { get; set; }

        /// <summary>
        /// This gets pulled automatically out of the version info. Please change only in a very special case! You can delete this from your class.
        /// </summary>
        public virtual string Description {
            get
            {
                return this._description;
            }
            set
            {
                this._description = value;
            }
        }
        /// <summary>
        /// This list contains all the actions of the plugin. If your plugin does not contain any actions, you can delete this.
        /// </summary>
        public List<PluginAction> Actions { get; set; }
        /// <summary>
        /// Set a custom icon that will be shown in the package manager and in the action configurator.
        /// </summary>
        public virtual Image Icon { get; }
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
    }

    public abstract class PluginAction
    {
        /// <summary>
        /// Name of the action
        /// </summary>
        public abstract string Name { get; }
        /// <summary>
        /// Display name of the action; can be changed after configuration
        /// </summary>
        public abstract string DisplayName { get; set; }
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
        /// true = the action can be configured. The ActionConfigControl needs to be implemented!
        /// </summary>
        public virtual bool CanConfigure { get; } = false;
        /// <summary>
        /// The user control for configuring the action
        /// </summary>
        /// <param name="actionConfigurator"></param>
        /// <returns></returns>
        public virtual ActionConfigControl GetActionConfigControl(ActionConfigurator actionConfigurator) { return null; }
    }

    /*[Obsolete("IMacroDeckPlugin is deprecated, please use the abstract MacroDeckPlugin instead. Will be deleted in version 2b15.")]
    public interface IMacroDeckPlugin
    {
        string Name { get; }
        string Version { get; }
        string Author { get; }
        string Description { get; }
        List<IMacroDeckAction> Actions { get; }
        Image Icon { get; }
        bool CanConfigure { get; }
        void OpenConfigurator();
        void Enable();
    }

    [Obsolete("IMacroDeckAction is deprecated, please use the abstract PluginAction instead. Will be deleted in version 2b15.")]
    public interface IMacroDeckAction
    {
        string Name { get; }
        string Description { get; }
        string DisplayName { get; set; }
        void Trigger(string clientId, ActionButton.ActionButton actionButton);
        string Configuration { get; set; }
        bool CanConfigure { get; }
        ActionConfigControl GetActionConfigurator(ActionConfigurator actionConfigurator);
    }*/

 
}
