using SuchByte.MacroDeck.Events;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Profiles;
using SuchByte.MacroDeck.Properties;
using SuchByte.MacroDeck.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.MainWindowContents
{
    public partial class DeckView : UserControl
    {
        private Folders.MacroDeckFolder _currentFolder;

        private Control _buttonClicked;

        public DeckView()
        {
            InitializeComponent();
            this.Dock = DockStyle.Fill;
            this.UpdateTranslation();
            this._currentFolder = ProfileManager.CurrentProfile.Folders[0];

        }

        private void MainWindow_ResizeEnd(object sender, EventArgs e)
        {
            this.UpdateButtons();
        }

        public void UpdateTranslation()
        {
            this.Name = Language.LanguageManager.Strings.DeckTitle;
            this.lblColumns.Text = Language.LanguageManager.Strings.Columns;
            this.lblRows.Text = Language.LanguageManager.Strings.Rows;
            this.lblSpacing.Text = Language.LanguageManager.Strings.Spacing;
            this.lblCornerRadius.Text = Language.LanguageManager.Strings.CornerRadius;
            this.checkButtonBackground.Text = Language.LanguageManager.Strings.ButtonBackGround;
            this.foldersContextMenuNew.Text = Language.LanguageManager.Strings.Create;
            this.foldersContextMenuEdit.Text = Language.LanguageManager.Strings.Edit;
            this.foldersContextMenuDelete.Text = Language.LanguageManager.Strings.Delete;
            this.actionButtonContextMenuItemRun.Text = Language.LanguageManager.Strings.Run;
            this.actionButtonContextMenuItemEdit.Text = Language.LanguageManager.Strings.Edit;
            this.actionButtonContextMenuItemCopy.Text = Language.LanguageManager.Strings.Copy;
            this.actionButtonContextMenuItemPaste.Text = Language.LanguageManager.Strings.Paste;
            this.actionButtonContextMenuItemDelete.Text = Language.LanguageManager.Strings.Delete;
        }


        public void UpdateFolders()
        {
            this.foldersView.Nodes.Clear();

                var stack = new Stack<TreeNode>();
                var rootDirectory = ProfileManager.CurrentProfile.Folders[0];
                var node = new TreeNode(rootDirectory.DisplayName) { Tag = rootDirectory };
                stack.Push(node);

                while (stack.Count > 0)
                {
                    var currentNode = stack.Pop();
                    var directoryInfo = (Folders.MacroDeckFolder)currentNode.Tag;
                    foreach (var directoryId in directoryInfo.Childs.ToList())
                    {
                        var directory = ProfileManager.FindFolderById(directoryId, ProfileManager.CurrentProfile);
                        var childDirectoryNode = new TreeNode(directory.DisplayName) { Tag = directory };
                        this.Invoke(new Action(() => currentNode.Nodes.Add(childDirectoryNode)));
                        stack.Push(childDirectoryNode);
                    }
                }
                this.foldersView.Nodes.Add(node);
                this.foldersView.ExpandAll();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }


        public void UpdateButtons(bool clear = false)
        {
            Task.Run(() =>
            {
                this.Invoke(new Action(() => this.SuspendLayout()));
                if (clear)
                {
                    foreach (RoundedButton roundedButton in this.buttonPanel.Controls)
                    {
                        ActionButton.ActionButton actionButton = this._currentFolder.ActionButtons.Find(aB => aB.Position_X == roundedButton.Column && aB.Position_Y == roundedButton.Row);
                        if (actionButton != null)
                        {
                            actionButton.StateChanged -= this.ButtonStateChanged;
                            actionButton.IconChanged -= ActionButton_IconChanged;
                            actionButton.LabelOff.LabelBase64Changed -= this.LabelChanged;
                            actionButton.LabelOn.LabelBase64Changed -= this.LabelChanged;
                        }
                        
                        roundedButton.MouseDown -= this.ActionButton_Down;
                        roundedButton.DragDrop -= Button_DragDrop;
                        roundedButton.DragEnter -= Button_DragEnter;
                        this.Invoke(new Action(() => roundedButton.Dispose()));

                    }
                    this.Invoke(new Action(() => this.buttonPanel.Controls.Clear()));
                }


                // Button size algorithm from @PhoenixWyllow

                int buttonSize = 100;
                int rows = ProfileManager.CurrentProfile.Rows, columns = ProfileManager.CurrentProfile.Columns, spacing = ProfileManager.CurrentProfile.ButtonSpacing; // from settings
                int width = buttonPanel.Width, height = buttonPanel.Height; // from panel
                int buttonSizeX, buttonSizeY; // for convenience

                if (rows > columns) //if NOT "normal case" (ie, user attempting vertical layout), force horizontal
                {
                    rows = ProfileManager.CurrentProfile.Columns;
                    columns = ProfileManager.CurrentProfile.Rows;

                }
                buttonSizeX = width / columns; //calc with spacing, remove it after
                buttonSizeY = height / rows;
                buttonSize = Math.Min(buttonSizeX, buttonSizeY) - spacing;

                for (int row = 0; row < rows; row++)
                {
                    for (int column = 0; column < columns; column++)
                    {
                        this.Invoke(new Action(() => this.LoadButton(row, column, buttonSize)));
                    }
                }
                this.Invoke(new Action(() => this.ResumeLayout()));
            });

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private void LabelChanged(object sender, EventArgs e)
        {
            if (this.Disposing || this.IsDisposed) return;
            try
            {
                ActionButton.ActionButton actionButton = this._currentFolder.ActionButtons.Find(aB => (aB.LabelOff != null && aB.LabelOff.Equals(sender)) || (aB.LabelOn != null && aB.LabelOn.Equals(sender)));
                this.Invoke(new Action(() =>
                {
                    if (actionButton != null)
                    {
                        this.UpdateButtonIcon(actionButton);
                    }
                }));
            }
            catch { }
        }

        private void ButtonStateChanged(object sender, EventArgs e)
        {
            if (this.Disposing || this.IsDisposed) return;
            try
            {
                ActionButton.ActionButton actionButton = (ActionButton.ActionButton)sender;
                this.Invoke(new Action(() =>
                {
                    if (this._currentFolder.ActionButtons.Contains(actionButton))
                    {
                        this.UpdateButtonIcon(actionButton);
                    }
                }));
            } catch { }
        }

        private void UpdateButtonIcon(ActionButton.ActionButton actionButton, RoundedButton button = null)
        {
            if (button == null)
            {
                foreach (RoundedButton roundedButton in this.buttonPanel.Controls)
                {
                    if (roundedButton.Row.Equals(actionButton.Position_Y) && roundedButton.Column.Equals(actionButton.Position_X))
                    {
                        button = roundedButton;
                    }
                }
            }

            if (button == null) return;

            if (actionButton != null)
            {
                if (actionButton.State == false)
                {
                    if (actionButton.LabelOff != null && actionButton.LabelOff.LabelBase64.Length > 0)
                    {
                        Image labelImage = Utils.Base64.GetImageFromBase64(actionButton.LabelOff.LabelBase64);
                        button.ForegroundImage = labelImage;
                    }

                    if (actionButton.IconOff != null && actionButton.IconOff.Split(".").Length > 1)
                    {
                        Icons.IconPack iconPack = IconManager.GetIconPackByName(actionButton.IconOff.Split(".")[0]);
                        Icons.Icon icon = IconManager.GetIcon(iconPack, long.Parse(actionButton.IconOff.Split(".")[1]));
                        if (iconPack != null && icon != null)
                        {
                            button.BackgroundImage = icon.IconImage;
                        }
                    }
                }
                else
                {
                    if (actionButton.LabelOn != null && actionButton.LabelOn.LabelBase64.Length > 0)
                    {
                        Image labelImage = Utils.Base64.GetImageFromBase64(actionButton.LabelOn.LabelBase64);
                        button.ForegroundImage = labelImage;
                    }

                    if (actionButton.IconOn != null && actionButton.IconOn.Split(".").Length > 1)
                    {
                        Icons.IconPack iconPack = IconManager.GetIconPackByName(actionButton.IconOn.Split(".")[0]);
                        Icons.Icon icon = IconManager.GetIcon(iconPack, long.Parse(actionButton.IconOn.Split(".")[1]));
                        if (iconPack != null && icon != null)
                        {
                            button.BackgroundImage = icon.IconImage;
                        }
                    }
                }

            }

        }


        private void LoadButton(int row, int column, int buttonSize)
        {
            RoundedButton button = null;
            foreach (RoundedButton roundedButton in this.buttonPanel.Controls)
            {
                if (roundedButton.Row.Equals(row) && roundedButton.Column.Equals(column))
                {
                    button = roundedButton;
                }
            }

            if (button == null)
            {
                button = new RoundedButton
                {
                    BackgroundImageLayout = ImageLayout.Stretch,
                    SizeMode = PictureBoxSizeMode.StretchImage,
                    Row = row,
                    Column = column,
                    ForeColor = Color.White
                };
                button.MouseDown += this.ActionButton_Down;
                button.DragDrop += Button_DragDrop;
                button.DragEnter += Button_DragEnter;
                button.AllowDrop = true;
                button.Cursor = Cursors.Hand;
            }

            button.Width = buttonSize;
            button.Height = buttonSize;
            button.Radius = ProfileManager.CurrentProfile.ButtonRadius;
            button.Location = new Point((column * buttonSize) + (column * ProfileManager.CurrentProfile.ButtonSpacing), (row * buttonSize) + (row * ProfileManager.CurrentProfile.ButtonSpacing));
            button.BackColor = ProfileManager.CurrentProfile.ButtonBackground ? Color.FromArgb(35, 35, 35) : Color.Transparent;

            if (button == null) return;
            if (button.BackgroundImage != null)
            {
                button.BackgroundImage = null;
            }
            if (button.ForegroundImage != null)
            {
                button.ForegroundImage = null;
            }
            if (button.Image != null)
            {
                button.Image = null;
            }

            ActionButton.ActionButton actionButton = this._currentFolder.ActionButtons.Find(aB => aB.Position_X == column && aB.Position_Y == row);
            if (actionButton != null)
            {
                // Remove event handlers if there are any to prevent multiple event handlers
                actionButton.StateChanged -= this.ButtonStateChanged;
                actionButton.LabelOff.LabelBase64Changed -= this.LabelChanged;
                actionButton.LabelOn.LabelBase64Changed -= this.LabelChanged;
                actionButton.IconChanged -= ActionButton_IconChanged;
                // Add event handlers
                actionButton.StateChanged += this.ButtonStateChanged;
                actionButton.LabelOff.LabelBase64Changed += this.LabelChanged;
                actionButton.LabelOn.LabelBase64Changed += this.LabelChanged;
                actionButton.IconChanged += ActionButton_IconChanged;

                this.UpdateButtonIcon(actionButton, button);
            }

           

            if (!this.buttonPanel.Controls.Contains(button))
            {
                this.buttonPanel.Controls.Add(button);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private void ActionButton_IconChanged(object sender, EventArgs e)
        {
            ActionButton.ActionButton actionButton = sender as ActionButton.ActionButton;
            if (actionButton != null)
            {
                if (this._currentFolder.ActionButtons.Contains(actionButton))
                {
                    this.UpdateButtonIcon(actionButton);
                }
            }
        }

        private void Button_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) != null)
            {

            }
            else
            {
                RoundedButton draggedButton = (RoundedButton)e.Data.GetData(typeof(RoundedButton));
                RoundedButton targetButton = (RoundedButton)sender;

                ActionButton.ActionButton targetActionButton = ProfileManager.FindActionButton(this._currentFolder, targetButton.Row, targetButton.Column);
                ActionButton.ActionButton draggedActionButton = ProfileManager.FindActionButton(this._currentFolder, draggedButton.Row, draggedButton.Column);

                if (draggedActionButton == null) return;

                ActionButton.ActionButton newActionButton = draggedActionButton;
                newActionButton.Position_Y = targetButton.Row;
                newActionButton.Position_X = targetButton.Column;

                this._currentFolder.ActionButtons.Remove(draggedActionButton);
                this._currentFolder.ActionButtons.Add(newActionButton);

                if (targetActionButton != null)
                {
                    ActionButton.ActionButton newTargetButton = targetActionButton;
                    newTargetButton.Position_Y = draggedButton.Row;
                    newTargetButton.Position_X = draggedButton.Column;
                    this._currentFolder.ActionButtons.Remove(targetActionButton);
                    this._currentFolder.ActionButtons.Add(newTargetButton);

                    foreach (PluginAction pluginAction in newTargetButton.Actions)
                    {
                        pluginAction.SetActionButton(newTargetButton);
                    }
                    foreach (var eventListener in newTargetButton.EventListeners)
                    {
                        foreach (PluginAction pluginAction in eventListener.Actions)
                        {
                            pluginAction.SetActionButton(newTargetButton);
                        }
                    }
                }
                ProfileManager.Save();
                this.UpdateButtons();
                MacroDeckServer.UpdateFolder(this._currentFolder);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        private void Button_DragEnter(object sender, DragEventArgs e)
        {
            RoundedButton button = ((RoundedButton)e.Data.GetData(typeof(RoundedButton)));
            if (button != null)
            {
                if (button.Equals((RoundedButton)sender)) return;
                e.Effect = DragDropEffects.Move;
            }
            else
            {
                e.Effect = DragDropEffects.Copy;
            }
        }

        private void ActionButton_Down(object sender, MouseEventArgs e)
        {
            RoundedButton button = (RoundedButton)sender;
            
            this._buttonClicked = button;

            switch (e.Button)
            {
                case MouseButtons.Left:
                    if (e.Clicks == 2)
                    {
                        OpenButtonEditor(button);
                    }
                    else if (e.Clicks == 1)
                    {
                        button.DoDragDrop(button, DragDropEffects.Move);
                    }
                    break;
                case MouseButtons.Right:
                    this.actionButtonContextMenu.Show(button, button.PointToClient(Control.MousePosition));
                    break;
            }
        }

        private void RunToolStripMenuItem_Click(object sender, EventArgs e)
        {
            int row = ((RoundedButton)this._buttonClicked).Row;
            int col = ((RoundedButton)this._buttonClicked).Column;
            ActionButton.ActionButton actionButton = this._currentFolder.ActionButtons.Find(aB => aB.Position_X == col && aB.Position_Y == row);
            if (actionButton != null)
            {
                try
                {
                    MacroDeckServer.Trigger(actionButton, "");
                } catch { }
            }
        }

        public void ContextMenuEditItemClick(object sender, EventArgs e)
        {
            this.OpenButtonEditor((RoundedButton)this._buttonClicked);
        }
        public void ContextMenuDeleteItemClick(object sender, EventArgs e)
        {
            int row = ((RoundedButton)this._buttonClicked).Row;
            int col = ((RoundedButton)this._buttonClicked).Column;
            ActionButton.ActionButton actionButton = this._currentFolder.ActionButtons.Find(aB => aB.Position_X == col && aB.Position_Y == row);
            if (actionButton != null)
            {
                actionButton.Dispose();
                this._currentFolder.ActionButtons.Remove(actionButton);
                ProfileManager.Save();
                this.UpdateButtons();
                MacroDeckServer.UpdateFolder(this._currentFolder);
            }
        }

        public void OpenButtonEditor(RoundedButton button)
        {
            int row = button.Row;
            int column = button.Column;

            ActionButton.ActionButton actionButton = this._currentFolder.ActionButtons.Find(aB => aB.Position_X == column && aB.Position_Y == row);
            if (actionButton == null)
            {
                actionButton = new ActionButton.ActionButton
                {
                    Actions = new List<PluginAction>(),
                    EventListeners = new List<EventListener>(),
                    ButtonId = this._currentFolder.ActionButtons.Count,
                    Position_Y = row,
                    Position_X = column,
                    IconOff = "",
                    IconOn = "",
                    LabelOff = new ActionButton.ButtonLabel(),
                    LabelOn = new ActionButton.ButtonLabel(),
                };
            }


            using (var buttonEditor = new ButtonEditor(actionButton, this._currentFolder))
            {
                buttonEditor.ShowDialog();
                ProfileManager.Save();
                this.UpdateButtons();
            }

        }

        private void FoldersView_AfterSelect(object sender, TreeViewEventArgs e)
        {
            this._currentFolder = ProfileManager.FindFolderByDisplayName(foldersView.SelectedNode.Text, ProfileManager.CurrentProfile);
            this.UpdateButtons();
        }

        private void FoldersView_MouseDown(object sender, MouseEventArgs e)
        {

            switch (e.Button)
            {
                case MouseButtons.Right:
                    this.foldersView.SelectedNode = this.foldersView.GetNodeAt(e.X, e.Y);
                    if (this.foldersView.SelectedNode == null) this.foldersView.SelectedNode = this.foldersView.Nodes[0];
                    this.foldersContextMenu.Show(this.foldersView, this.foldersView.PointToClient(Control.MousePosition));
                    break;
            }
        }

        private void Deck_Load(object sender, EventArgs e)
        {
            this.LoadProfiles();
            this.LoadProfileSettings();
            this.UpdateButtons();
            CustomControls.Form mainWindow = (CustomControls.Form)this.Parent.Parent;
            mainWindow.ResizeEnd += MainWindow_ResizeEnd;
        }

        private void LoadProfiles()
        {
            this.boxProfiles.Items.Clear();
            foreach (MacroDeckProfile macroDeckProfile in ProfileManager.Profiles)
            {
                this.boxProfiles.Items.Add(macroDeckProfile.DisplayName);
            }
            this.boxProfiles.Text = ProfileManager.CurrentProfile.DisplayName;
        }

        private void BtnGoToRoot_Click(object sender, EventArgs e)
        {
            this.foldersView.SelectedNode = this.foldersView.Nodes[0];
        }

        private void BtnCreateFolder_Click(object sender, EventArgs e)
        {
            Folders.MacroDeckFolder selectedFolder = ProfileManager.FindFolderByDisplayName(foldersView.SelectedNode.Text, ProfileManager.CurrentProfile);
            if (selectedFolder == null)
                selectedFolder = ProfileManager.CurrentProfile.Folders[0];

            using (var addFolder = new AddFolder(selectedFolder))
            {
                if (addFolder.ShowDialog() == DialogResult.OK)
                {
                    this.UpdateFolders();
                }
            }
        }

        private void BtnRenameFolder_Click(object sender, EventArgs e)
        {
            Folders.MacroDeckFolder selectedFolder = ProfileManager.FindFolderByDisplayName(foldersView.SelectedNode.Text, ProfileManager.CurrentProfile);
            if (selectedFolder == null)
                selectedFolder = ProfileManager.CurrentProfile.Folders[0];

            using (var addFolder = new AddFolder(selectedFolder, true))
            {
                if (addFolder.ShowDialog() == DialogResult.OK)
                {
                    this.UpdateFolders();
                }
            }
        }

        private void BtnDeleteFolder_Click(object sender, EventArgs e)
        {
            Folders.MacroDeckFolder selectedFolder = ProfileManager.FindFolderByDisplayName(foldersView.SelectedNode.Text, ProfileManager.CurrentProfile);
            if (selectedFolder.Equals(ProfileManager.CurrentProfile.Folders[0])) return;
            using (var msgBox = new CustomControls.MessageBox())
            {
                if (msgBox.ShowDialog(Language.LanguageManager.Strings.AreYouSure, String.Format(Language.LanguageManager.Strings.TheFolderWillBeDeleted, selectedFolder.DisplayName), MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    if (this._currentFolder == selectedFolder)
                    {
                        this._currentFolder = ProfileManager.CurrentProfile.Folders[0];
                        UpdateButtons();
                    }

                    ProfileManager.DeleteFolder(selectedFolder, ProfileManager.CurrentProfile);
                    this.UpdateFolders();
                }
            }
        }

        private void ActionButtonContextMenuItemCopy_Click(object sender, EventArgs e)
        {
            RoundedButton button = (RoundedButton)this._buttonClicked;
            int row = button.Row;
            int col = button.Column;
            ActionButton.ActionButton actionButton = this._currentFolder.ActionButtons.Find(aB => aB.Position_X == col && aB.Position_Y == row);
            if (actionButton != null)
            {
                Utils.Clipboard.CopyActionButton(actionButton);
            }
        }

        private void ActionButtonContextMenuOpened(object sender, EventArgs e)
        {
            RoundedButton button = (RoundedButton)this._buttonClicked;
            int row = button.Row;
            int col = button.Column;
            ActionButton.ActionButton actionButton = this._currentFolder.ActionButtons.Find(aB => aB.Position_X == col && aB.Position_Y == row);
            this.actionButtonContextMenuItemRun.Enabled = !(actionButton == null);
            this.actionButtonContextMenuItemCopy.Enabled = !(actionButton == null);
            this.actionButtonContextMenuItemPaste.Enabled = !(Utils.Clipboard.GetActionButtonCopy() == null);
            this.actionButtonContextMenuItemDelete.Enabled = !(actionButton == null);
        }

        private void ActionButtonContextMenuItemPaste_Click(object sender, EventArgs e)
        {
            RoundedButton button = (RoundedButton)this._buttonClicked;
            int row = button.Row;
            int col = button.Column;
            ActionButton.ActionButton actionButtonOld = this._currentFolder.ActionButtons.Find(aB => aB.Position_X == col && aB.Position_Y == row);

            if (actionButtonOld != null)
            {
                this._currentFolder.ActionButtons.Remove(actionButtonOld);
            }

            if (Utils.Clipboard.GetActionButtonCopy() == null) return;

            ActionButton.ActionButton actionButtonNew = Utils.Clipboard.GetActionButtonCopy();
            foreach (PluginAction pluginAction in actionButtonNew.Actions)
            {
                pluginAction.SetActionButton(actionButtonNew);
            }
            foreach (var eventListener in actionButtonNew.EventListeners)
            {
                foreach (PluginAction pluginAction in eventListener.Actions)
                {
                    pluginAction.SetActionButton(actionButtonNew);
                }
            }
            actionButtonNew.ButtonId = this._currentFolder.ActionButtons.Count;
            actionButtonNew.Position_X = col;
            actionButtonNew.Position_Y = row;


            this._currentFolder.ActionButtons.Add(actionButtonNew);

            ProfileManager.Save();
            this.UpdateButtons();
            MacroDeckServer.UpdateFolder(this._currentFolder);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private void BoxProfiles_SelectedIndexChanged(object sender, EventArgs e)
        {
            MacroDeckProfile profile = ProfileManager.FindProfileByDisplayName(this.boxProfiles.Text);
            if (profile == null)
            {
                profile = ProfileManager.Profiles[0];
            }
            ProfileManager.CurrentProfile = profile;
            this._currentFolder = profile.Folders[0];
            this.UpdateButtons(true);
            this.LoadProfileSettings();
            this.UpdateFolders();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private void LoadProfileSettings()
        {
            this.buttonRows.ValueChanged -= this.ButtonSettingsChanged;
            this.buttonColumns.ValueChanged -= this.ButtonSettingsChanged;
            this.buttonSpacing.ValueChanged -= this.ButtonSettingsChanged;
            this.cornerRadius.ValueChanged -= this.ButtonSettingsChanged;
            this.checkButtonBackground.CheckedChanged -= this.ButtonSettingsChanged;

            this.buttonRows.Value = ProfileManager.CurrentProfile.Rows;
            this.buttonColumns.Value = ProfileManager.CurrentProfile.Columns;
            this.buttonSpacing.Value = ProfileManager.CurrentProfile.ButtonSpacing;
            this.cornerRadius.Value = ProfileManager.CurrentProfile.ButtonRadius;
            this.checkButtonBackground.Checked = ProfileManager.CurrentProfile.ButtonBackground;
            this.buttonRows.ValueChanged += this.ButtonSettingsChanged;
            this.buttonColumns.ValueChanged += this.ButtonSettingsChanged;
            this.buttonSpacing.ValueChanged += this.ButtonSettingsChanged;
            this.cornerRadius.ValueChanged += this.ButtonSettingsChanged;
            this.checkButtonBackground.CheckedChanged += this.ButtonSettingsChanged;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }


        private void BtnAddProfile_Click(object sender, EventArgs e)
        {
            using (var createProfileDialog = new CreateProfileDialog())
            {
                if (createProfileDialog.ShowDialog() == DialogResult.OK)
                {
                    ProfileManager.CurrentProfile = createProfileDialog.Profile;
                    this.LoadProfiles();
                    this._currentFolder = ProfileManager.CurrentProfile.Folders[0];
                    this.UpdateButtons(true);
                    this.LoadProfileSettings();
                    this.UpdateFolders();
                }
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private void BtnEditProfile_Click(object sender, EventArgs e)
        {
            using (var createProfileDialog = new CreateProfileDialog(ProfileManager.CurrentProfile))
            {
                if (createProfileDialog.ShowDialog() == DialogResult.OK)
                {
                    this.LoadProfiles();
                }
            }
        }

        private void BtnDeleteProfile_Click(object sender, EventArgs e)
        {
            if (ProfileManager.Profiles.Count < 2) return;
            using (var msgBox = new CustomControls.MessageBox())
            {
                if (msgBox.ShowDialog(Language.LanguageManager.Strings.AreYouSure, String.Format(Language.LanguageManager.Strings.TheProfileWillBeDeleted, ProfileManager.CurrentProfile.DisplayName), MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    ProfileManager.DeleteProfile(ProfileManager.CurrentProfile);
                    ProfileManager.CurrentProfile = ProfileManager.Profiles[0];
                    this.LoadProfiles();
                    this._currentFolder = ProfileManager.CurrentProfile.Folders[0];
                    this.UpdateButtons(true);
                    this.LoadProfileSettings();
                    this.UpdateFolders();
                }
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private void ButtonSettingsChanged(object sender, EventArgs e)
        {
            ProfileManager.CurrentProfile.Rows = (int)buttonRows.Value;
            ProfileManager.CurrentProfile.Columns = (int)buttonColumns.Value;
            ProfileManager.CurrentProfile.ButtonSpacing = (int)buttonSpacing.Value;
            ProfileManager.CurrentProfile.ButtonRadius = (int)cornerRadius.Value;
            ProfileManager.CurrentProfile.ButtonBackground = checkButtonBackground.Checked;
            ProfileManager.Save();
            this.UpdateButtons(true);
            foreach (MacroDeckClient macroDeckClient in MacroDeckServer.Clients.FindAll(macroDeckClient => macroDeckClient.Profile.ProfileId.Equals(ProfileManager.CurrentProfile.ProfileId)))
            {
                MacroDeckServer.SetProfile(macroDeckClient, ProfileManager.CurrentProfile);
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        
    }
}
