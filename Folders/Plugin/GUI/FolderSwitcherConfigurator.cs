using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using SuchByte.MacroDeck;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Profiles;
using SuchByte.MacroDeck.Utils;

namespace SuchByte.MacroDeck.Folders.Plugin.GUI
{

    public partial class FolderSwitcherConfigurator : ActionConfigControl
    {
        public MacroDeckFolder SelectedFolder;

        private PluginAction _macroDeckAction;

        public FolderSwitcherConfigurator(PluginAction macroDeckAction)
        {
            if (ProfileManager.CurrentProfile.Folders == null) return;

            this._macroDeckAction = macroDeckAction;
            InitializeComponent();


            foldersView.Nodes.Clear();

            var stack = new Stack<TreeNode>();
            var rootDirectory = ProfileManager.CurrentProfile.Folders[0];
            var node = new TreeNode(rootDirectory.DisplayName) { Tag = rootDirectory };
            stack.Push(node);

            while (stack.Count > 0)
            {
                var currentNode = stack.Pop();
                var directoryInfo = (MacroDeckFolder)currentNode.Tag;
                foreach (var directoryId in directoryInfo.Childs)
                {
                    var directory = ProfileManager.FindFolderById(directoryId, ProfileManager.CurrentProfile);
                    var childDirectoryNode = new TreeNode(directory.DisplayName) { Tag = directory };
                    currentNode.Nodes.Add(childDirectoryNode);
                    stack.Push(childDirectoryNode);
                }
            }

            foldersView.Nodes.Add(node);

            foldersView.ExpandAll();

            foldersView.SelectedNode = foldersView.Nodes[0];
        }

        public override bool OnActionSave()
        {
            if (foldersView.SelectedNode == null) return false;
            MacroDeckFolder folder = ProfileManager.FindFolderByDisplayName(foldersView.SelectedNode.Text, ProfileManager.CurrentProfile);
            this.SelectedFolder = folder;
            long folderId = this.SelectedFolder.FolderId;
            this._macroDeckAction.Configuration = folderId.ToString();
            this._macroDeckAction.ConfigurationSummary = folder.DisplayName;
            return true;
        }
    }
}
