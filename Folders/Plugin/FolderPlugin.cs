using SuchByte.MacroDeck.Folders.Plugin.GUI;
using SuchByte.MacroDeck.GUI;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Profiles;
using SuchByte.MacroDeck.Server;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace SuchByte.MacroDeck.Folders.Plugin
{
    public class FolderPlugin : MacroDeckPlugin
    {
        public override string Name => LanguageManager.Strings.PluginMacroDeckFolder;
        public override string Author => "Macro Deck";
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
        public override string Name => LanguageManager.Strings.ActionChangeFolder;
        public override bool CanConfigure => true;
        public override string Description => LanguageManager.Strings.ActionChangeFolderDescription;

        public override void Trigger(string clientId, ActionButton.ActionButton actionButton)
        {
            MacroDeckLogger.Trace("Switch folder triggered by " + clientId);
            switch (clientId)
            {
                // ClientID -1 or "" = Macro Deck software itself
                case "":
                case "-1":
                    if (MacroDeck.MainWindow != null && MacroDeck.MainWindow.DeckView != null)
                    {
                        MacroDeck.MainWindow.DeckView.SetFolder(ProfileManager.FindFolderById(this.Configuration, ProfileManager.CurrentProfile));
                    }
                    break;
                // ClientId != -1 = Connected device
                default:
                    MacroDeckServer.SetFolder(MacroDeckServer.GetMacroDeckClient(clientId), ProfileManager.FindFolderById(this.Configuration, MacroDeckServer.GetMacroDeckClient(clientId).Profile));
                    break;
            }
        }
        public override ActionConfigControl GetActionConfigControl(ActionConfigurator actionConfigurator)
        {
            return new FolderSwitcherConfigurator(this);
        }
    }

    public class GoToParentFolder : PluginAction
    {
        public override string Name => LanguageManager.Strings.ActionGoToParentFolder;
        public override string Description => LanguageManager.Strings.ActionGoToParentFolderDescription;
        public override void Trigger(string clientId, ActionButton.ActionButton actionButton)
        {
            try
            {
                MacroDeckLogger.Trace("Go to parent folder triggered by " + clientId);
                switch (clientId)
                {
                    // ClientID -1 or "" = Macro Deck software itself
                    case "":
                    case "-1":
                        if (MacroDeck.MainWindow != null && MacroDeck.MainWindow.DeckView != null)
                        {
                            MacroDeck.MainWindow.DeckView.SetFolder(ProfileManager.FindParentFolder(MacroDeck.MainWindow.DeckView.CurrentFolder, ProfileManager.CurrentProfile));
                        }
                        break;
                    // ClientId != -1 = Connected device
                    default:
                        MacroDeckClient macroDeckClient = MacroDeckServer.GetMacroDeckClient(clientId);
                        MacroDeckFolder parentFolder = ProfileManager.FindParentFolder(macroDeckClient.Folder, macroDeckClient.Profile);
                        MacroDeckServer.SetFolder(macroDeckClient, parentFolder);
                        break;
                }
            } catch { }
        }
        public override ActionConfigControl GetActionConfigControl(ActionConfigurator actionConfigurator)
        {
            return null;
        }
    }

    public class GoToRootFolder : PluginAction
    {
        public override string Name => LanguageManager.Strings.ActionGoToRootFolder;
        public override string Description => LanguageManager.Strings.ActionGoToRootFolderDescription;
        public override void Trigger(string clientId, ActionButton.ActionButton actionButton)
        {
            try
            {
                MacroDeckLogger.Trace("Go to root folder triggered by " + clientId);
                switch (clientId)
                {
                    // ClientID -1 or "" = Macro Deck software itself
                    case "":
                    case "-1":
                        if (MacroDeck.MainWindow != null && MacroDeck.MainWindow.DeckView != null)
                        {
                            MacroDeck.MainWindow.DeckView.SetFolder(ProfileManager.CurrentProfile.Folders.Find(folder => folder.IsRootFolder));
                        }
                        break;
                    // ClientId != -1 = Connected device
                    default:
                        MacroDeckClient macroDeckClient = MacroDeckServer.GetMacroDeckClient(clientId);
                        MacroDeckFolder rootFolder = macroDeckClient.Profile.Folders.Find(folder => folder.IsRootFolder);
                        MacroDeckServer.SetFolder(macroDeckClient, rootFolder);
                        break;
                }
                
            }
            catch { }
        }
        public override ActionConfigControl GetActionConfigControl(ActionConfigurator actionConfigurator)
        {
            return null;
        }
    }
}
