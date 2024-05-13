using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.ActionButton;
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
using SuchByte.MacroDeck.Services;
using SuchByte.MacroDeck.Utils;
using Clipboard = SuchByte.MacroDeck.Utils.Clipboard;
using Form = SuchByte.MacroDeck.GUI.CustomControls.Form;
using MessageBox = SuchByte.MacroDeck.GUI.CustomControls.MessageBox;

namespace SuchByte.MacroDeck.GUI.MainWindowContents;

public partial class DeckView : UserControl
{
    private MacroDeckFolder _currentFolder;

    private Control _buttonClicked;

    public MacroDeckFolder CurrentFolder => _currentFolder;

    public DeckView()
    {
        InitializeComponent();
        Dock = DockStyle.Fill;
        UpdateTranslation();
        _currentFolder = ProfileManager.CurrentProfile.Folders.FirstOrDefault();
        qrCodeBox.BackgroundImage = QrCodeService.Instance.GetQuickSetupQrCode();
    }

    private void MainWindow_ResizeEnd(object sender, EventArgs e)
    {
        UpdateButtons();
    }

    public void UpdateTranslation()
    {
        Name = LanguageManager.Strings.DeckTitle;
        lblColumns.Text = LanguageManager.Strings.Columns;
        lblRows.Text = LanguageManager.Strings.Rows;
        lblSpacing.Text = LanguageManager.Strings.Spacing;
        lblCornerRadius.Text = LanguageManager.Strings.CornerRadius;
        checkButtonBackground.Text = LanguageManager.Strings.ButtonBackGround;
        foldersContextMenuNew.Text = LanguageManager.Strings.Create;
        foldersContextMenuEdit.Text = LanguageManager.Strings.Edit;
        foldersContextMenuDelete.Text = LanguageManager.Strings.Delete;
        actionButtonContextMenuItemEdit.Text = LanguageManager.Strings.Edit;
        actionButtonContextMenuItemCopy.Text = LanguageManager.Strings.Copy;
        actionButtonContextMenuItemPaste.Text = LanguageManager.Strings.Paste;
        actionButtonContextMenuItemDelete.Text = LanguageManager.Strings.Delete;
        actionButtonContextMenuItemSimulatePress.Text = LanguageManager.Strings.SimulateOnPress;
        actionButtonContextMenuItemSimulateRelease.Text = LanguageManager.Strings.SimulateOnRelease;
        actionButtonContextMenuItemSimulateLongPress.Text = LanguageManager.Strings.SimulateOnLongPress;
        actionButtonContextMenuItemSimulateLongPressRelease.Text = LanguageManager.Strings.SimulateOnLongPressRelease;
        lblFolders.Text = LanguageManager.Strings.Folders;
        lblGrid.Text = LanguageManager.Strings.Grid;
    }


    public void UpdateFolders()
    {
        foldersView.Nodes.Clear();

        var stack = new Stack<TreeNode>();
        var rootDirectory = ProfileManager.CurrentProfile.Folders.FirstOrDefault();
        var node = new TreeNode(rootDirectory.DisplayName) { Tag = rootDirectory };
        stack.Push(node);

        while (stack.Count > 0)
        {
            var currentNode = stack.Pop();
            var directoryInfo = (MacroDeckFolder)currentNode.Tag;
            foreach (var directoryId in directoryInfo.Childs.ToList())
            {
                try
                {
                    var directory = ProfileManager.FindFolderById(directoryId, ProfileManager.CurrentProfile);
                    var childDirectoryNode = new TreeNode(directory.DisplayName) { Tag = directory };
                    Invoke(new Action(() => currentNode.Nodes.Add(childDirectoryNode)));
                    stack.Push(childDirectoryNode);
                } catch { }
            }
        }
        foldersView.Nodes.Add(node);
        foldersView.ExpandAll();
    }


    public void UpdateButtons(bool clear = false)
    {
        if (clear)
        {
            foreach (RoundedButton roundedButton in buttonPanel.Controls)
            {
                var actionButton = _currentFolder.ActionButtons.Find(aB => aB.Position_X == roundedButton.Column && aB.Position_Y == roundedButton.Row);
                if (actionButton != null)
                {
                    actionButton.StateChanged -= ButtonStateChanged;
                    actionButton.IconChanged -= ActionButton_IconChanged;
                    actionButton.LabelOff.LabelBase64Changed -= LabelChanged;
                    actionButton.LabelOn.LabelBase64Changed -= LabelChanged;
                }

                roundedButton.MouseDown -= ActionButton_Down;
                roundedButton.DragDrop -= Button_DragDrop;
                roundedButton.DragEnter -= Button_DragEnter;
                Invoke(() => roundedButton.Dispose());

            }
            Invoke(() => buttonPanel.Controls.Clear());
        }

        var buttonSize = 100;
        int rows = ProfileManager.CurrentProfile.Rows, columns = ProfileManager.CurrentProfile.Columns, spacing = ProfileManager.CurrentProfile.ButtonSpacing; // from settings
        int width = buttonPanel.Width, height = buttonPanel.Height; // from panel
        int buttonSizeX, buttonSizeY;

        buttonSizeX = width / columns; //calc with spacing, remove it after
        buttonSizeY = height / rows;
        buttonSize = Math.Min(buttonSizeX, buttonSizeY) - spacing;


        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                LoadButton(row, column, buttonSize);
            }
        }
    }

    private void LabelChanged(object sender, EventArgs e)
    {
        var actionButton = _currentFolder.ActionButtons.Find(aB => (aB.LabelOff != null && aB.LabelOff.Equals(sender)) || (aB.LabelOn != null && aB.LabelOn.Equals(sender)));
        UpdateButtonIcon(actionButton);
    }

    private void ButtonStateChanged(object sender, EventArgs e)
    {
        var actionButton = (ActionButton.ActionButton)sender;
        UpdateButtonIcon(actionButton);
    }

    private void UpdateButtonIcon(ActionButton.ActionButton actionButton, RoundedButton button = null)
    {
        if (IsDisposed || !IsHandleCreated || actionButton == null) return;
        if (InvokeRequired)
        {
            Invoke(() => UpdateButtonIcon(actionButton, button));
            return;
        }

        button ??= buttonPanel.Controls.Cast<RoundedButton>().Where(x => x.Row.Equals(actionButton.Position_Y) && x.Column.Equals(actionButton.Position_X)).FirstOrDefault();

        if (actionButton.State)
        {
            if (actionButton.LabelOn != null && !string.IsNullOrWhiteSpace(actionButton.LabelOn.LabelBase64))
            {
                var labelImage = Base64.GetImageFromBase64(actionButton.LabelOn.LabelBase64);
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
                var labelImage = Base64.GetImageFromBase64(actionButton.LabelOff.LabelBase64);
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
        var button = buttonPanel.Controls.Cast<RoundedButton>().Where(x => x.Row.Equals(row) && x.Column.Equals(column)).FirstOrDefault();

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
            button.MouseDown += ActionButton_Down;
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

        var actionButton = _currentFolder.ActionButtons.Find(aB => aB.Position_X == column && aB.Position_Y == row);

        if (actionButton != null)
        {
            // Remove event handlers if there are any to prevent multiple event handlers
            actionButton.StateChanged -= ButtonStateChanged;
            actionButton.LabelOff.LabelBase64Changed -= LabelChanged;
            actionButton.LabelOn.LabelBase64Changed -= LabelChanged;
            actionButton.IconChanged -= ActionButton_IconChanged;
            // Add event handlers
            actionButton.StateChanged += ButtonStateChanged;
            actionButton.LabelOff.LabelBase64Changed += LabelChanged;
            actionButton.LabelOn.LabelBase64Changed += LabelChanged;
            actionButton.IconChanged += ActionButton_IconChanged;

            button.ShowKeyboardHotkeyIndicator = actionButton.KeyCode != Keys.None;
            if (button.ShowKeyboardHotkeyIndicator)
            {
                button.KeyboardHotkeyIndicatorText = actionButton.ModifierKeyCodes.ToString().Replace("Control", "CTRL").Replace("None", string.Empty).Replace(", ", "  + ") + (!actionButton.ModifierKeyCodes.ToString().Equals("None") ? " + " : string.Empty) + actionButton.KeyCode;
            }

            UpdateButtonIcon(actionButton, button);
        }

        if (!buttonPanel.Controls.Contains(button))
        {
            Invoke(() => buttonPanel.Controls.Add(button));
        }

    }

    private void ActionButton_IconChanged(object sender, EventArgs e)
    {
        var actionButton = sender as ActionButton.ActionButton;
        if (actionButton != null)
        {
            if (_currentFolder.ActionButtons.Contains(actionButton))
            {
                UpdateButtonIcon(actionButton);
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

                var targetActionButton = ProfileManager.FindActionButton(_currentFolder, targetButton.Row, targetButton.Column);
                var draggedActionButton = ProfileManager.FindActionButton(_currentFolder, draggedButton.Row, draggedButton.Column);

                if (draggedActionButton == null) return;

                var newActionButton = draggedActionButton;
                newActionButton.Position_Y = targetButton.Row;
                newActionButton.Position_X = targetButton.Column;

                _currentFolder.ActionButtons.Remove(draggedActionButton);
                _currentFolder.ActionButtons.Add(newActionButton);

                if (targetActionButton != null)
                {
                    var newTargetButton = targetActionButton;
                    newTargetButton.Position_Y = draggedButton.Row;
                    newTargetButton.Position_X = draggedButton.Column;
                    _currentFolder.ActionButtons.Remove(targetActionButton);
                    _currentFolder.ActionButtons.Add(newTargetButton);

                    foreach (var pluginAction in newTargetButton.Actions)
                    {
                        pluginAction.SetActionButton(newTargetButton);
                    }
                    foreach (var pluginAction in newTargetButton.ActionsRelease)
                    {
                        pluginAction.SetActionButton(newTargetButton);
                    }
                    foreach (var pluginAction in newTargetButton.ActionsLongPress)
                    {
                        pluginAction.SetActionButton(newTargetButton);
                    }
                    foreach (var pluginAction in newTargetButton.ActionsLongPressRelease)
                    {
                        pluginAction.SetActionButton(newTargetButton);
                    }

                    newTargetButton.EventListeners ??= new List<EventListener>();

                    foreach (var eventListener in newTargetButton.EventListeners)
                    {
                        foreach (var pluginAction in eventListener.Actions)
                        {
                            pluginAction.SetActionButton(newTargetButton);
                        }
                    }
                }
            });
                
            ProfileManager.Save();
            UpdateButtons();
            MacroDeckServer.UpdateFolder(_currentFolder);
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
            
        _buttonClicked = button;
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
                actionButtonContextMenu.Show(button, button.PointToClient(MousePosition));
                break;
        }
    }

    public void ContextMenuEditItemClick(object sender, EventArgs e)
    {
        OpenButtonEditor((RoundedButton)_buttonClicked);
    }

    public void ContextMenuDeleteItemClick(object sender, EventArgs e)
    {
        var row = ((RoundedButton)_buttonClicked).Row;
        var col = ((RoundedButton)_buttonClicked).Column;
        var actionButton = _currentFolder.ActionButtons.Find(aB => aB.Position_X == col && aB.Position_Y == row);
        if (actionButton != null)
        {
            actionButton.Dispose();
            _currentFolder.ActionButtons.Remove(actionButton);
            ProfileManager.Save();
            UpdateButtons();
            MacroDeckServer.UpdateFolder(_currentFolder);
        }
    }

    public void OpenButtonEditor(RoundedButton button)
    {
        var row = button.Row;
        var column = button.Column;

        var actionButton = _currentFolder.ActionButtons.Find(aB => aB.Position_X == column && aB.Position_Y == row);
        actionButton ??= new ActionButton.ActionButton
        {
            Actions = new List<PluginAction>(),
            EventListeners = new List<EventListener>(),
            Position_Y = row,
            Position_X = column,
            IconOff = "",
            IconOn = "",
            LabelOff = new ButtonLabel(),
            LabelOn = new ButtonLabel(),
        };


        using var buttonEditor = new ButtonEditor(actionButton, _currentFolder);
        buttonEditor.ShowDialog();
        ProfileManager.Save();
        UpdateButtons();

    }

    public void SetFolder(MacroDeckFolder macroDeckFolder)
    {
        if (macroDeckFolder == null || !ProfileManager.CurrentProfile.Folders.Contains(macroDeckFolder)) return;
        _currentFolder = macroDeckFolder;
        UpdateButtons();
    }

    private void FoldersView_AfterSelect(object sender, TreeViewEventArgs e)
    {
        SetFolder(ProfileManager.FindFolderByDisplayName(foldersView.SelectedNode.Text, ProfileManager.CurrentProfile));
    }

    private void FoldersView_MouseDown(object sender, MouseEventArgs e)
    {

        switch (e.Button)
        {
            case MouseButtons.Right:
                foldersView.SelectedNode = foldersView.GetNodeAt(e.X, e.Y);
                if (foldersView.SelectedNode == null) foldersView.SelectedNode = foldersView.Nodes[0];
                foldersContextMenu.Show(foldersView, foldersView.PointToClient(MousePosition));
                break;
        }
    }

    private void Deck_Load(object sender, EventArgs e)
    {
        Task.Run(() =>
        {
            LoadProfiles();
            LoadProfileSettings();
            UpdateButtons();
        });
        var mainWindow = (Form)Parent.Parent;
        mainWindow.ResizeEnd += MainWindow_ResizeEnd;
    }

    private void LoadProfiles()
    {
        if (InvokeRequired)
        {
            Invoke(() => LoadProfiles());
            return;
        }
        boxProfiles.Items.Clear();
        foreach (var macroDeckProfile in ProfileManager.Profiles)
        {
            boxProfiles.Items.Add(macroDeckProfile.DisplayName);
        }
        boxProfiles.Text = ProfileManager.CurrentProfile.DisplayName;
    }

    private void BtnGoToRoot_Click(object sender, EventArgs e)
    {
        foldersView.SelectedNode = foldersView.Nodes[0];
    }

    private void BtnCreateFolder_Click(object sender, EventArgs e)
    {
        var selectedFolder = ProfileManager.FindFolderByDisplayName(foldersView.SelectedNode.Text, ProfileManager.CurrentProfile);
        selectedFolder ??= ProfileManager.CurrentProfile.Folders[0];

        using var addFolder = new AddFolder(selectedFolder);
        if (addFolder.ShowDialog() == DialogResult.OK)
        {
            UpdateFolders();
        }
    }

    private void BtnRenameFolder_Click(object sender, EventArgs e)
    {
        var selectedFolder = ProfileManager.FindFolderByDisplayName(foldersView.SelectedNode.Text, ProfileManager.CurrentProfile);
        selectedFolder ??= ProfileManager.CurrentProfile.Folders[0];

        using var addFolder = new AddFolder(selectedFolder, true);
        if (addFolder.ShowDialog() == DialogResult.OK)
        {
            UpdateFolders();
        }
    }

    private void BtnDeleteFolder_Click(object sender, EventArgs e)
    {
        var selectedFolder = ProfileManager.FindFolderByDisplayName(foldersView.SelectedNode.Text, ProfileManager.CurrentProfile);
        if (selectedFolder.Equals(ProfileManager.CurrentProfile.Folders[0])) return;
        using var msgBox = new MessageBox();
        if (msgBox.ShowDialog(LanguageManager.Strings.AreYouSure, string.Format(LanguageManager.Strings.TheFolderWillBeDeleted, selectedFolder.DisplayName), MessageBoxButtons.YesNo) == DialogResult.Yes)
        {
            if (_currentFolder == selectedFolder)
            {
                _currentFolder = ProfileManager.CurrentProfile.Folders[0];
                UpdateButtons();
            }

            ProfileManager.DeleteFolder(selectedFolder, ProfileManager.CurrentProfile);
            UpdateFolders();
        }
    }

    private void ActionButtonContextMenuItemCopy_Click(object sender, EventArgs e)
    {
        var button = (RoundedButton)_buttonClicked;
        var row = button.Row;
        var col = button.Column;
        var actionButton = _currentFolder.ActionButtons.Find(aB => aB.Position_X == col && aB.Position_Y == row);
        if (actionButton != null)
        {
            Clipboard.CopyActionButton(actionButton);
        }
    }

    private void ActionButtonContextMenuOpened(object sender, EventArgs e)
    {
        var button = (RoundedButton)_buttonClicked;
        var row = button.Row;
        var col = button.Column;
        var actionButton = _currentFolder.ActionButtons.Find(aB => aB.Position_X == col && aB.Position_Y == row);
        actionButtonContextMenuItemSimulatePress.Enabled = !(actionButton == null);
        actionButtonContextMenuItemSimulateRelease.Enabled = !(actionButton == null);
        actionButtonContextMenuItemSimulateLongPress.Enabled = !(actionButton == null);
        actionButtonContextMenuItemSimulateLongPressRelease.Enabled = !(actionButton == null);
        actionButtonContextMenuItemCopy.Enabled = !(actionButton == null);
        actionButtonContextMenuItemPaste.Enabled = !(Clipboard.GetActionButtonCopy() == null);
        actionButtonContextMenuItemDelete.Enabled = !(actionButton == null);
    }

    private void ActionButtonContextMenuItemPaste_Click(object sender, EventArgs e)
    {
        var button = (RoundedButton)_buttonClicked;
        var row = button.Row;
        var col = button.Column;
        var actionButtonOld = _currentFolder.ActionButtons.Find(aB => aB.Position_X == col && aB.Position_Y == row);

        if (_currentFolder != null && _currentFolder.ActionButtons != null && actionButtonOld != null)
        {
            _currentFolder.ActionButtons.Remove(actionButtonOld);
        }

        if (Clipboard.GetActionButtonCopy() == null) return;

        var actionButtonNew = Clipboard.GetActionButtonCopy();
        foreach (var pluginAction in actionButtonNew.Actions)
        {
            pluginAction.SetActionButton(actionButtonNew);
        }
        foreach (var pluginAction in actionButtonNew.ActionsRelease)
        {
            pluginAction.SetActionButton(actionButtonNew);
        }
        foreach (var pluginAction in actionButtonNew.ActionsLongPress)
        {
            pluginAction.SetActionButton(actionButtonNew);
        }
        foreach (var pluginAction in actionButtonNew.ActionsLongPressRelease)
        {
            pluginAction.SetActionButton(actionButtonNew);
        }

        foreach (var eventListener in actionButtonNew.EventListeners)
        {
            foreach (var pluginAction in eventListener.Actions)
            {
                pluginAction.SetActionButton(actionButtonNew);
            }
        }

        actionButtonNew.Position_X = col;
        actionButtonNew.Position_Y = row;

        _currentFolder.ActionButtons.Add(actionButtonNew);

        ProfileManager.Save();
        UpdateButtons();
        UpdateButtonIcon(actionButtonNew);
        MacroDeckServer.UpdateFolder(_currentFolder);
    }

    private void BoxProfiles_SelectedIndexChanged(object sender, EventArgs e)
    {
        var profile = ProfileManager.FindProfileByDisplayName(boxProfiles.Text) ?? ProfileManager.Profiles.FirstOrDefault();
        SetProfile(profile);
    }

    public void SetProfile(MacroDeckProfile profile)
    {
        Settings.Default.SelectedProfile = profile.ProfileId;
        Settings.Default.Save();
        ProfileManager.CurrentProfile = profile;
        _currentFolder = profile.Folders.FirstOrDefault();
        UpdateButtons(true);
        Invoke(() => {
            boxProfiles.Text = profile.DisplayName;
            UpdateFolders();
            LoadProfileSettings();
        });
    }

    private void LoadProfileSettings()
    {
        if (InvokeRequired)
        {
            Invoke(() => LoadProfileSettings());
            return;
        }
        buttonRows.ValueChanged -= ButtonSettingsChanged;
        buttonColumns.ValueChanged -= ButtonSettingsChanged;
        buttonSpacing.ValueChanged -= ButtonSettingsChanged;
        cornerRadius.ValueChanged -= ButtonSettingsChanged;
        checkButtonBackground.CheckedChanged -= ButtonSettingsChanged;

        buttonRows.Value = ProfileManager.CurrentProfile.Rows;
        buttonColumns.Value = ProfileManager.CurrentProfile.Columns;
        buttonSpacing.Value = ProfileManager.CurrentProfile.ButtonSpacing;
        cornerRadius.Value = ProfileManager.CurrentProfile.ButtonRadius;
        checkButtonBackground.Checked = ProfileManager.CurrentProfile.ButtonBackground;
        buttonRows.ValueChanged += ButtonSettingsChanged;
        buttonColumns.ValueChanged += ButtonSettingsChanged;
        buttonSpacing.ValueChanged += ButtonSettingsChanged;
        cornerRadius.ValueChanged += ButtonSettingsChanged;
        checkButtonBackground.CheckedChanged += ButtonSettingsChanged;

        buttonRows.Enabled = ProfileManager.CurrentProfile.ButtonsCustomizable;
        buttonColumns.Enabled = ProfileManager.CurrentProfile.ButtonsCustomizable;
        buttonSpacing.Enabled = ProfileManager.CurrentProfile.ButtonsCustomizable;
        cornerRadius.Enabled = ProfileManager.CurrentProfile.ButtonsCustomizable;
        checkButtonBackground.Enabled = ProfileManager.CurrentProfile.ButtonsCustomizable;
    }


    private void BtnAddProfile_Click(object sender, EventArgs e)
    {
        using var createProfileDialog = new CreateProfileDialog();
        if (createProfileDialog.ShowDialog() == DialogResult.OK)
        {
            ProfileManager.CurrentProfile = createProfileDialog.Profile;
            LoadProfiles();
            _currentFolder = ProfileManager.CurrentProfile.Folders[0];
            UpdateButtons(true);
            LoadProfileSettings();
            UpdateFolders();
        }
    }

    private void BtnEditProfile_Click(object sender, EventArgs e)
    {
        using var createProfileDialog = new CreateProfileDialog(ProfileManager.CurrentProfile);
        if (createProfileDialog.ShowDialog() == DialogResult.OK)
        {
            LoadProfiles();
        }
    }

    private void BtnDeleteProfile_Click(object sender, EventArgs e)
    {
        if (ProfileManager.Profiles.Count < 2) return;
        using var msgBox = new MessageBox();
        if (msgBox.ShowDialog(LanguageManager.Strings.AreYouSure, string.Format(LanguageManager.Strings.TheProfileWillBeDeleted, ProfileManager.CurrentProfile.DisplayName), MessageBoxButtons.YesNo) == DialogResult.Yes)
        {
            ProfileManager.DeleteProfile(ProfileManager.CurrentProfile);
            ProfileManager.CurrentProfile = ProfileManager.Profiles[0];
            LoadProfiles();
            _currentFolder = ProfileManager.CurrentProfile.Folders[0];
            UpdateButtons(true);
            LoadProfileSettings();
            UpdateFolders();
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
        UpdateButtons(true);
        foreach (var macroDeckClient in MacroDeckServer.Clients.FindAll(macroDeckClient => macroDeckClient.Profile.ProfileId.Equals(ProfileManager.CurrentProfile.ProfileId)))
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
        var row = ((RoundedButton)_buttonClicked).Row;
        var column = ((RoundedButton)_buttonClicked).Column;
        var actionButton = _currentFolder.ActionButtons.Find(aB => aB.Position_X == column && aB.Position_Y == row);
        MacroDeckServer.Execute(actionButton, "", buttonPressType);

    }
}