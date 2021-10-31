using SuchByte.MacroDeck.Folders.Plugin.GUI;
using SuchByte.MacroDeck.GUI;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Server;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace SuchByte.MacroDeck.Folders.Plugin
{
    public class FolderPlugin : MacroDeckPlugin
    {
        public override string Name => "Folder";
        public override string Author => "Macro Deck";
        public override string Description => "Allows to switch between folders, switch to folders parent and switch to root folder";
        public override void Enable()
        {
            this.Actions = new List<PluginAction>
            {
                new FolderSwitcher(),
                new GoToParentFolder(),
                new GoToRootFolder(),
            };
        }
    }

    public class FolderSwitcher : PluginAction
    {
        public override string Name => "Switch folder";
        public override bool CanConfigure => true;
        public override string Description => "This action allows you to switch between folders.\n\r\n\rConfiguration: yes";
        public override string DisplayName { get; set; } = "Switch folder";
        public override void Trigger(string clientId, ActionButton.ActionButton actionButton)
        {
            MacroDeckServer.SetFolder(MacroDeckServer.GetMacroDeckClient(clientId), MacroDeck.ProfileManager.FindFolderById(long.Parse(this.Configuration), MacroDeckServer.GetMacroDeckClient(clientId).Profile));
        }
        public override ActionConfigControl GetActionConfigControl(ActionConfigurator actionConfigurator)
        {
            return new FolderSwitcherConfigurator(this);
        }
    }

    public class GoToParentFolder : PluginAction
    {
        public override string Name => "Go to parent folder";
        public override string Description => "This action allows you to switch to the folders parent.\n\r\n\rConfiguration: no";
        public override string DisplayName { get; set; } = "Go to parent folder";
        public override void Trigger(string clientId, ActionButton.ActionButton actionButton)
        {
            try
            {
                MacroDeckClient macroDeckClient = MacroDeckServer.GetMacroDeckClient(clientId);
                MacroDeckFolder parentFolder = MacroDeck.ProfileManager.FindParentFolder(macroDeckClient.Folder, macroDeckClient.Profile);
                MacroDeckServer.SetFolder(macroDeckClient, parentFolder);
            } catch { }
        }
        public override ActionConfigControl GetActionConfigControl(ActionConfigurator actionConfigurator)
        {
            return null;
        }
    }

    public class GoToRootFolder : PluginAction
    {
        public override string Name => "Go to root folder";
        public override string Description => "This action allows you to switch to the root folder.\n\r\n\rConfiguration: no";
        public override string DisplayName { get; set; } = "Go to root folder";
        public override void Trigger(string clientId, ActionButton.ActionButton actionButton)
        {
            try
            {
                MacroDeckClient macroDeckClient = MacroDeckServer.GetMacroDeckClient(clientId);
                MacroDeckFolder rootFolder = macroDeckClient.Profile.Folders.Find(folder => folder.FolderId == 0);
                MacroDeckServer.SetFolder(macroDeckClient, rootFolder);
            }
            catch { }
        }
        public override ActionConfigControl GetActionConfigControl(ActionConfigurator actionConfigurator)
        {
            return null;
        }
    }
}
