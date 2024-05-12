
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
            components = new Container();
            foldersView = new TreeView();
            buttonPanel = new BufferedPanel();
            actionButtonContextMenuItemEdit = new ToolStripMenuItem();
            actionButtonContextMenuItemDelete = new ToolStripMenuItem();
            foldersContextMenuNew = new ToolStripMenuItem();
            foldersContextMenuEdit = new ToolStripMenuItem();
            foldersContextMenuDelete = new ToolStripMenuItem();
            foldersContextMenu = new ContextMenuStrip(components);
            actionButtonContextMenu = new ContextMenuStrip(components);
            toolStripSeparator2 = new ToolStripSeparator();
            actionButtonContextMenuItemSimulatePress = new ToolStripMenuItem();
            actionButtonContextMenuItemSimulateRelease = new ToolStripMenuItem();
            actionButtonContextMenuItemSimulateLongPress = new ToolStripMenuItem();
            actionButtonContextMenuItemSimulateLongPressRelease = new ToolStripMenuItem();
            toolStripSeparator3 = new ToolStripSeparator();
            actionButtonContextMenuItemCopy = new ToolStripMenuItem();
            actionButtonContextMenuItemPaste = new ToolStripMenuItem();
            toolStripSeparator1 = new ToolStripSeparator();
            boxProfiles = new RoundedComboBox();
            btnAddProfile = new PictureButton();
            btnDeleteProfile = new PictureButton();
            buttonColumns = new NumericUpDown();
            buttonRows = new NumericUpDown();
            lblColumns = new Label();
            lblRows = new Label();
            lblSpacing = new Label();
            buttonSpacing = new NumericUpDown();
            lblCornerRadius = new Label();
            cornerRadius = new NumericUpDown();
            checkButtonBackground = new CheckBox();
            btnEditProfile = new PictureButton();
            panel1 = new Panel();
            lblFolders = new Label();
            lblGrid = new Label();
            roundedPanel1 = new RoundedPanel();
            roundedPanel2 = new RoundedPanel();
            label1 = new Label();
            foldersContextMenu.SuspendLayout();
            actionButtonContextMenu.SuspendLayout();
            ((ISupportInitialize)btnAddProfile).BeginInit();
            ((ISupportInitialize)btnDeleteProfile).BeginInit();
            ((ISupportInitialize)buttonColumns).BeginInit();
            ((ISupportInitialize)buttonRows).BeginInit();
            ((ISupportInitialize)buttonSpacing).BeginInit();
            ((ISupportInitialize)cornerRadius).BeginInit();
            ((ISupportInitialize)btnEditProfile).BeginInit();
            panel1.SuspendLayout();
            roundedPanel1.SuspendLayout();
            roundedPanel2.SuspendLayout();
            SuspendLayout();
            // 
            // foldersView
            // 
            foldersView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            foldersView.BackColor = Color.FromArgb(45, 45, 45);
            foldersView.BorderStyle = BorderStyle.None;
            foldersView.Font = new Font("Tahoma", 11.25F);
            foldersView.ForeColor = Color.White;
            foldersView.FullRowSelect = true;
            foldersView.ItemHeight = 26;
            foldersView.LineColor = Color.White;
            foldersView.Location = new Point(0, 41);
            foldersView.Name = "foldersView";
            foldersView.PathSeparator = "/";
            foldersView.Size = new Size(227, 302);
            foldersView.TabIndex = 6;
            foldersView.AfterSelect += FoldersView_AfterSelect;
            foldersView.MouseDown += FoldersView_MouseDown;
            // 
            // buttonPanel
            // 
            buttonPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            buttonPanel.Location = new Point(236, 45);
            buttonPanel.Name = "buttonPanel";
            buttonPanel.Size = new Size(887, 500);
            buttonPanel.TabIndex = 5;
            // 
            // actionButtonContextMenuItemEdit
            // 
            actionButtonContextMenuItemEdit.ForeColor = Color.White;
            actionButtonContextMenuItemEdit.Name = "actionButtonContextMenuItemEdit";
            actionButtonContextMenuItemEdit.Size = new Size(330, 28);
            actionButtonContextMenuItemEdit.Text = "Edit";
            actionButtonContextMenuItemEdit.Click += ContextMenuEditItemClick;
            // 
            // actionButtonContextMenuItemDelete
            // 
            actionButtonContextMenuItemDelete.ForeColor = Color.White;
            actionButtonContextMenuItemDelete.Name = "actionButtonContextMenuItemDelete";
            actionButtonContextMenuItemDelete.Size = new Size(330, 28);
            actionButtonContextMenuItemDelete.Text = "Delete";
            actionButtonContextMenuItemDelete.Click += ContextMenuDeleteItemClick;
            // 
            // foldersContextMenuNew
            // 
            foldersContextMenuNew.ForeColor = Color.White;
            foldersContextMenuNew.Name = "foldersContextMenuNew";
            foldersContextMenuNew.Size = new Size(146, 28);
            foldersContextMenuNew.Text = "New folder";
            foldersContextMenuNew.Click += BtnCreateFolder_Click;
            // 
            // foldersContextMenuEdit
            // 
            foldersContextMenuEdit.ForeColor = Color.White;
            foldersContextMenuEdit.Name = "foldersContextMenuEdit";
            foldersContextMenuEdit.Size = new Size(146, 28);
            foldersContextMenuEdit.Text = "Edit";
            foldersContextMenuEdit.Click += BtnRenameFolder_Click;
            // 
            // foldersContextMenuDelete
            // 
            foldersContextMenuDelete.ForeColor = Color.White;
            foldersContextMenuDelete.Name = "foldersContextMenuDelete";
            foldersContextMenuDelete.Size = new Size(146, 28);
            foldersContextMenuDelete.Text = "Delete";
            foldersContextMenuDelete.Click += BtnDeleteFolder_Click;
            // 
            // foldersContextMenu
            // 
            foldersContextMenu.BackColor = Color.FromArgb(55, 55, 55);
            foldersContextMenu.Font = new Font("Tahoma", 14.25F);
            foldersContextMenu.Items.AddRange(new ToolStripItem[] { foldersContextMenuNew, foldersContextMenuEdit, foldersContextMenuDelete });
            foldersContextMenu.Name = "foldersContextMenu";
            foldersContextMenu.ShowImageMargin = false;
            foldersContextMenu.Size = new Size(147, 88);
            // 
            // actionButtonContextMenu
            // 
            actionButtonContextMenu.BackColor = Color.FromArgb(55, 55, 55);
            actionButtonContextMenu.Font = new Font("Tahoma", 14.25F);
            actionButtonContextMenu.Items.AddRange(new ToolStripItem[] { actionButtonContextMenuItemEdit, toolStripSeparator2, actionButtonContextMenuItemSimulatePress, actionButtonContextMenuItemSimulateRelease, actionButtonContextMenuItemSimulateLongPress, actionButtonContextMenuItemSimulateLongPressRelease, toolStripSeparator3, actionButtonContextMenuItemCopy, actionButtonContextMenuItemPaste, toolStripSeparator1, actionButtonContextMenuItemDelete });
            actionButtonContextMenu.Name = "actionButtonContextMenu";
            actionButtonContextMenu.ShowImageMargin = false;
            actionButtonContextMenu.Size = new Size(331, 246);
            actionButtonContextMenu.Opening += ActionButtonContextMenuOpened;
            // 
            // toolStripSeparator2
            // 
            toolStripSeparator2.Name = "toolStripSeparator2";
            toolStripSeparator2.Size = new Size(327, 6);
            // 
            // actionButtonContextMenuItemSimulatePress
            // 
            actionButtonContextMenuItemSimulatePress.ForeColor = Color.White;
            actionButtonContextMenuItemSimulatePress.Name = "actionButtonContextMenuItemSimulatePress";
            actionButtonContextMenuItemSimulatePress.Size = new Size(330, 28);
            actionButtonContextMenuItemSimulatePress.Text = "Simulate \"On press\"";
            actionButtonContextMenuItemSimulatePress.Click += ActionButtonContextMenuItemSimulatePress_Click;
            // 
            // actionButtonContextMenuItemSimulateRelease
            // 
            actionButtonContextMenuItemSimulateRelease.ForeColor = Color.White;
            actionButtonContextMenuItemSimulateRelease.Name = "actionButtonContextMenuItemSimulateRelease";
            actionButtonContextMenuItemSimulateRelease.Size = new Size(330, 28);
            actionButtonContextMenuItemSimulateRelease.Text = "Simulate \"On release\"";
            actionButtonContextMenuItemSimulateRelease.Click += ActionButtonContextMenuItemSimulateRelease_Click;
            // 
            // actionButtonContextMenuItemSimulateLongPress
            // 
            actionButtonContextMenuItemSimulateLongPress.ForeColor = Color.White;
            actionButtonContextMenuItemSimulateLongPress.Name = "actionButtonContextMenuItemSimulateLongPress";
            actionButtonContextMenuItemSimulateLongPress.Size = new Size(330, 28);
            actionButtonContextMenuItemSimulateLongPress.Text = "Simulate \"On long press\"";
            actionButtonContextMenuItemSimulateLongPress.Click += ActionButtonContextMenuItemSimulateLongPress_Click;
            // 
            // actionButtonContextMenuItemSimulateLongPressRelease
            // 
            actionButtonContextMenuItemSimulateLongPressRelease.ForeColor = Color.White;
            actionButtonContextMenuItemSimulateLongPressRelease.Name = "actionButtonContextMenuItemSimulateLongPressRelease";
            actionButtonContextMenuItemSimulateLongPressRelease.Size = new Size(330, 28);
            actionButtonContextMenuItemSimulateLongPressRelease.Text = "Simulate \"On long press release\"";
            actionButtonContextMenuItemSimulateLongPressRelease.Click += ActionButtonContextMenuItemSimulateLongPressRelease_Click;
            // 
            // toolStripSeparator3
            // 
            toolStripSeparator3.Name = "toolStripSeparator3";
            toolStripSeparator3.Size = new Size(327, 6);
            // 
            // actionButtonContextMenuItemCopy
            // 
            actionButtonContextMenuItemCopy.ForeColor = Color.White;
            actionButtonContextMenuItemCopy.Name = "actionButtonContextMenuItemCopy";
            actionButtonContextMenuItemCopy.Size = new Size(330, 28);
            actionButtonContextMenuItemCopy.Text = "Copy";
            actionButtonContextMenuItemCopy.Click += ActionButtonContextMenuItemCopy_Click;
            // 
            // actionButtonContextMenuItemPaste
            // 
            actionButtonContextMenuItemPaste.Enabled = false;
            actionButtonContextMenuItemPaste.ForeColor = Color.White;
            actionButtonContextMenuItemPaste.Name = "actionButtonContextMenuItemPaste";
            actionButtonContextMenuItemPaste.Size = new Size(330, 28);
            actionButtonContextMenuItemPaste.Text = "Paste";
            actionButtonContextMenuItemPaste.Click += ActionButtonContextMenuItemPaste_Click;
            // 
            // toolStripSeparator1
            // 
            toolStripSeparator1.Name = "toolStripSeparator1";
            toolStripSeparator1.Size = new Size(327, 6);
            // 
            // boxProfiles
            // 
            boxProfiles.Anchor = AnchorStyles.Top;
            boxProfiles.BackColor = Color.FromArgb(65, 65, 65);
            boxProfiles.Cursor = Cursors.Hand;
            boxProfiles.DropDownStyle = ComboBoxStyle.DropDownList;
            boxProfiles.Font = new Font("Tahoma", 11.25F);
            boxProfiles.ForeColor = Color.White;
            boxProfiles.Icon = null;
            boxProfiles.Location = new Point(260, 3);
            boxProfiles.Name = "boxProfiles";
            boxProfiles.Padding = new Padding(8, 2, 8, 2);
            boxProfiles.SelectedIndex = -1;
            boxProfiles.SelectedItem = null;
            boxProfiles.Size = new Size(285, 30);
            boxProfiles.TabIndex = 10;
            boxProfiles.SelectedIndexChanged += BoxProfiles_SelectedIndexChanged;
            // 
            // btnAddProfile
            // 
            btnAddProfile.Anchor = AnchorStyles.Top;
            btnAddProfile.BackColor = Color.Transparent;
            btnAddProfile.BackgroundImage = Resources.Create_Normal;
            btnAddProfile.BackgroundImageLayout = ImageLayout.Stretch;
            btnAddProfile.Cursor = Cursors.Hand;
            btnAddProfile.Font = new Font("Tahoma", 9.75F);
            btnAddProfile.ForeColor = Color.White;
            btnAddProfile.HoverImage = Resources.Create_Hover;
            btnAddProfile.Location = new Point(551, 6);
            btnAddProfile.Name = "btnAddProfile";
            btnAddProfile.Size = new Size(25, 25);
            btnAddProfile.TabIndex = 12;
            btnAddProfile.TabStop = false;
            btnAddProfile.Text = "+";
            btnAddProfile.Click += BtnAddProfile_Click;
            // 
            // btnDeleteProfile
            // 
            btnDeleteProfile.Anchor = AnchorStyles.Top;
            btnDeleteProfile.BackColor = Color.Transparent;
            btnDeleteProfile.BackgroundImage = Resources.Delete_Normal;
            btnDeleteProfile.BackgroundImageLayout = ImageLayout.Stretch;
            btnDeleteProfile.Cursor = Cursors.Hand;
            btnDeleteProfile.Font = new Font("Tahoma", 9.75F);
            btnDeleteProfile.ForeColor = Color.White;
            btnDeleteProfile.HoverImage = Resources.Delete_Hover;
            btnDeleteProfile.Location = new Point(609, 6);
            btnDeleteProfile.Name = "btnDeleteProfile";
            btnDeleteProfile.Size = new Size(25, 25);
            btnDeleteProfile.TabIndex = 13;
            btnDeleteProfile.TabStop = false;
            btnDeleteProfile.Click += BtnDeleteProfile_Click;
            // 
            // buttonColumns
            // 
            buttonColumns.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            buttonColumns.BackColor = Color.FromArgb(55, 55, 55);
            buttonColumns.BorderStyle = BorderStyle.FixedSingle;
            buttonColumns.Font = new Font("Tahoma", 11.25F);
            buttonColumns.ForeColor = Color.White;
            buttonColumns.Location = new Point(9, 424);
            buttonColumns.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            buttonColumns.Name = "buttonColumns";
            buttonColumns.Size = new Size(55, 26);
            buttonColumns.TabIndex = 14;
            buttonColumns.Value = new decimal(new int[] { 5, 0, 0, 0 });
            buttonColumns.ValueChanged += ButtonSettingsChanged;
            // 
            // buttonRows
            // 
            buttonRows.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            buttonRows.BackColor = Color.FromArgb(55, 55, 55);
            buttonRows.BorderStyle = BorderStyle.FixedSingle;
            buttonRows.Font = new Font("Tahoma", 11.25F);
            buttonRows.ForeColor = Color.White;
            buttonRows.Location = new Point(9, 392);
            buttonRows.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            buttonRows.Name = "buttonRows";
            buttonRows.Size = new Size(55, 26);
            buttonRows.TabIndex = 15;
            buttonRows.Value = new decimal(new int[] { 3, 0, 0, 0 });
            buttonRows.ValueChanged += ButtonSettingsChanged;
            // 
            // lblColumns
            // 
            lblColumns.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            lblColumns.BackColor = Color.Transparent;
            lblColumns.Font = new Font("Tahoma", 11.25F);
            lblColumns.ForeColor = Color.White;
            lblColumns.Location = new Point(70, 422);
            lblColumns.Name = "lblColumns";
            lblColumns.Size = new Size(154, 26);
            lblColumns.TabIndex = 16;
            lblColumns.Text = "Columns";
            lblColumns.TextAlign = ContentAlignment.MiddleLeft;
            lblColumns.UseMnemonic = false;
            // 
            // lblRows
            // 
            lblRows.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            lblRows.BackColor = Color.Transparent;
            lblRows.Font = new Font("Tahoma", 11.25F);
            lblRows.ForeColor = Color.White;
            lblRows.Location = new Point(70, 392);
            lblRows.Name = "lblRows";
            lblRows.Size = new Size(157, 26);
            lblRows.TabIndex = 17;
            lblRows.Text = "Rows";
            lblRows.TextAlign = ContentAlignment.MiddleLeft;
            lblRows.UseMnemonic = false;
            // 
            // lblSpacing
            // 
            lblSpacing.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            lblSpacing.BackColor = Color.Transparent;
            lblSpacing.Font = new Font("Tahoma", 11.25F);
            lblSpacing.ForeColor = Color.White;
            lblSpacing.Location = new Point(70, 454);
            lblSpacing.Name = "lblSpacing";
            lblSpacing.Size = new Size(157, 26);
            lblSpacing.TabIndex = 19;
            lblSpacing.Text = "Spacing";
            lblSpacing.TextAlign = ContentAlignment.MiddleLeft;
            lblSpacing.UseMnemonic = false;
            // 
            // buttonSpacing
            // 
            buttonSpacing.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            buttonSpacing.BackColor = Color.FromArgb(55, 55, 55);
            buttonSpacing.BorderStyle = BorderStyle.FixedSingle;
            buttonSpacing.Font = new Font("Tahoma", 11.25F);
            buttonSpacing.ForeColor = Color.White;
            buttonSpacing.Location = new Point(9, 456);
            buttonSpacing.Maximum = new decimal(new int[] { 25, 0, 0, 0 });
            buttonSpacing.Name = "buttonSpacing";
            buttonSpacing.Size = new Size(55, 26);
            buttonSpacing.TabIndex = 18;
            buttonSpacing.Value = new decimal(new int[] { 10, 0, 0, 0 });
            buttonSpacing.ValueChanged += ButtonSettingsChanged;
            // 
            // lblCornerRadius
            // 
            lblCornerRadius.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            lblCornerRadius.BackColor = Color.Transparent;
            lblCornerRadius.Font = new Font("Tahoma", 11.25F);
            lblCornerRadius.ForeColor = Color.White;
            lblCornerRadius.Location = new Point(70, 488);
            lblCornerRadius.Name = "lblCornerRadius";
            lblCornerRadius.Size = new Size(154, 26);
            lblCornerRadius.TabIndex = 21;
            lblCornerRadius.Text = "Corner radius";
            lblCornerRadius.TextAlign = ContentAlignment.MiddleLeft;
            lblCornerRadius.UseMnemonic = false;
            // 
            // cornerRadius
            // 
            cornerRadius.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            cornerRadius.BackColor = Color.FromArgb(55, 55, 55);
            cornerRadius.BorderStyle = BorderStyle.FixedSingle;
            cornerRadius.Font = new Font("Tahoma", 11.25F);
            cornerRadius.ForeColor = Color.White;
            cornerRadius.Location = new Point(9, 488);
            cornerRadius.Name = "cornerRadius";
            cornerRadius.Size = new Size(55, 26);
            cornerRadius.TabIndex = 20;
            cornerRadius.Value = new decimal(new int[] { 40, 0, 0, 0 });
            cornerRadius.ValueChanged += ButtonSettingsChanged;
            // 
            // checkButtonBackground
            // 
            checkButtonBackground.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            checkButtonBackground.AutoSize = true;
            checkButtonBackground.BackColor = Color.Transparent;
            checkButtonBackground.Checked = true;
            checkButtonBackground.CheckState = CheckState.Checked;
            checkButtonBackground.Font = new Font("Tahoma", 11.25F);
            checkButtonBackground.ForeColor = Color.White;
            checkButtonBackground.Location = new Point(9, 520);
            checkButtonBackground.Name = "checkButtonBackground";
            checkButtonBackground.Size = new Size(151, 22);
            checkButtonBackground.TabIndex = 22;
            checkButtonBackground.Text = "Button Background";
            checkButtonBackground.UseMnemonic = false;
            checkButtonBackground.UseVisualStyleBackColor = false;
            checkButtonBackground.CheckedChanged += ButtonSettingsChanged;
            // 
            // btnEditProfile
            // 
            btnEditProfile.Anchor = AnchorStyles.Top;
            btnEditProfile.BackColor = Color.Transparent;
            btnEditProfile.BackgroundImage = Resources.Edit_Normal;
            btnEditProfile.BackgroundImageLayout = ImageLayout.Stretch;
            btnEditProfile.Cursor = Cursors.Hand;
            btnEditProfile.Font = new Font("Tahoma", 9.75F);
            btnEditProfile.ForeColor = Color.White;
            btnEditProfile.HoverImage = Resources.Edit_Hover;
            btnEditProfile.Location = new Point(580, 6);
            btnEditProfile.Name = "btnEditProfile";
            btnEditProfile.Size = new Size(25, 25);
            btnEditProfile.TabIndex = 23;
            btnEditProfile.TabStop = false;
            btnEditProfile.Click += BtnEditProfile_Click;
            // 
            // panel1
            // 
            panel1.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            panel1.Controls.Add(boxProfiles);
            panel1.Controls.Add(btnAddProfile);
            panel1.Controls.Add(btnDeleteProfile);
            panel1.Controls.Add(btnEditProfile);
            panel1.Location = new Point(236, 3);
            panel1.Name = "panel1";
            panel1.Size = new Size(887, 36);
            panel1.TabIndex = 24;
            // 
            // lblFolders
            // 
            lblFolders.BackColor = Color.FromArgb(35, 35, 35);
            lblFolders.Font = new Font("Tahoma", 11.25F, FontStyle.Bold);
            lblFolders.ForeColor = Color.White;
            lblFolders.Location = new Point(9, 6);
            lblFolders.Name = "lblFolders";
            lblFolders.Size = new Size(215, 22);
            lblFolders.TabIndex = 40;
            lblFolders.Text = "Folders";
            lblFolders.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // lblGrid
            // 
            lblGrid.BackColor = Color.Transparent;
            lblGrid.Font = new Font("Tahoma", 11.25F, FontStyle.Bold);
            lblGrid.ForeColor = Color.White;
            lblGrid.Location = new Point(9, 6);
            lblGrid.Name = "lblGrid";
            lblGrid.Size = new Size(215, 22);
            lblGrid.TabIndex = 41;
            lblGrid.Text = "Grid";
            lblGrid.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // roundedPanel1
            // 
            roundedPanel1.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            roundedPanel1.BackColor = Color.FromArgb(35, 35, 35);
            roundedPanel1.Controls.Add(lblGrid);
            roundedPanel1.Location = new Point(0, 349);
            roundedPanel1.Name = "roundedPanel1";
            roundedPanel1.Size = new Size(227, 35);
            roundedPanel1.TabIndex = 42;
            // 
            // roundedPanel2
            // 
            roundedPanel2.BackColor = Color.FromArgb(35, 35, 35);
            roundedPanel2.Controls.Add(label1);
            roundedPanel2.Controls.Add(lblFolders);
            roundedPanel2.Location = new Point(0, 0);
            roundedPanel2.Name = "roundedPanel2";
            roundedPanel2.Size = new Size(227, 35);
            roundedPanel2.TabIndex = 43;
            // 
            // label1
            // 
            label1.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            label1.BackColor = Color.Transparent;
            label1.Font = new Font("Tahoma", 11.25F, FontStyle.Bold);
            label1.ForeColor = Color.White;
            label1.Location = new Point(36, -59);
            label1.Name = "label1";
            label1.Size = new Size(250, 22);
            label1.TabIndex = 41;
            label1.Text = "Grid";
            label1.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // DeckView
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(45, 45, 45);
            Controls.Add(roundedPanel2);
            Controls.Add(roundedPanel1);
            Controls.Add(panel1);
            Controls.Add(checkButtonBackground);
            Controls.Add(lblCornerRadius);
            Controls.Add(cornerRadius);
            Controls.Add(lblSpacing);
            Controls.Add(buttonSpacing);
            Controls.Add(lblRows);
            Controls.Add(lblColumns);
            Controls.Add(buttonRows);
            Controls.Add(buttonColumns);
            Controls.Add(foldersView);
            Controls.Add(buttonPanel);
            Font = new Font("Tahoma", 9F);
            Name = "DeckView";
            Size = new Size(1126, 545);
            Load += Deck_Load;
            foldersContextMenu.ResumeLayout(false);
            actionButtonContextMenu.ResumeLayout(false);
            ((ISupportInitialize)btnAddProfile).EndInit();
            ((ISupportInitialize)btnDeleteProfile).EndInit();
            ((ISupportInitialize)buttonColumns).EndInit();
            ((ISupportInitialize)buttonRows).EndInit();
            ((ISupportInitialize)buttonSpacing).EndInit();
            ((ISupportInitialize)cornerRadius).EndInit();
            ((ISupportInitialize)btnEditProfile).EndInit();
            panel1.ResumeLayout(false);
            roundedPanel1.ResumeLayout(false);
            roundedPanel2.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
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
