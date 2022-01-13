
using SuchByte.MacroDeck.GUI.CustomControls;
using System;

namespace SuchByte.MacroDeck.GUI.MainWindowContents
{
    partial class DeckView
    {
        /// <summary> 
        /// Erforderliche Designervariable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

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
            this.components = new System.ComponentModel.Container();
            this.foldersView = new System.Windows.Forms.TreeView();
            this.buttonPanel = new SuchByte.MacroDeck.GUI.CustomControls.BufferedPanel();
            this.actionButtonContextMenuItemEdit = new System.Windows.Forms.ToolStripMenuItem();
            this.actionButtonContextMenuItemDelete = new System.Windows.Forms.ToolStripMenuItem();
            this.foldersContextMenuNew = new System.Windows.Forms.ToolStripMenuItem();
            this.foldersContextMenuEdit = new System.Windows.Forms.ToolStripMenuItem();
            this.foldersContextMenuDelete = new System.Windows.Forms.ToolStripMenuItem();
            this.foldersContextMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.actionButtonContextMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.actionButtonContextMenuItemCopy = new System.Windows.Forms.ToolStripMenuItem();
            this.actionButtonContextMenuItemPaste = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.boxProfiles = new SuchByte.MacroDeck.GUI.CustomControls.RoundedComboBox();
            this.btnAddProfile = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.btnDeleteProfile = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.buttonColumns = new System.Windows.Forms.NumericUpDown();
            this.buttonRows = new System.Windows.Forms.NumericUpDown();
            this.lblColumns = new System.Windows.Forms.Label();
            this.lblRows = new System.Windows.Forms.Label();
            this.lblSpacing = new System.Windows.Forms.Label();
            this.buttonSpacing = new System.Windows.Forms.NumericUpDown();
            this.lblCornerRadius = new System.Windows.Forms.Label();
            this.cornerRadius = new System.Windows.Forms.NumericUpDown();
            this.checkButtonBackground = new System.Windows.Forms.CheckBox();
            this.btnEditProfile = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.panel1 = new System.Windows.Forms.Panel();
            this.actionButtonContextMenuItemSimulatePress = new System.Windows.Forms.ToolStripMenuItem();
            this.actionButtonContextMenuItemSimulateRelease = new System.Windows.Forms.ToolStripMenuItem();
            this.actionButtonContextMenuItemSimulateLongPress = new System.Windows.Forms.ToolStripMenuItem();
            this.actionButtonContextMenuItemSimulateLongPressRelease = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator3 = new System.Windows.Forms.ToolStripSeparator();
            this.foldersContextMenu.SuspendLayout();
            this.actionButtonContextMenu.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.btnAddProfile)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnDeleteProfile)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.buttonColumns)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.buttonRows)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.buttonSpacing)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.cornerRadius)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnEditProfile)).BeginInit();
            this.panel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // foldersView
            // 
            this.foldersView.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.foldersView.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.foldersView.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.foldersView.Font = new System.Drawing.Font("Segoe UI", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.foldersView.ForeColor = System.Drawing.Color.White;
            this.foldersView.FullRowSelect = true;
            this.foldersView.ItemHeight = 26;
            this.foldersView.LineColor = System.Drawing.Color.White;
            this.foldersView.Location = new System.Drawing.Point(865, 0);
            this.foldersView.Name = "foldersView";
            this.foldersView.PathSeparator = "/";
            this.foldersView.Size = new System.Drawing.Size(272, 402);
            this.foldersView.TabIndex = 6;
            this.foldersView.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.FoldersView_AfterSelect);
            this.foldersView.MouseDown += new System.Windows.Forms.MouseEventHandler(this.FoldersView_MouseDown);
            // 
            // buttonPanel
            // 
            this.buttonPanel.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonPanel.Location = new System.Drawing.Point(6, 45);
            this.buttonPanel.Name = "buttonPanel";
            this.buttonPanel.Size = new System.Drawing.Size(852, 495);
            this.buttonPanel.TabIndex = 5;
            // 
            // actionButtonContextMenuItemEdit
            // 
            this.actionButtonContextMenuItemEdit.ForeColor = System.Drawing.Color.White;
            this.actionButtonContextMenuItemEdit.Name = "actionButtonContextMenuItemEdit";
            this.actionButtonContextMenuItemEdit.Size = new System.Drawing.Size(330, 28);
            this.actionButtonContextMenuItemEdit.Text = "Edit";
            this.actionButtonContextMenuItemEdit.Click += new System.EventHandler(this.ContextMenuEditItemClick);
            // 
            // actionButtonContextMenuItemDelete
            // 
            this.actionButtonContextMenuItemDelete.ForeColor = System.Drawing.Color.White;
            this.actionButtonContextMenuItemDelete.Name = "actionButtonContextMenuItemDelete";
            this.actionButtonContextMenuItemDelete.Size = new System.Drawing.Size(330, 28);
            this.actionButtonContextMenuItemDelete.Text = "Delete";
            this.actionButtonContextMenuItemDelete.Click += new System.EventHandler(this.ContextMenuDeleteItemClick);
            // 
            // foldersContextMenuNew
            // 
            this.foldersContextMenuNew.ForeColor = System.Drawing.Color.White;
            this.foldersContextMenuNew.Name = "foldersContextMenuNew";
            this.foldersContextMenuNew.Size = new System.Drawing.Size(152, 30);
            this.foldersContextMenuNew.Text = "New folder";
            this.foldersContextMenuNew.Click += new System.EventHandler(this.BtnCreateFolder_Click);
            // 
            // foldersContextMenuEdit
            // 
            this.foldersContextMenuEdit.ForeColor = System.Drawing.Color.White;
            this.foldersContextMenuEdit.Name = "foldersContextMenuEdit";
            this.foldersContextMenuEdit.Size = new System.Drawing.Size(152, 30);
            this.foldersContextMenuEdit.Text = "Edit";
            this.foldersContextMenuEdit.Click += new System.EventHandler(this.BtnRenameFolder_Click);
            // 
            // foldersContextMenuDelete
            // 
            this.foldersContextMenuDelete.ForeColor = System.Drawing.Color.White;
            this.foldersContextMenuDelete.Name = "foldersContextMenuDelete";
            this.foldersContextMenuDelete.Size = new System.Drawing.Size(152, 30);
            this.foldersContextMenuDelete.Text = "Delete";
            this.foldersContextMenuDelete.Click += new System.EventHandler(this.BtnDeleteFolder_Click);
            // 
            // foldersContextMenu
            // 
            this.foldersContextMenu.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.foldersContextMenu.Font = new System.Drawing.Font("Segoe UI", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.foldersContextMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.foldersContextMenuNew,
            this.foldersContextMenuEdit,
            this.foldersContextMenuDelete});
            this.foldersContextMenu.Name = "foldersContextMenu";
            this.foldersContextMenu.ShowImageMargin = false;
            this.foldersContextMenu.Size = new System.Drawing.Size(153, 94);
            // 
            // actionButtonContextMenu
            // 
            this.actionButtonContextMenu.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.actionButtonContextMenu.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.actionButtonContextMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
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
            this.actionButtonContextMenu.Size = new System.Drawing.Size(331, 268);
            this.actionButtonContextMenu.Opening += new System.ComponentModel.CancelEventHandler(this.ActionButtonContextMenuOpened);
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new System.Drawing.Size(327, 6);
            // 
            // actionButtonContextMenuItemCopy
            // 
            this.actionButtonContextMenuItemCopy.ForeColor = System.Drawing.Color.White;
            this.actionButtonContextMenuItemCopy.Name = "actionButtonContextMenuItemCopy";
            this.actionButtonContextMenuItemCopy.Size = new System.Drawing.Size(330, 28);
            this.actionButtonContextMenuItemCopy.Text = "Copy";
            this.actionButtonContextMenuItemCopy.Click += new System.EventHandler(this.ActionButtonContextMenuItemCopy_Click);
            // 
            // actionButtonContextMenuItemPaste
            // 
            this.actionButtonContextMenuItemPaste.Enabled = false;
            this.actionButtonContextMenuItemPaste.ForeColor = System.Drawing.Color.White;
            this.actionButtonContextMenuItemPaste.Name = "actionButtonContextMenuItemPaste";
            this.actionButtonContextMenuItemPaste.Size = new System.Drawing.Size(330, 28);
            this.actionButtonContextMenuItemPaste.Text = "Paste";
            this.actionButtonContextMenuItemPaste.Click += new System.EventHandler(this.ActionButtonContextMenuItemPaste_Click);
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(327, 6);
            // 
            // boxProfiles
            // 
            this.boxProfiles.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.boxProfiles.Cursor = System.Windows.Forms.Cursors.Hand;
            this.boxProfiles.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.boxProfiles.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.boxProfiles.ForeColor = System.Drawing.Color.White;
            this.boxProfiles.Icon = null;
            this.boxProfiles.Location = new System.Drawing.Point(239, 3);
            this.boxProfiles.Name = "boxProfiles";
            this.boxProfiles.Padding = new System.Windows.Forms.Padding(8, 2, 8, 2);
            this.boxProfiles.SelectedIndex = -1;
            this.boxProfiles.SelectedItem = null;
            this.boxProfiles.Size = new System.Drawing.Size(285, 30);
            this.boxProfiles.TabIndex = 10;
            this.boxProfiles.SelectedIndexChanged += new System.EventHandler(this.BoxProfiles_SelectedIndexChanged);
            // 
            // btnAddProfile
            // 
            this.btnAddProfile.BackColor = System.Drawing.Color.Transparent;
            this.btnAddProfile.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Create_Normal;
            this.btnAddProfile.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnAddProfile.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnAddProfile.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnAddProfile.ForeColor = System.Drawing.Color.White;
            this.btnAddProfile.HoverImage = global::SuchByte.MacroDeck.Properties.Resources.Create_Hover;
            this.btnAddProfile.Location = new System.Drawing.Point(530, 6);
            this.btnAddProfile.Name = "btnAddProfile";
            this.btnAddProfile.Size = new System.Drawing.Size(25, 25);
            this.btnAddProfile.TabIndex = 12;
            this.btnAddProfile.TabStop = false;
            this.btnAddProfile.Text = "+";
            this.btnAddProfile.Click += new System.EventHandler(this.BtnAddProfile_Click);
            // 
            // btnDeleteProfile
            // 
            this.btnDeleteProfile.BackColor = System.Drawing.Color.Transparent;
            this.btnDeleteProfile.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Delete_Normal;
            this.btnDeleteProfile.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnDeleteProfile.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnDeleteProfile.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnDeleteProfile.ForeColor = System.Drawing.Color.White;
            this.btnDeleteProfile.HoverImage = global::SuchByte.MacroDeck.Properties.Resources.Delete_Hover;
            this.btnDeleteProfile.Location = new System.Drawing.Point(588, 6);
            this.btnDeleteProfile.Name = "btnDeleteProfile";
            this.btnDeleteProfile.Size = new System.Drawing.Size(25, 25);
            this.btnDeleteProfile.TabIndex = 13;
            this.btnDeleteProfile.TabStop = false;
            this.btnDeleteProfile.Click += new System.EventHandler(this.BtnDeleteProfile_Click);
            // 
            // buttonColumns
            // 
            this.buttonColumns.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonColumns.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.buttonColumns.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.buttonColumns.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.buttonColumns.ForeColor = System.Drawing.Color.White;
            this.buttonColumns.Location = new System.Drawing.Point(941, 408);
            this.buttonColumns.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.buttonColumns.Name = "buttonColumns";
            this.buttonColumns.Size = new System.Drawing.Size(55, 30);
            this.buttonColumns.TabIndex = 14;
            this.buttonColumns.Value = new decimal(new int[] {
            5,
            0,
            0,
            0});
            this.buttonColumns.ValueChanged += new System.EventHandler(this.ButtonSettingsChanged);
            // 
            // buttonRows
            // 
            this.buttonRows.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonRows.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.buttonRows.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.buttonRows.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.buttonRows.ForeColor = System.Drawing.Color.White;
            this.buttonRows.Location = new System.Drawing.Point(1079, 408);
            this.buttonRows.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.buttonRows.Name = "buttonRows";
            this.buttonRows.Size = new System.Drawing.Size(55, 30);
            this.buttonRows.TabIndex = 15;
            this.buttonRows.Value = new decimal(new int[] {
            3,
            0,
            0,
            0});
            this.buttonRows.ValueChanged += new System.EventHandler(this.ButtonSettingsChanged);
            // 
            // lblColumns
            // 
            this.lblColumns.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.lblColumns.BackColor = System.Drawing.Color.Transparent;
            this.lblColumns.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblColumns.ForeColor = System.Drawing.Color.White;
            this.lblColumns.Location = new System.Drawing.Point(863, 408);
            this.lblColumns.Name = "lblColumns";
            this.lblColumns.Size = new System.Drawing.Size(72, 30);
            this.lblColumns.TabIndex = 16;
            this.lblColumns.Text = "Columns";
            this.lblColumns.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // lblRows
            // 
            this.lblRows.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.lblRows.BackColor = System.Drawing.Color.Transparent;
            this.lblRows.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblRows.ForeColor = System.Drawing.Color.White;
            this.lblRows.Location = new System.Drawing.Point(1001, 408);
            this.lblRows.Name = "lblRows";
            this.lblRows.Size = new System.Drawing.Size(72, 30);
            this.lblRows.TabIndex = 17;
            this.lblRows.Text = "Rows";
            this.lblRows.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // lblSpacing
            // 
            this.lblSpacing.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.lblSpacing.BackColor = System.Drawing.Color.Transparent;
            this.lblSpacing.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblSpacing.ForeColor = System.Drawing.Color.White;
            this.lblSpacing.Location = new System.Drawing.Point(941, 444);
            this.lblSpacing.Name = "lblSpacing";
            this.lblSpacing.Size = new System.Drawing.Size(132, 30);
            this.lblSpacing.TabIndex = 19;
            this.lblSpacing.Text = "Spacing";
            this.lblSpacing.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // buttonSpacing
            // 
            this.buttonSpacing.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonSpacing.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.buttonSpacing.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.buttonSpacing.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.buttonSpacing.ForeColor = System.Drawing.Color.White;
            this.buttonSpacing.Location = new System.Drawing.Point(1079, 444);
            this.buttonSpacing.Maximum = new decimal(new int[] {
            25,
            0,
            0,
            0});
            this.buttonSpacing.Name = "buttonSpacing";
            this.buttonSpacing.Size = new System.Drawing.Size(55, 30);
            this.buttonSpacing.TabIndex = 18;
            this.buttonSpacing.Value = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.buttonSpacing.ValueChanged += new System.EventHandler(this.ButtonSettingsChanged);
            // 
            // lblCornerRadius
            // 
            this.lblCornerRadius.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.lblCornerRadius.BackColor = System.Drawing.Color.Transparent;
            this.lblCornerRadius.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblCornerRadius.ForeColor = System.Drawing.Color.White;
            this.lblCornerRadius.Location = new System.Drawing.Point(941, 480);
            this.lblCornerRadius.Name = "lblCornerRadius";
            this.lblCornerRadius.Size = new System.Drawing.Size(132, 30);
            this.lblCornerRadius.TabIndex = 21;
            this.lblCornerRadius.Text = "Corner radius";
            this.lblCornerRadius.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // cornerRadius
            // 
            this.cornerRadius.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.cornerRadius.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.cornerRadius.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.cornerRadius.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.cornerRadius.ForeColor = System.Drawing.Color.White;
            this.cornerRadius.Location = new System.Drawing.Point(1079, 480);
            this.cornerRadius.Name = "cornerRadius";
            this.cornerRadius.Size = new System.Drawing.Size(55, 30);
            this.cornerRadius.TabIndex = 20;
            this.cornerRadius.Value = new decimal(new int[] {
            40,
            0,
            0,
            0});
            this.cornerRadius.ValueChanged += new System.EventHandler(this.ButtonSettingsChanged);
            // 
            // checkButtonBackground
            // 
            this.checkButtonBackground.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.checkButtonBackground.AutoSize = true;
            this.checkButtonBackground.BackColor = System.Drawing.Color.Transparent;
            this.checkButtonBackground.CheckAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.checkButtonBackground.Checked = true;
            this.checkButtonBackground.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkButtonBackground.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.checkButtonBackground.ForeColor = System.Drawing.Color.White;
            this.checkButtonBackground.Location = new System.Drawing.Point(983, 513);
            this.checkButtonBackground.Name = "checkButtonBackground";
            this.checkButtonBackground.Size = new System.Drawing.Size(151, 22);
            this.checkButtonBackground.TabIndex = 22;
            this.checkButtonBackground.Text = "Button Background";
            this.checkButtonBackground.UseVisualStyleBackColor = false;
            this.checkButtonBackground.CheckedChanged += new System.EventHandler(this.ButtonSettingsChanged);
            // 
            // btnEditProfile
            // 
            this.btnEditProfile.BackColor = System.Drawing.Color.Transparent;
            this.btnEditProfile.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Edit_Normal;
            this.btnEditProfile.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnEditProfile.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnEditProfile.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnEditProfile.ForeColor = System.Drawing.Color.White;
            this.btnEditProfile.HoverImage = global::SuchByte.MacroDeck.Properties.Resources.Edit_Hover;
            this.btnEditProfile.Location = new System.Drawing.Point(559, 6);
            this.btnEditProfile.Name = "btnEditProfile";
            this.btnEditProfile.Size = new System.Drawing.Size(25, 25);
            this.btnEditProfile.TabIndex = 23;
            this.btnEditProfile.TabStop = false;
            this.btnEditProfile.Click += new System.EventHandler(this.BtnEditProfile_Click);
            // 
            // panel1
            // 
            this.panel1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.panel1.Controls.Add(this.boxProfiles);
            this.panel1.Controls.Add(this.btnAddProfile);
            this.panel1.Controls.Add(this.btnDeleteProfile);
            this.panel1.Controls.Add(this.btnEditProfile);
            this.panel1.Location = new System.Drawing.Point(6, 3);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(852, 36);
            this.panel1.TabIndex = 24;
            // 
            // actionButtonContextMenuItemSimulatePress
            // 
            this.actionButtonContextMenuItemSimulatePress.ForeColor = System.Drawing.Color.White;
            this.actionButtonContextMenuItemSimulatePress.Name = "actionButtonContextMenuItemSimulatePress";
            this.actionButtonContextMenuItemSimulatePress.Size = new System.Drawing.Size(330, 28);
            this.actionButtonContextMenuItemSimulatePress.Text = "Simulate \"On press\"";
            this.actionButtonContextMenuItemSimulatePress.Click += new System.EventHandler(this.ActionButtonContextMenuItemSimulatePress_Click);
            // 
            // actionButtonContextMenuItemSimulateRelease
            // 
            this.actionButtonContextMenuItemSimulateRelease.ForeColor = System.Drawing.Color.White;
            this.actionButtonContextMenuItemSimulateRelease.Name = "actionButtonContextMenuItemSimulateRelease";
            this.actionButtonContextMenuItemSimulateRelease.Size = new System.Drawing.Size(330, 28);
            this.actionButtonContextMenuItemSimulateRelease.Text = "Simulate \"On release\"";
            this.actionButtonContextMenuItemSimulateRelease.Click += new System.EventHandler(this.ActionButtonContextMenuItemSimulateRelease_Click);
            // 
            // actionButtonContextMenuItemSimulateLongPress
            // 
            this.actionButtonContextMenuItemSimulateLongPress.ForeColor = System.Drawing.Color.White;
            this.actionButtonContextMenuItemSimulateLongPress.Name = "actionButtonContextMenuItemSimulateLongPress";
            this.actionButtonContextMenuItemSimulateLongPress.Size = new System.Drawing.Size(330, 28);
            this.actionButtonContextMenuItemSimulateLongPress.Text = "Simulate \"On long press\"";
            this.actionButtonContextMenuItemSimulateLongPress.Click += new System.EventHandler(this.ActionButtonContextMenuItemSimulateLongPress_Click);
            // 
            // actionButtonContextMenuItemSimulateLongPressRelease
            // 
            this.actionButtonContextMenuItemSimulateLongPressRelease.ForeColor = System.Drawing.Color.White;
            this.actionButtonContextMenuItemSimulateLongPressRelease.Name = "actionButtonContextMenuItemSimulateLongPressRelease";
            this.actionButtonContextMenuItemSimulateLongPressRelease.Size = new System.Drawing.Size(330, 28);
            this.actionButtonContextMenuItemSimulateLongPressRelease.Text = "Simulate \"On long press release\"";
            this.actionButtonContextMenuItemSimulateLongPressRelease.Click += new System.EventHandler(this.ActionButtonContextMenuItemSimulateLongPressRelease_Click);
            // 
            // toolStripSeparator3
            // 
            this.toolStripSeparator3.Name = "toolStripSeparator3";
            this.toolStripSeparator3.Size = new System.Drawing.Size(327, 6);
            // 
            // DeckView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
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
            this.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.Name = "DeckView";
            this.Size = new System.Drawing.Size(1137, 540);
            this.Load += new System.EventHandler(this.Deck_Load);
            this.foldersContextMenu.ResumeLayout(false);
            this.actionButtonContextMenu.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.btnAddProfile)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnDeleteProfile)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.buttonColumns)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.buttonRows)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.buttonSpacing)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.cornerRadius)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnEditProfile)).EndInit();
            this.panel1.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.TreeView foldersView;
        private BufferedPanel buttonPanel;
        private System.Windows.Forms.ContextMenuStrip actionButtonContextMenu;
        private System.Windows.Forms.ToolStripMenuItem actionButtonContextMenuItemEdit;
        private System.Windows.Forms.ToolStripMenuItem actionButtonContextMenuItemDelete;
        private System.Windows.Forms.ContextMenuStrip foldersContextMenu;
        private System.Windows.Forms.ToolStripMenuItem foldersContextMenuEdit;
        private System.Windows.Forms.ToolStripMenuItem foldersContextMenuDelete;
        private System.Windows.Forms.ToolStripMenuItem foldersContextMenuNew;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
        private System.Windows.Forms.ToolStripMenuItem actionButtonContextMenuItemCopy;
        private System.Windows.Forms.ToolStripMenuItem actionButtonContextMenuItemPaste;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private RoundedComboBox boxProfiles;
        private PictureButton btnAddProfile;
        private PictureButton btnDeleteProfile;
        private System.Windows.Forms.NumericUpDown buttonColumns;
        private System.Windows.Forms.NumericUpDown buttonRows;
        private System.Windows.Forms.Label lblColumns;
        private System.Windows.Forms.Label lblRows;
        private System.Windows.Forms.Label lblSpacing;
        private System.Windows.Forms.NumericUpDown buttonSpacing;
        private System.Windows.Forms.Label lblCornerRadius;
        private System.Windows.Forms.NumericUpDown cornerRadius;
        private System.Windows.Forms.CheckBox checkButtonBackground;
        private PictureButton btnEditProfile;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.ToolStripMenuItem actionButtonContextMenuItemSimulatePress;
        private System.Windows.Forms.ToolStripMenuItem actionButtonContextMenuItemSimulateRelease;
        private System.Windows.Forms.ToolStripMenuItem actionButtonContextMenuItemSimulateLongPress;
        private System.Windows.Forms.ToolStripMenuItem actionButtonContextMenuItemSimulateLongPressRelease;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator3;
    }
}
