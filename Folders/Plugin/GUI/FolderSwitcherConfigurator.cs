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

namespace SuchByte.MacroDeck.Folders.Plugin.GUI
{
    public partial class FolderSwitcherConfigurator : ActionConfigControl
    {
        public MacroDeckFolder SelectedFolder;

        private PluginAction _macroDeckAction;

        public FolderSwitcherConfigurator(PluginAction macroDeckAction)
        {
            if (MacroDeck.ProfileManager == null || MacroDeck.ProfileManager.CurrentProfile.Folders == null) return;

            this._macroDeckAction = macroDeckAction;
            InitializeComponent();


            foldersView.Nodes.Clear();

            var stack = new Stack<TreeNode>();
            var rootDirectory = MacroDeck.ProfileManager.CurrentProfile.Folders[0];
            var node = new TreeNode(rootDirectory.DisplayName) { Tag = rootDirectory };
            stack.Push(node);

            while (stack.Count > 0)
            {
                var currentNode = stack.Pop();
                var directoryInfo = (MacroDeckFolder)currentNode.Tag;
                foreach (var directoryId in directoryInfo.Childs)
                {
                    var directory = MacroDeck.ProfileManager.FindFolderById(directoryId, MacroDeck.ProfileManager.CurrentProfile);
                    var childDirectoryNode = new TreeNode(directory.DisplayName) { Tag = directory };
                    currentNode.Nodes.Add(childDirectoryNode);
                    stack.Push(childDirectoryNode);
                }
            }

            foldersView.Nodes.Add(node);

            foldersView.ExpandAll();

            foldersView.SelectedNode = foldersView.Nodes[0];
        }


        private void FoldersView_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (foldersView.SelectedNode == null) return;
            MacroDeckFolder folder = MacroDeck.ProfileManager.FindFolderByDisplayName(foldersView.SelectedNode.Text, MacroDeck.ProfileManager.CurrentProfile);
            this.SelectedFolder = folder;
            long folderId = this.SelectedFolder.FolderId;
            this._macroDeckAction.Configuration = folderId.ToString();
            this._macroDeckAction.DisplayName = this._macroDeckAction.Name + " -> " + folder.DisplayName;
        }
    }
}
