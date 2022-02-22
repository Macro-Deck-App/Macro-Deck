using SuchByte.MacroDeck.GUI.CustomControls;

namespace SuchByte.MacroDeck.GUI
{
    partial class IconSelector
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            foreach (RoundedButton roundedButton in this.iconList.Controls)
            {
                roundedButton.Dispose();
            }
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.iconList = new System.Windows.Forms.FlowLayoutPanel();
            this.btnImport = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.btnPreview = new SuchByte.MacroDeck.GUI.CustomControls.RoundedButton();
            this.btnOk = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.iconPacksBox = new SuchByte.MacroDeck.GUI.CustomControls.RoundedComboBox();
            this.btnCreateIconPack = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.btnDeleteIconPack = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.btnImportIconPack = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.btnExportIconPack = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.btnDeleteIcon = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.lblSizeLabel = new System.Windows.Forms.Label();
            this.lblSize = new System.Windows.Forms.Label();
            this.lblType = new System.Windows.Forms.Label();
            this.lblTypeLabel = new System.Windows.Forms.Label();
            this.btnCreateIcon = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.btnGiphy = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.panelCreateIcon = new System.Windows.Forms.FlowLayoutPanel();
            this.lblManaged = new System.Windows.Forms.Label();
            this.btnGenerateStatic = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            ((System.ComponentModel.ISupportInitialize)(this.btnPreview)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnCreateIconPack)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnDeleteIconPack)).BeginInit();
            this.panelCreateIcon.SuspendLayout();
            this.SuspendLayout();
            // 
            // iconList
            // 
            this.iconList.AutoScroll = true;
            this.iconList.Location = new System.Drawing.Point(12, 40);
            this.iconList.Name = "iconList";
            this.iconList.Size = new System.Drawing.Size(760, 397);
            this.iconList.TabIndex = 0;
            // 
            // btnImport
            // 
            this.btnImport.BorderRadius = 8;
            this.btnImport.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnImport.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnImport.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnImport.ForeColor = System.Drawing.Color.White;
            this.btnImport.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnImport.Icon = null;
            this.btnImport.Location = new System.Drawing.Point(3, 3);
            this.btnImport.Name = "btnImport";
            this.btnImport.Progress = 0;
            this.btnImport.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnImport.Size = new System.Drawing.Size(115, 25);
            this.btnImport.TabIndex = 1;
            this.btnImport.Text = "Import icon";
            this.btnImport.UseMnemonic = false;
            this.btnImport.UseVisualStyleBackColor = true;
            this.btnImport.UseWindowsAccentColor = true;
            this.btnImport.Click += new System.EventHandler(this.BtnImport_Click);
            // 
            // btnPreview
            // 
            this.btnPreview.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.btnPreview.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnPreview.Column = 0;
            this.btnPreview.ForegroundImage = null;
            this.btnPreview.Location = new System.Drawing.Point(12, 443);
            this.btnPreview.Name = "btnPreview";
            this.btnPreview.Radius = 40;
            this.btnPreview.Row = 0;
            this.btnPreview.ShowGIFIndicator = false;
            this.btnPreview.ShowKeyboardHotkeyIndicator = false;
            this.btnPreview.Size = new System.Drawing.Size(150, 150);
            this.btnPreview.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.btnPreview.TabIndex = 5;
            this.btnPreview.TabStop = false;
            // 
            // btnOk
            // 
            this.btnOk.BorderRadius = 8;
            this.btnOk.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnOk.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOk.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnOk.ForeColor = System.Drawing.Color.White;
            this.btnOk.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnOk.Icon = null;
            this.btnOk.Location = new System.Drawing.Point(700, 570);
            this.btnOk.Name = "btnOk";
            this.btnOk.Progress = 0;
            this.btnOk.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnOk.Size = new System.Drawing.Size(75, 25);
            this.btnOk.TabIndex = 6;
            this.btnOk.Text = "Ok";
            this.btnOk.UseMnemonic = false;
            this.btnOk.UseVisualStyleBackColor = true;
            this.btnOk.UseWindowsAccentColor = true;
            this.btnOk.Click += new System.EventHandler(this.BtnOk_Click);
            // 
            // iconPacksBox
            // 
            this.iconPacksBox.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.iconPacksBox.Cursor = System.Windows.Forms.Cursors.Hand;
            this.iconPacksBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.iconPacksBox.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.iconPacksBox.ForeColor = System.Drawing.Color.White;
            this.iconPacksBox.Icon = null;
            this.iconPacksBox.Location = new System.Drawing.Point(12, 9);
            this.iconPacksBox.Name = "iconPacksBox";
            this.iconPacksBox.Padding = new System.Windows.Forms.Padding(8, 2, 8, 2);
            this.iconPacksBox.SelectedIndex = -1;
            this.iconPacksBox.SelectedItem = null;
            this.iconPacksBox.Size = new System.Drawing.Size(320, 26);
            this.iconPacksBox.TabIndex = 7;
            this.iconPacksBox.SelectedIndexChanged += new System.EventHandler(this.IconPacksBox_SelectedIndexChanged);
            // 
            // btnCreateIconPack
            // 
            this.btnCreateIconPack.BackColor = System.Drawing.Color.Transparent;
            this.btnCreateIconPack.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Create_Normal;
            this.btnCreateIconPack.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnCreateIconPack.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnCreateIconPack.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnCreateIconPack.ForeColor = System.Drawing.Color.White;
            this.btnCreateIconPack.HoverImage = global::SuchByte.MacroDeck.Properties.Resources.Create_Hover;
            this.btnCreateIconPack.Location = new System.Drawing.Point(333, 9);
            this.btnCreateIconPack.Name = "btnCreateIconPack";
            this.btnCreateIconPack.Size = new System.Drawing.Size(25, 25);
            this.btnCreateIconPack.TabIndex = 9;
            this.btnCreateIconPack.TabStop = false;
            this.btnCreateIconPack.Click += new System.EventHandler(this.btnCreateIconPack_Click);
            // 
            // btnDeleteIconPack
            // 
            this.btnDeleteIconPack.BackColor = System.Drawing.Color.Transparent;
            this.btnDeleteIconPack.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Delete_Normal;
            this.btnDeleteIconPack.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnDeleteIconPack.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnDeleteIconPack.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnDeleteIconPack.ForeColor = System.Drawing.Color.White;
            this.btnDeleteIconPack.HoverImage = global::SuchByte.MacroDeck.Properties.Resources.Delete_Hover;
            this.btnDeleteIconPack.Location = new System.Drawing.Point(361, 9);
            this.btnDeleteIconPack.Name = "btnDeleteIconPack";
            this.btnDeleteIconPack.Size = new System.Drawing.Size(25, 25);
            this.btnDeleteIconPack.TabIndex = 10;
            this.btnDeleteIconPack.TabStop = false;
            this.btnDeleteIconPack.Click += new System.EventHandler(this.btnDeleteIconPack_Click);
            // 
            // btnImportIconPack
            // 
            this.btnImportIconPack.BorderRadius = 8;
            this.btnImportIconPack.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnImportIconPack.FlatAppearance.BorderSize = 0;
            this.btnImportIconPack.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnImportIconPack.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnImportIconPack.ForeColor = System.Drawing.Color.White;
            this.btnImportIconPack.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnImportIconPack.Icon = null;
            this.btnImportIconPack.Location = new System.Drawing.Point(417, 10);
            this.btnImportIconPack.Name = "btnImportIconPack";
            this.btnImportIconPack.Progress = 0;
            this.btnImportIconPack.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnImportIconPack.Size = new System.Drawing.Size(130, 23);
            this.btnImportIconPack.TabIndex = 11;
            this.btnImportIconPack.Text = "Import icon pack...";
            this.btnImportIconPack.UseMnemonic = false;
            this.btnImportIconPack.UseVisualStyleBackColor = false;
            this.btnImportIconPack.UseWindowsAccentColor = true;
            this.btnImportIconPack.Click += new System.EventHandler(this.BtnImportIconPack_Click);
            // 
            // btnExportIconPack
            // 
            this.btnExportIconPack.BorderRadius = 8;
            this.btnExportIconPack.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnExportIconPack.FlatAppearance.BorderSize = 0;
            this.btnExportIconPack.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnExportIconPack.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnExportIconPack.ForeColor = System.Drawing.Color.White;
            this.btnExportIconPack.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnExportIconPack.Icon = null;
            this.btnExportIconPack.Location = new System.Drawing.Point(553, 11);
            this.btnExportIconPack.Name = "btnExportIconPack";
            this.btnExportIconPack.Progress = 0;
            this.btnExportIconPack.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnExportIconPack.Size = new System.Drawing.Size(130, 23);
            this.btnExportIconPack.TabIndex = 12;
            this.btnExportIconPack.Text = "Export icon pack...";
            this.btnExportIconPack.UseMnemonic = false;
            this.btnExportIconPack.UseVisualStyleBackColor = false;
            this.btnExportIconPack.UseWindowsAccentColor = true;
            this.btnExportIconPack.Visible = false;
            this.btnExportIconPack.Click += new System.EventHandler(this.BtnExportIconPack_Click);
            // 
            // btnDeleteIcon
            // 
            this.btnDeleteIcon.BorderRadius = 8;
            this.btnDeleteIcon.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnDeleteIcon.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnDeleteIcon.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnDeleteIcon.ForeColor = System.Drawing.Color.White;
            this.btnDeleteIcon.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(130)))), ((int)(((byte)(0)))), ((int)(((byte)(0)))));
            this.btnDeleteIcon.Icon = null;
            this.btnDeleteIcon.Location = new System.Drawing.Point(667, 443);
            this.btnDeleteIcon.Name = "btnDeleteIcon";
            this.btnDeleteIcon.Progress = 0;
            this.btnDeleteIcon.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnDeleteIcon.Size = new System.Drawing.Size(105, 25);
            this.btnDeleteIcon.TabIndex = 13;
            this.btnDeleteIcon.Text = "Delete icon";
            this.btnDeleteIcon.UseMnemonic = false;
            this.btnDeleteIcon.UseVisualStyleBackColor = false;
            this.btnDeleteIcon.UseWindowsAccentColor = false;
            this.btnDeleteIcon.Click += new System.EventHandler(this.BtnDeleteIcon_Click);
            // 
            // lblSizeLabel
            // 
            this.lblSizeLabel.AutoSize = true;
            this.lblSizeLabel.Location = new System.Drawing.Point(168, 484);
            this.lblSizeLabel.Name = "lblSizeLabel";
            this.lblSizeLabel.Size = new System.Drawing.Size(36, 16);
            this.lblSizeLabel.TabIndex = 14;
            this.lblSizeLabel.Text = "Size:";
            this.lblSizeLabel.UseMnemonic = false;
            // 
            // lblSize
            // 
            this.lblSize.Location = new System.Drawing.Point(214, 485);
            this.lblSize.Name = "lblSize";
            this.lblSize.Size = new System.Drawing.Size(235, 15);
            this.lblSize.TabIndex = 15;
            this.lblSize.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblSize.UseMnemonic = false;
            // 
            // lblType
            // 
            this.lblType.Location = new System.Drawing.Point(214, 504);
            this.lblType.Name = "lblType";
            this.lblType.Size = new System.Drawing.Size(46, 16);
            this.lblType.TabIndex = 17;
            this.lblType.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblType.UseMnemonic = false;
            // 
            // lblTypeLabel
            // 
            this.lblTypeLabel.AutoSize = true;
            this.lblTypeLabel.Location = new System.Drawing.Point(168, 504);
            this.lblTypeLabel.Name = "lblTypeLabel";
            this.lblTypeLabel.Size = new System.Drawing.Size(40, 16);
            this.lblTypeLabel.TabIndex = 16;
            this.lblTypeLabel.Text = "Type:";
            this.lblTypeLabel.UseMnemonic = false;
            // 
            // btnCreateIcon
            // 
            this.btnCreateIcon.BorderRadius = 8;
            this.btnCreateIcon.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnCreateIcon.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCreateIcon.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnCreateIcon.ForeColor = System.Drawing.Color.White;
            this.btnCreateIcon.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnCreateIcon.Icon = null;
            this.btnCreateIcon.Location = new System.Drawing.Point(124, 3);
            this.btnCreateIcon.Name = "btnCreateIcon";
            this.btnCreateIcon.Progress = 0;
            this.btnCreateIcon.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnCreateIcon.Size = new System.Drawing.Size(115, 25);
            this.btnCreateIcon.TabIndex = 18;
            this.btnCreateIcon.Text = "Create icon";
            this.btnCreateIcon.UseMnemonic = false;
            this.btnCreateIcon.UseVisualStyleBackColor = true;
            this.btnCreateIcon.UseWindowsAccentColor = true;
            this.btnCreateIcon.Click += new System.EventHandler(this.BtnCreateIcon_Click);
            // 
            // btnGiphy
            // 
            this.btnGiphy.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnGiphy.BorderRadius = 8;
            this.btnGiphy.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnGiphy.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnGiphy.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnGiphy.ForeColor = System.Drawing.Color.White;
            this.btnGiphy.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnGiphy.Icon = global::SuchByte.MacroDeck.Properties.Resources.giphy1;
            this.btnGiphy.Location = new System.Drawing.Point(245, 3);
            this.btnGiphy.Name = "btnGiphy";
            this.btnGiphy.Progress = 0;
            this.btnGiphy.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnGiphy.Size = new System.Drawing.Size(78, 25);
            this.btnGiphy.TabIndex = 20;
            this.btnGiphy.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.btnGiphy.UseMnemonic = false;
            this.btnGiphy.UseVisualStyleBackColor = true;
            this.btnGiphy.UseWindowsAccentColor = true;
            this.btnGiphy.Visible = false;
            this.btnGiphy.Click += new System.EventHandler(this.BtnGiphy_Click);
            // 
            // panelCreateIcon
            // 
            this.panelCreateIcon.Controls.Add(this.btnImport);
            this.panelCreateIcon.Controls.Add(this.btnCreateIcon);
            this.panelCreateIcon.Controls.Add(this.btnGiphy);
            this.panelCreateIcon.Controls.Add(this.lblManaged);
            this.panelCreateIcon.Location = new System.Drawing.Point(168, 443);
            this.panelCreateIcon.Name = "panelCreateIcon";
            this.panelCreateIcon.Size = new System.Drawing.Size(493, 38);
            this.panelCreateIcon.TabIndex = 21;
            // 
            // lblManaged
            // 
            this.lblManaged.AutoSize = true;
            this.lblManaged.Location = new System.Drawing.Point(3, 31);
            this.lblManaged.Name = "lblManaged";
            this.lblManaged.Size = new System.Drawing.Size(290, 16);
            this.lblManaged.TabIndex = 21;
            this.lblManaged.Text = "This icon pack is managed by the plugin manager";
            this.lblManaged.UseMnemonic = false;
            this.lblManaged.Visible = false;
            // 
            // btnGenerateStatic
            // 
            this.btnGenerateStatic.BorderRadius = 8;
            this.btnGenerateStatic.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnGenerateStatic.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnGenerateStatic.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnGenerateStatic.ForeColor = System.Drawing.Color.White;
            this.btnGenerateStatic.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnGenerateStatic.Icon = null;
            this.btnGenerateStatic.Location = new System.Drawing.Point(266, 500);
            this.btnGenerateStatic.Name = "btnGenerateStatic";
            this.btnGenerateStatic.Progress = 0;
            this.btnGenerateStatic.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnGenerateStatic.Size = new System.Drawing.Size(145, 25);
            this.btnGenerateStatic.TabIndex = 22;
            this.btnGenerateStatic.Text = "Generate static";
            this.btnGenerateStatic.UseMnemonic = false;
            this.btnGenerateStatic.UseVisualStyleBackColor = true;
            this.btnGenerateStatic.UseWindowsAccentColor = true;
            this.btnGenerateStatic.Visible = false;
            this.btnGenerateStatic.Click += new System.EventHandler(this.BtnGenerateStatic_Click);
            // 
            // IconSelector
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.ClientSize = new System.Drawing.Size(787, 606);
            this.Controls.Add(this.panelCreateIcon);
            this.Controls.Add(this.lblType);
            this.Controls.Add(this.lblTypeLabel);
            this.Controls.Add(this.btnGenerateStatic);
            this.Controls.Add(this.lblSize);
            this.Controls.Add(this.lblSizeLabel);
            this.Controls.Add(this.btnDeleteIcon);
            this.Controls.Add(this.btnExportIconPack);
            this.Controls.Add(this.btnImportIconPack);
            this.Controls.Add(this.btnDeleteIconPack);
            this.Controls.Add(this.btnCreateIconPack);
            this.Controls.Add(this.iconPacksBox);
            this.Controls.Add(this.btnOk);
            this.Controls.Add(this.btnPreview);
            this.Controls.Add(this.iconList);
            this.Location = new System.Drawing.Point(0, 0);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "IconSelector";
            this.Text = "Macro Deck :: Icon selector";
            this.Load += new System.EventHandler(this.IconSelector_Load);
            this.Controls.SetChildIndex(this.iconList, 0);
            this.Controls.SetChildIndex(this.btnPreview, 0);
            this.Controls.SetChildIndex(this.btnOk, 0);
            this.Controls.SetChildIndex(this.iconPacksBox, 0);
            this.Controls.SetChildIndex(this.btnCreateIconPack, 0);
            this.Controls.SetChildIndex(this.btnDeleteIconPack, 0);
            this.Controls.SetChildIndex(this.btnImportIconPack, 0);
            this.Controls.SetChildIndex(this.btnExportIconPack, 0);
            this.Controls.SetChildIndex(this.btnDeleteIcon, 0);
            this.Controls.SetChildIndex(this.lblSizeLabel, 0);
            this.Controls.SetChildIndex(this.lblSize, 0);
            this.Controls.SetChildIndex(this.btnGenerateStatic, 0);
            this.Controls.SetChildIndex(this.lblTypeLabel, 0);
            this.Controls.SetChildIndex(this.lblType, 0);
            this.Controls.SetChildIndex(this.panelCreateIcon, 0);
            ((System.ComponentModel.ISupportInitialize)(this.btnPreview)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnCreateIconPack)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnDeleteIconPack)).EndInit();
            this.panelCreateIcon.ResumeLayout(false);
            this.panelCreateIcon.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.FlowLayoutPanel iconList;
        private CustomControls.ButtonPrimary btnImport;
        private RoundedButton btnPreview;
        private CustomControls.ButtonPrimary btnOk;
        private RoundedComboBox iconPacksBox;
        private PictureButton btnCreateIconPack;
        private PictureButton btnDeleteIconPack;

        private ButtonPrimary btnImportIconPack;
        private ButtonPrimary btnExportIconPack;
        private ButtonPrimary btnDeleteIcon;
        private System.Windows.Forms.Label lblSizeLabel;
        private System.Windows.Forms.Label lblSize;
        private System.Windows.Forms.Label lblType;
        private System.Windows.Forms.Label lblTypeLabel;
        private ButtonPrimary btnCreateIcon;
        private ButtonPrimary btnGiphy;
        private System.Windows.Forms.FlowLayoutPanel panelCreateIcon;
        private System.Windows.Forms.Label lblManaged;
        private ButtonPrimary btnGenerateStatic;
    }
}