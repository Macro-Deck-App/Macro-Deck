
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Properties;

namespace SuchByte.MacroDeck.GUI.MainWindowContents
{
    partial class DeckView
    {
        /// <summary> 
        /// Erforderliche Designervariable.
        /// </summary>
        private IContainer components = null;

        /// <summary> 
        /// Verwendete Ressourcen bereinigen.
        /// </summary>
        /// <param name="disposing">True, wenn verwaltete Ressourcen gelöscht werden sollen; andernfalls False.</param>
        protected override void Dispose(bool disposing)
        {
            try
            {
                foreach (RoundedButton roundedButton in this.buttonPanel.Controls)
                {
                    ActionButton.ActionButton actionButton = this._currentFolder.ActionButtons.Find(aB => aB.Position_X == roundedButton.Column && aB.Position_Y == roundedButton.Row);
                    if (actionButton != null)
                    {
                        actionButton.StateChanged -= this.ButtonStateChanged;
                        actionButton.LabelOff.LabelBase64Changed -= this.LabelChanged;
                        actionButton.LabelOn.LabelBase64Changed -= this.LabelChanged;
                    }
                    roundedButton.MouseDown -= this.ActionButton_Down;
                    roundedButton.DragDrop -= Button_DragDrop;
                    roundedButton.DragEnter -= Button_DragEnter;
                    this.Invoke(new Action(() => roundedButton.Dispose()));

                }
            } catch { }
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Vom Komponenten-Designer generierter Code

        /// <summary> 
        /// Erforderliche Methode für die Designerunterstützung. 
        /// Der Inhalt der Methode darf nicht mit dem Code-Editor geändert werden.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new Container();
            this.foldersView = new TreeView();
            this.buttonPanel = new BufferedPanel();
            this.actionButtonContextMenuItemEdit = new ToolStripMenuItem();
            this.actionButtonContextMenuItemDelete = new ToolStripMenuItem();
            this.foldersContextMenuNew = new ToolStripMenuItem();
            this.foldersContextMenuEdit = new ToolStripMenuItem();
            this.foldersContextMenuDelete = new ToolStripMenuItem();
            this.foldersContextMenu = new ContextMenuStrip(this.components);
            this.actionButtonContextMenu = new ContextMenuStrip(this.components);
            this.toolStripSeparator2 = new ToolStripSeparator();
            this.actionButtonContextMenuItemSimulatePress = new ToolStripMenuItem();
            this.actionButtonContextMenuItemSimulateRelease = new ToolStripMenuItem();
            this.actionButtonContextMenuItemSimulateLongPress = new ToolStripMenuItem();
            this.actionButtonContextMenuItemSimulateLongPressRelease = new ToolStripMenuItem();
            this.toolStripSeparator3 = new ToolStripSeparator();
            this.actionButtonContextMenuItemCopy = new ToolStripMenuItem();
            this.actionButtonContextMenuItemPaste = new ToolStripMenuItem();
            this.toolStripSeparator1 = new ToolStripSeparator();
            this.boxProfiles = new RoundedComboBox();
            this.btnAddProfile = new PictureButton();
            this.btnDeleteProfile = new PictureButton();
            this.buttonColumns = new NumericUpDown();
            this.buttonRows = new NumericUpDown();
            this.lblColumns = new Label();
            this.lblRows = new Label();
            this.lblSpacing = new Label();
            this.buttonSpacing = new NumericUpDown();
            this.lblCornerRadius = new Label();
            this.cornerRadius = new NumericUpDown();
            this.checkButtonBackground = new CheckBox();
            this.btnEditProfile = new PictureButton();
            this.panel1 = new Panel();
            this.lblFolders = new Label();
            this.lblGrid = new Label();
            this.roundedPanel1 = new RoundedPanel();
            this.roundedPanel2 = new RoundedPanel();
            this.label1 = new Label();
            this.foldersContextMenu.SuspendLayout();
            this.actionButtonContextMenu.SuspendLayout();
            ((ISupportInitialize)(this.btnAddProfile)).BeginInit();
            ((ISupportInitialize)(this.btnDeleteProfile)).BeginInit();
            ((ISupportInitialize)(this.buttonColumns)).BeginInit();
            ((ISupportInitialize)(this.buttonRows)).BeginInit();
            ((ISupportInitialize)(this.buttonSpacing)).BeginInit();
            ((ISupportInitialize)(this.cornerRadius)).BeginInit();
            ((ISupportInitialize)(this.btnEditProfile)).BeginInit();
            this.panel1.SuspendLayout();
            this.roundedPanel1.SuspendLayout();
            this.roundedPanel2.SuspendLayout();
            this.SuspendLayout();
            // 
            // foldersView
            // 
            this.foldersView.Anchor = ((AnchorStyles)(((AnchorStyles.Top | AnchorStyles.Bottom) 
                                                       | AnchorStyles.Right)));
            this.foldersView.BackColor = Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.foldersView.BorderStyle = BorderStyle.None;
            this.foldersView.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.foldersView.ForeColor = Color.White;
            this.foldersView.FullRowSelect = true;
            this.foldersView.ItemHeight = 26;
            this.foldersView.LineColor = Color.White;
            this.foldersView.Location = new Point(854, 45);
            this.foldersView.Name = "foldersView";
            this.foldersView.PathSeparator = "/";
            this.foldersView.Size = new Size(272, 339);
            this.foldersView.TabIndex = 6;
            this.foldersView.AfterSelect += new TreeViewEventHandler(this.FoldersView_AfterSelect);
            this.foldersView.MouseDown += new MouseEventHandler(this.FoldersView_MouseDown);
            // 
            // buttonPanel
            // 
            this.buttonPanel.Anchor = ((AnchorStyles)((((AnchorStyles.Top | AnchorStyles.Bottom) 
                                                        | AnchorStyles.Left) 
                                                       | AnchorStyles.Right)));
            this.buttonPanel.Location = new Point(6, 45);
            this.buttonPanel.Name = "buttonPanel";
            this.buttonPanel.Size = new Size(841, 500);
            this.buttonPanel.TabIndex = 5;
            // 
            // actionButtonContextMenuItemEdit
            // 
            this.actionButtonContextMenuItemEdit.ForeColor = Color.White;
            this.actionButtonContextMenuItemEdit.Name = "actionButtonContextMenuItemEdit";
            this.actionButtonContextMenuItemEdit.Size = new Size(330, 28);
            this.actionButtonContextMenuItemEdit.Text = "Edit";
            this.actionButtonContextMenuItemEdit.Click += new EventHandler(this.ContextMenuEditItemClick);
            // 
            // actionButtonContextMenuItemDelete
            // 
            this.actionButtonContextMenuItemDelete.ForeColor = Color.White;
            this.actionButtonContextMenuItemDelete.Name = "actionButtonContextMenuItemDelete";
            this.actionButtonContextMenuItemDelete.Size = new Size(330, 28);
            this.actionButtonContextMenuItemDelete.Text = "Delete";
            this.actionButtonContextMenuItemDelete.Click += new EventHandler(this.ContextMenuDeleteItemClick);
            // 
            // foldersContextMenuNew
            // 
            this.foldersContextMenuNew.ForeColor = Color.White;
            this.foldersContextMenuNew.Name = "foldersContextMenuNew";
            this.foldersContextMenuNew.Size = new Size(146, 28);
            this.foldersContextMenuNew.Text = "New folder";
            this.foldersContextMenuNew.Click += new EventHandler(this.BtnCreateFolder_Click);
            // 
            // foldersContextMenuEdit
            // 
            this.foldersContextMenuEdit.ForeColor = Color.White;
            this.foldersContextMenuEdit.Name = "foldersContextMenuEdit";
            this.foldersContextMenuEdit.Size = new Size(146, 28);
            this.foldersContextMenuEdit.Text = "Edit";
            this.foldersContextMenuEdit.Click += new EventHandler(this.BtnRenameFolder_Click);
            // 
            // foldersContextMenuDelete
            // 
            this.foldersContextMenuDelete.ForeColor = Color.White;
            this.foldersContextMenuDelete.Name = "foldersContextMenuDelete";
            this.foldersContextMenuDelete.Size = new Size(146, 28);
            this.foldersContextMenuDelete.Text = "Delete";
            this.foldersContextMenuDelete.Click += new EventHandler(this.BtnDeleteFolder_Click);
            // 
            // foldersContextMenu
            // 
            this.foldersContextMenu.BackColor = Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.foldersContextMenu.Font = new Font("Tahoma", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.foldersContextMenu.Items.AddRange(new ToolStripItem[] {
            this.foldersContextMenuNew,
            this.foldersContextMenuEdit,
            this.foldersContextMenuDelete});
            this.foldersContextMenu.Name = "foldersContextMenu";
            this.foldersContextMenu.ShowImageMargin = false;
            this.foldersContextMenu.Size = new Size(147, 88);
            // 
            // actionButtonContextMenu
            // 
            this.actionButtonContextMenu.BackColor = Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.actionButtonContextMenu.Font = new Font("Tahoma", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.actionButtonContextMenu.Items.AddRange(new ToolStripItem[] {
            this.actionButtonContextMenuItemEdit,
            this.toolStripSeparator2,
            this.actionButtonContextMenuItemSimulatePress,
            this.actionButtonContextMenuItemSimulateRelease,
            this.actionButtonContextMenuItemSimulateLongPress,
            this.actionButtonContextMenuItemSimulateLongPressRelease,
            this.toolStripSeparator3,
            this.actionButtonContextMenuItemCopy,
            this.actionButtonContextMenuItemPaste,
            this.toolStripSeparator1,
            this.actionButtonContextMenuItemDelete});
            this.actionButtonContextMenu.Name = "actionButtonContextMenu";
            this.actionButtonContextMenu.ShowImageMargin = false;
            this.actionButtonContextMenu.Size = new Size(331, 246);
            this.actionButtonContextMenu.Opening += new CancelEventHandler(this.ActionButtonContextMenuOpened);
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new Size(327, 6);
            // 
            // actionButtonContextMenuItemSimulatePress
            // 
            this.actionButtonContextMenuItemSimulatePress.ForeColor = Color.White;
            this.actionButtonContextMenuItemSimulatePress.Name = "actionButtonContextMenuItemSimulatePress";
            this.actionButtonContextMenuItemSimulatePress.Size = new Size(330, 28);
            this.actionButtonContextMenuItemSimulatePress.Text = "Simulate \"On press\"";
            this.actionButtonContextMenuItemSimulatePress.Click += new EventHandler(this.ActionButtonContextMenuItemSimulatePress_Click);
            // 
            // actionButtonContextMenuItemSimulateRelease
            // 
            this.actionButtonContextMenuItemSimulateRelease.ForeColor = Color.White;
            this.actionButtonContextMenuItemSimulateRelease.Name = "actionButtonContextMenuItemSimulateRelease";
            this.actionButtonContextMenuItemSimulateRelease.Size = new Size(330, 28);
            this.actionButtonContextMenuItemSimulateRelease.Text = "Simulate \"On release\"";
            this.actionButtonContextMenuItemSimulateRelease.Click += new EventHandler(this.ActionButtonContextMenuItemSimulateRelease_Click);
            // 
            // actionButtonContextMenuItemSimulateLongPress
            // 
            this.actionButtonContextMenuItemSimulateLongPress.ForeColor = Color.White;
            this.actionButtonContextMenuItemSimulateLongPress.Name = "actionButtonContextMenuItemSimulateLongPress";
            this.actionButtonContextMenuItemSimulateLongPress.Size = new Size(330, 28);
            this.actionButtonContextMenuItemSimulateLongPress.Text = "Simulate \"On long press\"";
            this.actionButtonContextMenuItemSimulateLongPress.Click += new EventHandler(this.ActionButtonContextMenuItemSimulateLongPress_Click);
            // 
            // actionButtonContextMenuItemSimulateLongPressRelease
            // 
            this.actionButtonContextMenuItemSimulateLongPressRelease.ForeColor = Color.White;
            this.actionButtonContextMenuItemSimulateLongPressRelease.Name = "actionButtonContextMenuItemSimulateLongPressRelease";
            this.actionButtonContextMenuItemSimulateLongPressRelease.Size = new Size(330, 28);
            this.actionButtonContextMenuItemSimulateLongPressRelease.Text = "Simulate \"On long press release\"";
            this.actionButtonContextMenuItemSimulateLongPressRelease.Click += new EventHandler(this.ActionButtonContextMenuItemSimulateLongPressRelease_Click);
            // 
            // toolStripSeparator3
            // 
            this.toolStripSeparator3.Name = "toolStripSeparator3";
            this.toolStripSeparator3.Size = new Size(327, 6);
            // 
            // actionButtonContextMenuItemCopy
            // 
            this.actionButtonContextMenuItemCopy.ForeColor = Color.White;
            this.actionButtonContextMenuItemCopy.Name = "actionButtonContextMenuItemCopy";
            this.actionButtonContextMenuItemCopy.Size = new Size(330, 28);
            this.actionButtonContextMenuItemCopy.Text = "Copy";
            this.actionButtonContextMenuItemCopy.Click += new EventHandler(this.ActionButtonContextMenuItemCopy_Click);
            // 
            // actionButtonContextMenuItemPaste
            // 
            this.actionButtonContextMenuItemPaste.Enabled = false;
            this.actionButtonContextMenuItemPaste.ForeColor = Color.White;
            this.actionButtonContextMenuItemPaste.Name = "actionButtonContextMenuItemPaste";
            this.actionButtonContextMenuItemPaste.Size = new Size(330, 28);
            this.actionButtonContextMenuItemPaste.Text = "Paste";
            this.actionButtonContextMenuItemPaste.Click += new EventHandler(this.ActionButtonContextMenuItemPaste_Click);
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new Size(327, 6);
            // 
            // boxProfiles
            // 
            this.boxProfiles.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.boxProfiles.Cursor = Cursors.Hand;
            this.boxProfiles.DropDownStyle = ComboBoxStyle.DropDownList;
            this.boxProfiles.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.boxProfiles.ForeColor = Color.White;
            this.boxProfiles.Icon = null;
            this.boxProfiles.Location = new Point(239, 3);
            this.boxProfiles.Name = "boxProfiles";
            this.boxProfiles.Padding = new Padding(8, 2, 8, 2);
            this.boxProfiles.SelectedIndex = -1;
            this.boxProfiles.SelectedItem = null;
            this.boxProfiles.Size = new Size(285, 30);
            this.boxProfiles.TabIndex = 10;
            this.boxProfiles.SelectedIndexChanged += new EventHandler(this.BoxProfiles_SelectedIndexChanged);
            // 
            // btnAddProfile
            // 
            this.btnAddProfile.BackColor = Color.Transparent;
            this.btnAddProfile.BackgroundImage = Resources.Create_Normal;
            this.btnAddProfile.BackgroundImageLayout = ImageLayout.Stretch;
            this.btnAddProfile.Cursor = Cursors.Hand;
            this.btnAddProfile.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnAddProfile.ForeColor = Color.White;
            this.btnAddProfile.HoverImage = Resources.Create_Hover;
            this.btnAddProfile.Location = new Point(530, 6);
            this.btnAddProfile.Name = "btnAddProfile";
            this.btnAddProfile.Size = new Size(25, 25);
            this.btnAddProfile.TabIndex = 12;
            this.btnAddProfile.TabStop = false;
            this.btnAddProfile.Text = "+";
            this.btnAddProfile.Click += new EventHandler(this.BtnAddProfile_Click);
            // 
            // btnDeleteProfile
            // 
            this.btnDeleteProfile.BackColor = Color.Transparent;
            this.btnDeleteProfile.BackgroundImage = Resources.Delete_Normal;
            this.btnDeleteProfile.BackgroundImageLayout = ImageLayout.Stretch;
            this.btnDeleteProfile.Cursor = Cursors.Hand;
            this.btnDeleteProfile.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnDeleteProfile.ForeColor = Color.White;
            this.btnDeleteProfile.HoverImage = Resources.Delete_Hover;
            this.btnDeleteProfile.Location = new Point(588, 6);
            this.btnDeleteProfile.Name = "btnDeleteProfile";
            this.btnDeleteProfile.Size = new Size(25, 25);
            this.btnDeleteProfile.TabIndex = 13;
            this.btnDeleteProfile.TabStop = false;
            this.btnDeleteProfile.Click += new EventHandler(this.BtnDeleteProfile_Click);
            // 
            // buttonColumns
            // 
            this.buttonColumns.Anchor = ((AnchorStyles)((AnchorStyles.Bottom | AnchorStyles.Right)));
            this.buttonColumns.BackColor = Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.buttonColumns.BorderStyle = BorderStyle.FixedSingle;
            this.buttonColumns.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.buttonColumns.ForeColor = Color.White;
            this.buttonColumns.Location = new Point(930, 425);
            this.buttonColumns.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.buttonColumns.Name = "buttonColumns";
            this.buttonColumns.Size = new Size(55, 26);
            this.buttonColumns.TabIndex = 14;
            this.buttonColumns.Value = new decimal(new int[] {
            5,
            0,
            0,
            0});
            this.buttonColumns.ValueChanged += new EventHandler(this.ButtonSettingsChanged);
            // 
            // buttonRows
            // 
            this.buttonRows.Anchor = ((AnchorStyles)((AnchorStyles.Bottom | AnchorStyles.Right)));
            this.buttonRows.BackColor = Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.buttonRows.BorderStyle = BorderStyle.FixedSingle;
            this.buttonRows.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.buttonRows.ForeColor = Color.White;
            this.buttonRows.Location = new Point(1068, 425);
            this.buttonRows.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.buttonRows.Name = "buttonRows";
            this.buttonRows.Size = new Size(55, 26);
            this.buttonRows.TabIndex = 15;
            this.buttonRows.Value = new decimal(new int[] {
            3,
            0,
            0,
            0});
            this.buttonRows.ValueChanged += new EventHandler(this.ButtonSettingsChanged);
            // 
            // lblColumns
            // 
            this.lblColumns.Anchor = ((AnchorStyles)((AnchorStyles.Bottom | AnchorStyles.Right)));
            this.lblColumns.BackColor = Color.Transparent;
            this.lblColumns.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblColumns.ForeColor = Color.White;
            this.lblColumns.Location = new Point(854, 425);
            this.lblColumns.Name = "lblColumns";
            this.lblColumns.Size = new Size(72, 26);
            this.lblColumns.TabIndex = 16;
            this.lblColumns.Text = "Columns";
            this.lblColumns.TextAlign = ContentAlignment.MiddleRight;
            this.lblColumns.UseMnemonic = false;
            // 
            // lblRows
            // 
            this.lblRows.Anchor = ((AnchorStyles)((AnchorStyles.Bottom | AnchorStyles.Right)));
            this.lblRows.BackColor = Color.Transparent;
            this.lblRows.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblRows.ForeColor = Color.White;
            this.lblRows.Location = new Point(990, 425);
            this.lblRows.Name = "lblRows";
            this.lblRows.Size = new Size(72, 26);
            this.lblRows.TabIndex = 17;
            this.lblRows.Text = "Rows";
            this.lblRows.TextAlign = ContentAlignment.MiddleRight;
            this.lblRows.UseMnemonic = false;
            // 
            // lblSpacing
            // 
            this.lblSpacing.Anchor = ((AnchorStyles)((AnchorStyles.Bottom | AnchorStyles.Right)));
            this.lblSpacing.BackColor = Color.Transparent;
            this.lblSpacing.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblSpacing.ForeColor = Color.White;
            this.lblSpacing.Location = new Point(930, 457);
            this.lblSpacing.Name = "lblSpacing";
            this.lblSpacing.Size = new Size(132, 26);
            this.lblSpacing.TabIndex = 19;
            this.lblSpacing.Text = "Spacing";
            this.lblSpacing.TextAlign = ContentAlignment.MiddleRight;
            this.lblSpacing.UseMnemonic = false;
            // 
            // buttonSpacing
            // 
            this.buttonSpacing.Anchor = ((AnchorStyles)((AnchorStyles.Bottom | AnchorStyles.Right)));
            this.buttonSpacing.BackColor = Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.buttonSpacing.BorderStyle = BorderStyle.FixedSingle;
            this.buttonSpacing.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.buttonSpacing.ForeColor = Color.White;
            this.buttonSpacing.Location = new Point(1068, 457);
            this.buttonSpacing.Maximum = new decimal(new int[] {
            25,
            0,
            0,
            0});
            this.buttonSpacing.Name = "buttonSpacing";
            this.buttonSpacing.Size = new Size(55, 26);
            this.buttonSpacing.TabIndex = 18;
            this.buttonSpacing.Value = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.buttonSpacing.ValueChanged += new EventHandler(this.ButtonSettingsChanged);
            // 
            // lblCornerRadius
            // 
            this.lblCornerRadius.Anchor = ((AnchorStyles)((AnchorStyles.Bottom | AnchorStyles.Right)));
            this.lblCornerRadius.BackColor = Color.Transparent;
            this.lblCornerRadius.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblCornerRadius.ForeColor = Color.White;
            this.lblCornerRadius.Location = new Point(930, 489);
            this.lblCornerRadius.Name = "lblCornerRadius";
            this.lblCornerRadius.Size = new Size(132, 26);
            this.lblCornerRadius.TabIndex = 21;
            this.lblCornerRadius.Text = "Corner radius";
            this.lblCornerRadius.TextAlign = ContentAlignment.MiddleRight;
            this.lblCornerRadius.UseMnemonic = false;
            // 
            // cornerRadius
            // 
            this.cornerRadius.Anchor = ((AnchorStyles)((AnchorStyles.Bottom | AnchorStyles.Right)));
            this.cornerRadius.BackColor = Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.cornerRadius.BorderStyle = BorderStyle.FixedSingle;
            this.cornerRadius.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.cornerRadius.ForeColor = Color.White;
            this.cornerRadius.Location = new Point(1068, 489);
            this.cornerRadius.Name = "cornerRadius";
            this.cornerRadius.Size = new Size(55, 26);
            this.cornerRadius.TabIndex = 20;
            this.cornerRadius.Value = new decimal(new int[] {
            40,
            0,
            0,
            0});
            this.cornerRadius.ValueChanged += new EventHandler(this.ButtonSettingsChanged);
            // 
            // checkButtonBackground
            // 
            this.checkButtonBackground.Anchor = ((AnchorStyles)((AnchorStyles.Bottom | AnchorStyles.Right)));
            this.checkButtonBackground.AutoSize = true;
            this.checkButtonBackground.BackColor = Color.Transparent;
            this.checkButtonBackground.CheckAlign = ContentAlignment.MiddleRight;
            this.checkButtonBackground.Checked = true;
            this.checkButtonBackground.CheckState = CheckState.Checked;
            this.checkButtonBackground.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.checkButtonBackground.ForeColor = Color.White;
            this.checkButtonBackground.Location = new Point(972, 518);
            this.checkButtonBackground.Name = "checkButtonBackground";
            this.checkButtonBackground.Size = new Size(151, 22);
            this.checkButtonBackground.TabIndex = 22;
            this.checkButtonBackground.Text = "Button Background";
            this.checkButtonBackground.UseMnemonic = false;
            this.checkButtonBackground.UseVisualStyleBackColor = false;
            this.checkButtonBackground.CheckedChanged += new EventHandler(this.ButtonSettingsChanged);
            // 
            // btnEditProfile
            // 
            this.btnEditProfile.BackColor = Color.Transparent;
            this.btnEditProfile.BackgroundImage = Resources.Edit_Normal;
            this.btnEditProfile.BackgroundImageLayout = ImageLayout.Stretch;
            this.btnEditProfile.Cursor = Cursors.Hand;
            this.btnEditProfile.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnEditProfile.ForeColor = Color.White;
            this.btnEditProfile.HoverImage = Resources.Edit_Hover;
            this.btnEditProfile.Location = new Point(559, 6);
            this.btnEditProfile.Name = "btnEditProfile";
            this.btnEditProfile.Size = new Size(25, 25);
            this.btnEditProfile.TabIndex = 23;
            this.btnEditProfile.TabStop = false;
            this.btnEditProfile.Click += new EventHandler(this.BtnEditProfile_Click);
            // 
            // panel1
            // 
            this.panel1.Anchor = ((AnchorStyles)(((AnchorStyles.Top | AnchorStyles.Left) 
                                                  | AnchorStyles.Right)));
            this.panel1.Controls.Add(this.boxProfiles);
            this.panel1.Controls.Add(this.btnAddProfile);
            this.panel1.Controls.Add(this.btnDeleteProfile);
            this.panel1.Controls.Add(this.btnEditProfile);
            this.panel1.Location = new Point(6, 3);
            this.panel1.Name = "panel1";
            this.panel1.Size = new Size(841, 36);
            this.panel1.TabIndex = 24;
            // 
            // lblFolders
            // 
            this.lblFolders.BackColor = Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.lblFolders.Font = new Font("Tahoma", 11.25F, FontStyle.Bold, GraphicsUnit.Point);
            this.lblFolders.ForeColor = Color.White;
            this.lblFolders.Location = new Point(9, 6);
            this.lblFolders.Name = "lblFolders";
            this.lblFolders.Size = new Size(250, 22);
            this.lblFolders.TabIndex = 40;
            this.lblFolders.Text = "Folders";
            this.lblFolders.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // lblGrid
            // 
            this.lblGrid.BackColor = Color.Transparent;
            this.lblGrid.Font = new Font("Tahoma", 11.25F, FontStyle.Bold, GraphicsUnit.Point);
            this.lblGrid.ForeColor = Color.White;
            this.lblGrid.Location = new Point(9, 6);
            this.lblGrid.Name = "lblGrid";
            this.lblGrid.Size = new Size(250, 22);
            this.lblGrid.TabIndex = 41;
            this.lblGrid.Text = "Grid";
            this.lblGrid.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // roundedPanel1
            // 
            this.roundedPanel1.Anchor = ((AnchorStyles)((AnchorStyles.Bottom | AnchorStyles.Right)));
            this.roundedPanel1.BackColor = Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.roundedPanel1.Controls.Add(this.lblGrid);
            this.roundedPanel1.Location = new Point(857, 384);
            this.roundedPanel1.Name = "roundedPanel1";
            this.roundedPanel1.Size = new Size(269, 35);
            this.roundedPanel1.TabIndex = 42;
            // 
            // roundedPanel2
            // 
            this.roundedPanel2.Anchor = ((AnchorStyles)((AnchorStyles.Top | AnchorStyles.Right)));
            this.roundedPanel2.BackColor = Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.roundedPanel2.Controls.Add(this.label1);
            this.roundedPanel2.Controls.Add(this.lblFolders);
            this.roundedPanel2.Location = new Point(857, 4);
            this.roundedPanel2.Name = "roundedPanel2";
            this.roundedPanel2.Size = new Size(269, 35);
            this.roundedPanel2.TabIndex = 43;
            // 
            // label1
            // 
            this.label1.Anchor = ((AnchorStyles)((AnchorStyles.Bottom | AnchorStyles.Right)));
            this.label1.BackColor = Color.Transparent;
            this.label1.Font = new Font("Tahoma", 11.25F, FontStyle.Bold, GraphicsUnit.Point);
            this.label1.ForeColor = Color.White;
            this.label1.Location = new Point(78, -59);
            this.label1.Name = "label1";
            this.label1.Size = new Size(250, 22);
            this.label1.TabIndex = 41;
            this.label1.Text = "Grid";
            this.label1.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // DeckView
            // 
            this.AutoScaleDimensions = new SizeF(96F, 96F);
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.roundedPanel2);
            this.Controls.Add(this.roundedPanel1);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.checkButtonBackground);
            this.Controls.Add(this.lblCornerRadius);
            this.Controls.Add(this.cornerRadius);
            this.Controls.Add(this.lblSpacing);
            this.Controls.Add(this.buttonSpacing);
            this.Controls.Add(this.lblRows);
            this.Controls.Add(this.lblColumns);
            this.Controls.Add(this.buttonRows);
            this.Controls.Add(this.buttonColumns);
            this.Controls.Add(this.foldersView);
            this.Controls.Add(this.buttonPanel);
            this.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.Name = "DeckView";
            this.Size = new Size(1126, 545);
            this.Load += new EventHandler(this.Deck_Load);
            this.foldersContextMenu.ResumeLayout(false);
            this.actionButtonContextMenu.ResumeLayout(false);
            ((ISupportInitialize)(this.btnAddProfile)).EndInit();
            ((ISupportInitialize)(this.btnDeleteProfile)).EndInit();
            ((ISupportInitialize)(this.buttonColumns)).EndInit();
            ((ISupportInitialize)(this.buttonRows)).EndInit();
            ((ISupportInitialize)(this.buttonSpacing)).EndInit();
            ((ISupportInitialize)(this.cornerRadius)).EndInit();
            ((ISupportInitialize)(this.btnEditProfile)).EndInit();
            this.panel1.ResumeLayout(false);
            this.roundedPanel1.ResumeLayout(false);
            this.roundedPanel2.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private TreeView foldersView;
        private BufferedPanel buttonPanel;
        private ContextMenuStrip actionButtonContextMenu;
        private ToolStripMenuItem actionButtonContextMenuItemEdit;
        private ToolStripMenuItem actionButtonContextMenuItemDelete;
        private ContextMenuStrip foldersContextMenu;
        private ToolStripMenuItem foldersContextMenuEdit;
        private ToolStripMenuItem foldersContextMenuDelete;
        private ToolStripMenuItem foldersContextMenuNew;
        private ToolStripSeparator toolStripSeparator2;
        private ToolStripMenuItem actionButtonContextMenuItemCopy;
        private ToolStripMenuItem actionButtonContextMenuItemPaste;
        private ToolStripSeparator toolStripSeparator1;
        private RoundedComboBox boxProfiles;
        private PictureButton btnAddProfile;
        private PictureButton btnDeleteProfile;
        private NumericUpDown buttonColumns;
        private NumericUpDown buttonRows;
        private Label lblColumns;
        private Label lblRows;
        private Label lblSpacing;
        private NumericUpDown buttonSpacing;
        private Label lblCornerRadius;
        private NumericUpDown cornerRadius;
        private CheckBox checkButtonBackground;
        private PictureButton btnEditProfile;
        private Panel panel1;
        private ToolStripMenuItem actionButtonContextMenuItemSimulatePress;
        private ToolStripMenuItem actionButtonContextMenuItemSimulateRelease;
        private ToolStripMenuItem actionButtonContextMenuItemSimulateLongPress;
        private ToolStripMenuItem actionButtonContextMenuItemSimulateLongPressRelease;
        private ToolStripSeparator toolStripSeparator3;
        private Label lblFolders;
        private Label lblGrid;
        private RoundedPanel roundedPanel1;
        private RoundedPanel roundedPanel2;
        private Label label1;
    }
}
