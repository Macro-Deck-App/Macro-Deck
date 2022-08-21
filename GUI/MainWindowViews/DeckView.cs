using SuchByte.MacroDeck.Enums;
using SuchByte.MacroDeck.Events;
using SuchByte.MacroDeck.Folders;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Logging;
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
        private MacroDeckFolder _currentFolder;

        private Control _buttonClicked;

        public MacroDeckFolder CurrentFolder
        {
            get
            {
                return this._currentFolder;
            }
        }

        public DeckView()
        {
            InitializeComponent();
            this.Dock = DockStyle.Fill;
            this.UpdateTranslation();
            this._currentFolder = ProfileManager.CurrentProfile.Folders.FirstOrDefault();

        }

        private void MainWindow_ResizeEnd(object sender, EventArgs e)
        {
            this.UpdateButtons();
        }

        public void UpdateTranslation()
        {
            this.Name = LanguageManager.Strings.DeckTitle;
            this.lblColumns.Text = LanguageManager.Strings.Columns;
            this.lblRows.Text = LanguageManager.Strings.Rows;
            this.lblSpacing.Text = LanguageManager.Strings.Spacing;
            this.lblCornerRadius.Text = LanguageManager.Strings.CornerRadius;
            this.checkButtonBackground.Text = LanguageManager.Strings.ButtonBackGround;
            this.foldersContextMenuNew.Text = LanguageManager.Strings.Create;
            this.foldersContextMenuEdit.Text = LanguageManager.Strings.Edit;
            this.foldersContextMenuDelete.Text = LanguageManager.Strings.Delete;
            this.actionButtonContextMenuItemEdit.Text = LanguageManager.Strings.Edit;
            this.actionButtonContextMenuItemCopy.Text = LanguageManager.Strings.Copy;
            this.actionButtonContextMenuItemPaste.Text = LanguageManager.Strings.Paste;
            this.actionButtonContextMenuItemDelete.Text = LanguageManager.Strings.Delete;
            this.actionButtonContextMenuItemSimulatePress.Text = LanguageManager.Strings.SimulateOnPress;
            this.actionButtonContextMenuItemSimulateRelease.Text = LanguageManager.Strings.SimulateOnRelease;
            this.actionButtonContextMenuItemSimulateLongPress.Text = LanguageManager.Strings.SimulateOnLongPress;
            this.actionButtonContextMenuItemSimulateLongPressRelease.Text = LanguageManager.Strings.SimulateOnLongPressRelease;
            this.lblFolders.Text = LanguageManager.Strings.Folders;
            this.lblGrid.Text = LanguageManager.Strings.Grid;
        }


        public void UpdateFolders()
        {
            this.foldersView.Nodes.Clear();

            var stack = new Stack<TreeNode>();
            var rootDirectory = ProfileManager.CurrentProfile.Folders.FirstOrDefault();
            var node = new TreeNode(rootDirectory.DisplayName) { Tag = rootDirectory };
            stack.Push(node);

            while (stack.Count > 0)
            {
                var currentNode = stack.Pop();
                var directoryInfo = (Folders.MacroDeckFolder)currentNode.Tag;
                foreach (var directoryId in directoryInfo.Childs.ToList())
                {
                    try
                    {
                        var directory = ProfileManager.FindFolderById(directoryId, ProfileManager.CurrentProfile);
                        var childDirectoryNode = new TreeNode(directory.DisplayName) { Tag = directory };
                        this.Invoke(new Action(() => currentNode.Nodes.Add(childDirectoryNode)));
                        stack.Push(childDirectoryNode);
                    } catch { }
                }
            }
            this.foldersView.Nodes.Add(node);
            this.foldersView.ExpandAll();
        }


        public void UpdateButtons(bool clear = false)
        {
            if (clear)
            {
                foreach (RoundedButton roundedButton in this.buttonPanel.Controls)
                {
                    var actionButton = this._currentFolder.ActionButtons.Find(aB => aB.Position_X == roundedButton.Column && aB.Position_Y == roundedButton.Row);
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

            int buttonSize = 100;
            int rows = ProfileManager.CurrentProfile.Rows, columns = ProfileManager.CurrentProfile.Columns, spacing = ProfileManager.CurrentProfile.ButtonSpacing; // from settings
            int width = buttonPanel.Width, height = buttonPanel.Height; // from panel
            int buttonSizeX, buttonSizeY;

            buttonSizeX = width / columns; //calc with spacing, remove it after
            buttonSizeY = height / rows;
            buttonSize = Math.Min(buttonSizeX, buttonSizeY) - spacing;


            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    this.LoadButton(row, column, buttonSize);
                }
            }
        }

        private void LabelChanged(object sender, EventArgs e)
        {
            var actionButton = this._currentFolder.ActionButtons.Find(aB => (aB.LabelOff != null && aB.LabelOff.Equals(sender)) || (aB.LabelOn != null && aB.LabelOn.Equals(sender)));
            this.UpdateButtonIcon(actionButton);
        }

        private void ButtonStateChanged(object sender, EventArgs e)
        {
            ActionButton.ActionButton actionButton = (ActionButton.ActionButton)sender;
            this.UpdateButtonIcon(actionButton);
        }

        private void UpdateButtonIcon(ActionButton.ActionButton actionButton, RoundedButton button = null)
        {
            if (this.IsDisposed || !this.IsHandleCreated || actionButton == null) return;
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateButtonIcon(actionButton, button)));
                return;
            }

            button ??= this.buttonPanel.Controls.Cast<RoundedButton>().Where(x => x.Row.Equals(actionButton.Position_Y) && x.Column.Equals(actionButton.Position_X)).FirstOrDefault();

            if (actionButton.State == true)
            {
                if (actionButton.LabelOn != null && !string.IsNullOrWhiteSpace(actionButton.LabelOn.LabelBase64))
                {
                    Image labelImage = Utils.Base64.GetImageFromBase64(actionButton.LabelOn.LabelBase64);
                    button.ForegroundImage = labelImage;
                }

                if (!string.IsNullOrWhiteSpace(actionButton.IconOn))
                {
                    var icon = IconManager.GetIconByString(actionButton.IconOn);
                    if (icon != null)
                    {
                        button.BackgroundImage = icon.IconImage;
                    }
                }

                button.BackColor = ProfileManager.CurrentProfile.ButtonBackground ? actionButton.BackColorOn : Color.Transparent;
            } else
            {
                if (actionButton.LabelOff != null && !string.IsNullOrWhiteSpace(actionButton.LabelOff.LabelBase64))
                {
                    Image labelImage = Utils.Base64.GetImageFromBase64(actionButton.LabelOff.LabelBase64);
                    button.ForegroundImage = labelImage;
                }

                if (!string.IsNullOrWhiteSpace(actionButton.IconOff))
                {
                    var icon = IconManager.GetIconByString(actionButton.IconOff);
                    if (icon != null)
                    {
                        button.BackgroundImage = icon.IconImage;
                    }
                }

                button.BackColor = ProfileManager.CurrentProfile.ButtonBackground ? actionButton.BackColorOff : Color.Transparent;
            }
        }


        private void LoadButton(int row, int column, int buttonSize)
        {
            var button = this.buttonPanel.Controls.Cast<RoundedButton>().Where(x => x.Row.Equals(row) && x.Column.Equals(column)).FirstOrDefault();

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

            button.ShowKeyboardHotkeyIndicator = false;
            button.KeyboardHotkeyIndicatorText = string.Empty;
            button.Width = buttonSize;
            button.Height = buttonSize;
            button.Radius = ProfileManager.CurrentProfile.ButtonRadius;
            button.Location = new Point((column * buttonSize) + (column * ProfileManager.CurrentProfile.ButtonSpacing), (row * buttonSize) + (row * ProfileManager.CurrentProfile.ButtonSpacing));
            button.BackColor = ProfileManager.CurrentProfile.ButtonBackground ? Color.FromArgb(35, 35, 35) : Color.Transparent;

            if (button.BackgroundImage != null)
            {
                button.BackgroundImage.Dispose();
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

            var actionButton = this._currentFolder.ActionButtons.Find(aB => aB.Position_X == column && aB.Position_Y == row);

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

                button.ShowKeyboardHotkeyIndicator = actionButton.KeyCode != Keys.None;
                if (button.ShowKeyboardHotkeyIndicator)
                {
                    button.KeyboardHotkeyIndicatorText = actionButton.ModifierKeyCodes.ToString().Replace("Control", "CTRL").Replace("None", string.Empty).Replace(", ", "  + ") + (!actionButton.ModifierKeyCodes.ToString().Equals("None") ? " + " : string.Empty) + actionButton.KeyCode.ToString();
                }

                this.UpdateButtonIcon(actionButton, button);
            }

            if (!this.buttonPanel.Controls.Contains(button))
            {
                this.Invoke(new Action(() => this.buttonPanel.Controls.Add(button)));
            }

        }

        private void ActionButton_IconChanged(object sender, EventArgs e)
        {
            var actionButton = sender as ActionButton.ActionButton;
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
                Task.Run(() =>
                {
                    var draggedButton = (RoundedButton)e.Data.GetData(typeof(RoundedButton));
                    var targetButton = (RoundedButton)sender;

                    var targetActionButton = ProfileManager.FindActionButton(this._currentFolder, targetButton.Row, targetButton.Column);
                    var draggedActionButton = ProfileManager.FindActionButton(this._currentFolder, draggedButton.Row, draggedButton.Column);

                    if (draggedActionButton == null) return;

                    var newActionButton = draggedActionButton;
                    newActionButton.Position_Y = targetButton.Row;
                    newActionButton.Position_X = targetButton.Column;

                    this._currentFolder.ActionButtons.Remove(draggedActionButton);
                    this._currentFolder.ActionButtons.Add(newActionButton);

                    if (targetActionButton != null)
                    {
                        var newTargetButton = targetActionButton;
                        newTargetButton.Position_Y = draggedButton.Row;
                        newTargetButton.Position_X = draggedButton.Column;
                        this._currentFolder.ActionButtons.Remove(targetActionButton);
                        this._currentFolder.ActionButtons.Add(newTargetButton);

                        foreach (PluginAction pluginAction in newTargetButton.Actions)
                        {
                            pluginAction.SetActionButton(newTargetButton);
                        }
                        foreach (PluginAction pluginAction in newTargetButton.ActionsRelease)
                        {
                            pluginAction.SetActionButton(newTargetButton);
                        }
                        foreach (PluginAction pluginAction in newTargetButton.ActionsLongPress)
                        {
                            pluginAction.SetActionButton(newTargetButton);
                        }
                        foreach (PluginAction pluginAction in newTargetButton.ActionsLongPressRelease)
                        {
                            pluginAction.SetActionButton(newTargetButton);
                        }

                        newTargetButton.EventListeners ??= new List<EventListener>();

                        foreach (var eventListener in newTargetButton.EventListeners)
                        {
                            foreach (PluginAction pluginAction in eventListener.Actions)
                            {
                                pluginAction.SetActionButton(newTargetButton);
                            }
                        }
                    }
                });
                
                ProfileManager.Save();
                this.UpdateButtons();
                MacroDeckServer.UpdateFolder(this._currentFolder);
            }
        }
        private void Button_DragEnter(object sender, DragEventArgs e)
        {
            var button = ((RoundedButton)e.Data.GetData(typeof(RoundedButton)));
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
            var button = (RoundedButton)sender;
            
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

        public void ContextMenuEditItemClick(object sender, EventArgs e)
        {
            this.OpenButtonEditor((RoundedButton)this._buttonClicked);
        }

        public void ContextMenuDeleteItemClick(object sender, EventArgs e)
        {
            int row = ((RoundedButton)this._buttonClicked).Row;
            int col = ((RoundedButton)this._buttonClicked).Column;
            var actionButton = this._currentFolder.ActionButtons.Find(aB => aB.Position_X == col && aB.Position_Y == row);
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

            var actionButton = this._currentFolder.ActionButtons.Find(aB => aB.Position_X == column && aB.Position_Y == row);
            actionButton ??= new ActionButton.ActionButton
                {
                    Actions = new List<PluginAction>(),
                    EventListeners = new List<EventListener>(),
                    Position_Y = row,
                    Position_X = column,
                    IconOff = "",
                    IconOn = "",
                    LabelOff = new ActionButton.ButtonLabel(),
                    LabelOn = new ActionButton.ButtonLabel(),
                };


            using var buttonEditor = new ButtonEditor(actionButton, this._currentFolder);
            buttonEditor.ShowDialog();
            ProfileManager.Save();
            this.UpdateButtons();

        }

        public void SetFolder(MacroDeckFolder macroDeckFolder)
        {
            if (macroDeckFolder == null || !ProfileManager.CurrentProfile.Folders.Contains(macroDeckFolder)) return;
            this._currentFolder = macroDeckFolder;
            this.UpdateButtons();
        }

        private void FoldersView_AfterSelect(object sender, TreeViewEventArgs e)
        {
            this.SetFolder(ProfileManager.FindFolderByDisplayName(foldersView.SelectedNode.Text, ProfileManager.CurrentProfile));
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
            Task.Run(() =>
            {
                this.LoadProfiles();
                this.LoadProfileSettings();
                this.UpdateButtons();
            });
            CustomControls.Form mainWindow = (CustomControls.Form)this.Parent.Parent;
            mainWindow.ResizeEnd += MainWindow_ResizeEnd;
        }

        private void LoadProfiles()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => this.LoadProfiles()));
                return;
            }
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
            var selectedFolder = ProfileManager.FindFolderByDisplayName(foldersView.SelectedNode.Text, ProfileManager.CurrentProfile);
            selectedFolder ??= ProfileManager.CurrentProfile.Folders[0];

            using var addFolder = new AddFolder(selectedFolder);
            if (addFolder.ShowDialog() == DialogResult.OK)
            {
                this.UpdateFolders();
            }
        }

        private void BtnRenameFolder_Click(object sender, EventArgs e)
        {
            var selectedFolder = ProfileManager.FindFolderByDisplayName(foldersView.SelectedNode.Text, ProfileManager.CurrentProfile);
            selectedFolder ??= ProfileManager.CurrentProfile.Folders[0];

            using var addFolder = new AddFolder(selectedFolder, true);
            if (addFolder.ShowDialog() == DialogResult.OK)
            {
                this.UpdateFolders();
            }
        }

        private void BtnDeleteFolder_Click(object sender, EventArgs e)
        {
            var selectedFolder = ProfileManager.FindFolderByDisplayName(foldersView.SelectedNode.Text, ProfileManager.CurrentProfile);
            if (selectedFolder.Equals(ProfileManager.CurrentProfile.Folders[0])) return;
            using var msgBox = new CustomControls.MessageBox();
            if (msgBox.ShowDialog(LanguageManager.Strings.AreYouSure, string.Format(LanguageManager.Strings.TheFolderWillBeDeleted, selectedFolder.DisplayName), MessageBoxButtons.YesNo) == DialogResult.Yes)
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

        private void ActionButtonContextMenuItemCopy_Click(object sender, EventArgs e)
        {
            var button = (RoundedButton)this._buttonClicked;
            int row = button.Row;
            int col = button.Column;
            var actionButton = this._currentFolder.ActionButtons.Find(aB => aB.Position_X == col && aB.Position_Y == row);
            if (actionButton != null)
            {
                Utils.Clipboard.CopyActionButton(actionButton);
            }
        }

        private void ActionButtonContextMenuOpened(object sender, EventArgs e)
        {
            var button = (RoundedButton)this._buttonClicked;
            int row = button.Row;
            int col = button.Column;
            var actionButton = this._currentFolder.ActionButtons.Find(aB => aB.Position_X == col && aB.Position_Y == row);
            this.actionButtonContextMenuItemSimulatePress.Enabled = !(actionButton == null);
            this.actionButtonContextMenuItemSimulateRelease.Enabled = !(actionButton == null);
            this.actionButtonContextMenuItemSimulateLongPress.Enabled = !(actionButton == null);
            this.actionButtonContextMenuItemSimulateLongPressRelease.Enabled = !(actionButton == null);
            this.actionButtonContextMenuItemCopy.Enabled = !(actionButton == null);
            this.actionButtonContextMenuItemPaste.Enabled = !(Utils.Clipboard.GetActionButtonCopy() == null);
            this.actionButtonContextMenuItemDelete.Enabled = !(actionButton == null);
        }

        private void ActionButtonContextMenuItemPaste_Click(object sender, EventArgs e)
        {
            var button = (RoundedButton)this._buttonClicked;
            int row = button.Row;
            int col = button.Column;
            var actionButtonOld = this._currentFolder.ActionButtons.Find(aB => aB.Position_X == col && aB.Position_Y == row);

            if (this._currentFolder != null && this._currentFolder.ActionButtons != null && actionButtonOld != null)
            {
                this._currentFolder.ActionButtons.Remove(actionButtonOld);
            }

            if (Utils.Clipboard.GetActionButtonCopy() == null) return;

            var actionButtonNew = Utils.Clipboard.GetActionButtonCopy();
            foreach (PluginAction pluginAction in actionButtonNew.Actions)
            {
                pluginAction.SetActionButton(actionButtonNew);
            }
            foreach (PluginAction pluginAction in actionButtonNew.ActionsRelease)
            {
                pluginAction.SetActionButton(actionButtonNew);
            }
            foreach (PluginAction pluginAction in actionButtonNew.ActionsLongPress)
            {
                pluginAction.SetActionButton(actionButtonNew);
            }
            foreach (PluginAction pluginAction in actionButtonNew.ActionsLongPressRelease)
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

            actionButtonNew.Position_X = col;
            actionButtonNew.Position_Y = row;

            this._currentFolder.ActionButtons.Add(actionButtonNew);

            ProfileManager.Save();
            this.UpdateButtons();
            this.UpdateButtonIcon(actionButtonNew);
            MacroDeckServer.UpdateFolder(this._currentFolder);
        }

        private void BoxProfiles_SelectedIndexChanged(object sender, EventArgs e)
        {
            var profile = ProfileManager.FindProfileByDisplayName(this.boxProfiles.Text) ?? ProfileManager.Profiles.FirstOrDefault();
            this.SetProfile(profile);
        }

        public void SetProfile(MacroDeckProfile profile)
        {
            Settings.Default.SelectedProfile = profile.ProfileId;
            Settings.Default.Save();
            ProfileManager.CurrentProfile = profile;
            this._currentFolder = profile.Folders.FirstOrDefault();
            this.UpdateButtons(true);
            this.Invoke(new Action(() => {
                this.boxProfiles.Text = profile.DisplayName;
                this.UpdateFolders();
                this.LoadProfileSettings();
            }));
        }

        private void LoadProfileSettings()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => this.LoadProfileSettings()));
                return;
            }
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

            this.buttonRows.Enabled = ProfileManager.CurrentProfile.ButtonsCustomizable;
            this.buttonColumns.Enabled = ProfileManager.CurrentProfile.ButtonsCustomizable;
            this.buttonSpacing.Enabled = ProfileManager.CurrentProfile.ButtonsCustomizable;
            this.cornerRadius.Enabled = ProfileManager.CurrentProfile.ButtonsCustomizable;
            this.checkButtonBackground.Enabled = ProfileManager.CurrentProfile.ButtonsCustomizable;
        }


        private void BtnAddProfile_Click(object sender, EventArgs e)
        {
            using var createProfileDialog = new CreateProfileDialog();
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

        private void BtnEditProfile_Click(object sender, EventArgs e)
        {
            using var createProfileDialog = new CreateProfileDialog(ProfileManager.CurrentProfile);
            if (createProfileDialog.ShowDialog() == DialogResult.OK)
            {
                this.LoadProfiles();
            }
        }

        private void BtnDeleteProfile_Click(object sender, EventArgs e)
        {
            if (ProfileManager.Profiles.Count < 2) return;
            using var msgBox = new CustomControls.MessageBox();
            if (msgBox.ShowDialog(LanguageManager.Strings.AreYouSure, string.Format(Language.LanguageManager.Strings.TheProfileWillBeDeleted, ProfileManager.CurrentProfile.DisplayName), MessageBoxButtons.YesNo) == DialogResult.Yes)
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

        private void ButtonSettingsChanged(object sender, EventArgs e)
        {
            ProfileManager.CurrentProfile.Rows = (int)buttonRows.Value;
            ProfileManager.CurrentProfile.Columns = (int)buttonColumns.Value;
            ProfileManager.CurrentProfile.ButtonSpacing = (int)buttonSpacing.Value;
            ProfileManager.CurrentProfile.ButtonRadius = (int)cornerRadius.Value;
            ProfileManager.CurrentProfile.ButtonBackground = checkButtonBackground.Checked;
            ProfileManager.Save();
            MacroDeckLogger.Info(GetType(), string.Format("Updated profile settings of {0}:", ProfileManager.CurrentProfile.DisplayName) + Environment.NewLine +
                                            string.Format("Rows: {0}", ProfileManager.CurrentProfile.Rows) + Environment.NewLine +
                                            string.Format("Columns: {0}", ProfileManager.CurrentProfile.Columns) + Environment.NewLine +
                                            string.Format("ButtonSpacing: {0}", ProfileManager.CurrentProfile.ButtonSpacing) + Environment.NewLine +
                                            string.Format("ButtonRadius: {0}", ProfileManager.CurrentProfile.ButtonRadius) + Environment.NewLine +
                                            string.Format("ButtonBackground: {0}", ProfileManager.CurrentProfile.ButtonBackground));
            this.UpdateButtons(true);
            foreach (MacroDeckClient macroDeckClient in MacroDeckServer.Clients.FindAll(macroDeckClient => macroDeckClient.Profile.ProfileId.Equals(ProfileManager.CurrentProfile.ProfileId)))
            {
                MacroDeckServer.SetProfile(macroDeckClient, ProfileManager.CurrentProfile);
            }
        }

        private void ActionButtonContextMenuItemSimulatePress_Click(object sender, EventArgs e)
        {
            SimulatePress(ButtonPressType.SHORT);
        }

        private void ActionButtonContextMenuItemSimulateRelease_Click(object sender, EventArgs e)
        {
            SimulatePress(ButtonPressType.SHORT_RELEASE);
        }

        private void ActionButtonContextMenuItemSimulateLongPress_Click(object sender, EventArgs e)
        {
            SimulatePress(ButtonPressType.LONG);
        }

        private void ActionButtonContextMenuItemSimulateLongPressRelease_Click(object sender, EventArgs e)
        {
            SimulatePress(ButtonPressType.LONG_RELEASE);
        }

        private void SimulatePress(ButtonPressType buttonPressType)
        {
            int row = ((RoundedButton)this._buttonClicked).Row;
            int column = ((RoundedButton)this._buttonClicked).Column;
            var actionButton = this._currentFolder.ActionButtons.Find(aB => aB.Position_X == column && aB.Position_Y == row);
            MacroDeckServer.Execute(actionButton, "", buttonPressType);

        }
    }
}
