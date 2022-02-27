namespace SuchByte.MacroDeck.GUI.CustomControls.ExtensionsView
{
    partial class ExtensionZipInstallerView
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
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.btnClose = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.dlgSelectPluginFile = new System.Windows.Forms.OpenFileDialog();
            this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            this.txtZipPath = new SuchByte.MacroDeck.GUI.CustomControls.RoundedTextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.lblPackageId = new System.Windows.Forms.Label();
            this.lblZipPath = new System.Windows.Forms.Label();
            this.txtMainFile = new SuchByte.MacroDeck.GUI.CustomControls.RoundedTextBox();
            this.txtAuthor = new SuchByte.MacroDeck.GUI.CustomControls.RoundedTextBox();
            this.txtPackageId = new SuchByte.MacroDeck.GUI.CustomControls.RoundedTextBox();
            this.btnInstall = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.btnSelectFile = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.roundedTextBox1 = new SuchByte.MacroDeck.GUI.CustomControls.RoundedTextBox();
            ((System.ComponentModel.ISupportInitialize)(this.btnClose)).BeginInit();
            this.tableLayoutPanel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // btnClose
            // 
            this.btnClose.BackColor = System.Drawing.Color.Transparent;
            this.btnClose.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Close_Normal;
            this.btnClose.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnClose.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnClose.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.btnClose.ForeColor = System.Drawing.Color.White;
            this.btnClose.HoverImage = global::SuchByte.MacroDeck.Properties.Resources.Close_Hover;
            this.btnClose.Location = new System.Drawing.Point(10, 7);
            this.btnClose.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.btnClose.Name = "btnClose";
            this.btnClose.Size = new System.Drawing.Size(25, 25);
            this.btnClose.TabIndex = 3;
            this.btnClose.TabStop = false;
            this.btnClose.Click += new System.EventHandler(this.btnClose_Click);
            // 
            // dlgSelectPluginFile
            // 
            this.dlgSelectPluginFile.DefaultExt = "zip";
            this.dlgSelectPluginFile.FileName = "openFileDialog1";
            this.dlgSelectPluginFile.Filter = "ZIP Archives|*.zip";
            // 
            // tableLayoutPanel1
            // 
            this.tableLayoutPanel1.Anchor = System.Windows.Forms.AnchorStyles.Top;
            this.tableLayoutPanel1.ColumnCount = 2;
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.tableLayoutPanel1.Controls.Add(this.txtZipPath, 1, 0);
            this.tableLayoutPanel1.Controls.Add(this.label3, 0, 3);
            this.tableLayoutPanel1.Controls.Add(this.label1, 0, 2);
            this.tableLayoutPanel1.Controls.Add(this.lblPackageId, 0, 1);
            this.tableLayoutPanel1.Controls.Add(this.lblZipPath, 0, 0);
            this.tableLayoutPanel1.Controls.Add(this.txtMainFile, 1, 3);
            this.tableLayoutPanel1.Controls.Add(this.txtAuthor, 1, 2);
            this.tableLayoutPanel1.Controls.Add(this.txtPackageId, 1, 1);
            this.tableLayoutPanel1.Location = new System.Drawing.Point(16, 82);
            this.tableLayoutPanel1.Margin = new System.Windows.Forms.Padding(0);
            this.tableLayoutPanel1.Name = "tableLayoutPanel1";
            this.tableLayoutPanel1.RowCount = 4;
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.tableLayoutPanel1.Size = new System.Drawing.Size(511, 126);
            this.tableLayoutPanel1.TabIndex = 4;
            // 
            // txtZipPath
            // 
            this.txtZipPath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtZipPath.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.txtZipPath.Cursor = System.Windows.Forms.Cursors.Hand;
            this.txtZipPath.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.txtZipPath.Icon = null;
            this.txtZipPath.Location = new System.Drawing.Point(87, 3);
            this.txtZipPath.MaxCharacters = 32767;
            this.txtZipPath.Multiline = false;
            this.txtZipPath.Name = "txtZipPath";
            this.txtZipPath.Padding = new System.Windows.Forms.Padding(8, 5, 8, 5);
            this.txtZipPath.PasswordChar = false;
            this.txtZipPath.PlaceHolderColor = System.Drawing.Color.Gray;
            this.txtZipPath.PlaceHolderText = "";
            this.txtZipPath.ReadOnly = true;
            this.txtZipPath.ScrollBars = System.Windows.Forms.ScrollBars.None;
            this.txtZipPath.SelectionStart = 0;
            this.txtZipPath.Size = new System.Drawing.Size(421, 25);
            this.txtZipPath.TabIndex = 10;
            this.txtZipPath.TextAlignment = System.Windows.Forms.HorizontalAlignment.Left;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.label3.ForeColor = System.Drawing.Color.White;
            this.label3.Location = new System.Drawing.Point(6, 99);
            this.label3.Margin = new System.Windows.Forms.Padding(6);
            this.label3.Name = "label3";
            this.label3.Padding = new System.Windows.Forms.Padding(2);
            this.label3.Size = new System.Drawing.Size(62, 19);
            this.label3.TabIndex = 5;
            this.label3.Text = "Main File:";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.label1.ForeColor = System.Drawing.Color.White;
            this.label1.Location = new System.Drawing.Point(6, 68);
            this.label1.Margin = new System.Windows.Forms.Padding(6);
            this.label1.Name = "label1";
            this.label1.Padding = new System.Windows.Forms.Padding(2);
            this.label1.Size = new System.Drawing.Size(51, 19);
            this.label1.TabIndex = 2;
            this.label1.Text = "Author:";
            // 
            // lblPackageId
            // 
            this.lblPackageId.AutoSize = true;
            this.lblPackageId.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblPackageId.ForeColor = System.Drawing.Color.White;
            this.lblPackageId.Location = new System.Drawing.Point(6, 37);
            this.lblPackageId.Margin = new System.Windows.Forms.Padding(6);
            this.lblPackageId.Name = "lblPackageId";
            this.lblPackageId.Padding = new System.Windows.Forms.Padding(2);
            this.lblPackageId.Size = new System.Drawing.Size(72, 19);
            this.lblPackageId.TabIndex = 1;
            this.lblPackageId.Text = "Package ID:";
            // 
            // lblZipPath
            // 
            this.lblZipPath.AutoSize = true;
            this.lblZipPath.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblZipPath.ForeColor = System.Drawing.Color.White;
            this.lblZipPath.Location = new System.Drawing.Point(6, 6);
            this.lblZipPath.Margin = new System.Windows.Forms.Padding(6);
            this.lblZipPath.Name = "lblZipPath";
            this.lblZipPath.Padding = new System.Windows.Forms.Padding(2);
            this.lblZipPath.Size = new System.Drawing.Size(58, 19);
            this.lblZipPath.TabIndex = 8;
            this.lblZipPath.Text = "Zip Path:";
            // 
            // txtMainFile
            // 
            this.txtMainFile.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtMainFile.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.txtMainFile.Cursor = System.Windows.Forms.Cursors.Hand;
            this.txtMainFile.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.txtMainFile.Icon = null;
            this.txtMainFile.Location = new System.Drawing.Point(87, 96);
            this.txtMainFile.MaxCharacters = 32767;
            this.txtMainFile.Multiline = false;
            this.txtMainFile.Name = "txtMainFile";
            this.txtMainFile.Padding = new System.Windows.Forms.Padding(8, 5, 8, 5);
            this.txtMainFile.PasswordChar = false;
            this.txtMainFile.PlaceHolderColor = System.Drawing.Color.Gray;
            this.txtMainFile.PlaceHolderText = "";
            this.txtMainFile.ReadOnly = true;
            this.txtMainFile.ScrollBars = System.Windows.Forms.ScrollBars.None;
            this.txtMainFile.SelectionStart = 0;
            this.txtMainFile.Size = new System.Drawing.Size(421, 25);
            this.txtMainFile.TabIndex = 7;
            this.txtMainFile.TextAlignment = System.Windows.Forms.HorizontalAlignment.Left;
            // 
            // txtAuthor
            // 
            this.txtAuthor.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtAuthor.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.txtAuthor.Cursor = System.Windows.Forms.Cursors.Hand;
            this.txtAuthor.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.txtAuthor.Icon = null;
            this.txtAuthor.Location = new System.Drawing.Point(87, 65);
            this.txtAuthor.MaxCharacters = 32767;
            this.txtAuthor.Multiline = false;
            this.txtAuthor.Name = "txtAuthor";
            this.txtAuthor.Padding = new System.Windows.Forms.Padding(8, 5, 8, 5);
            this.txtAuthor.PasswordChar = false;
            this.txtAuthor.PlaceHolderColor = System.Drawing.Color.Gray;
            this.txtAuthor.PlaceHolderText = "";
            this.txtAuthor.ReadOnly = true;
            this.txtAuthor.ScrollBars = System.Windows.Forms.ScrollBars.None;
            this.txtAuthor.SelectionStart = 0;
            this.txtAuthor.Size = new System.Drawing.Size(421, 25);
            this.txtAuthor.TabIndex = 6;
            this.txtAuthor.TextAlignment = System.Windows.Forms.HorizontalAlignment.Left;
            // 
            // txtPackageId
            // 
            this.txtPackageId.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtPackageId.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.txtPackageId.Cursor = System.Windows.Forms.Cursors.Hand;
            this.txtPackageId.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.txtPackageId.Icon = null;
            this.txtPackageId.Location = new System.Drawing.Point(87, 34);
            this.txtPackageId.MaxCharacters = 32767;
            this.txtPackageId.Multiline = false;
            this.txtPackageId.Name = "txtPackageId";
            this.txtPackageId.Padding = new System.Windows.Forms.Padding(8, 5, 8, 5);
            this.txtPackageId.PasswordChar = false;
            this.txtPackageId.PlaceHolderColor = System.Drawing.Color.Gray;
            this.txtPackageId.PlaceHolderText = "";
            this.txtPackageId.ReadOnly = true;
            this.txtPackageId.ScrollBars = System.Windows.Forms.ScrollBars.None;
            this.txtPackageId.SelectionStart = 0;
            this.txtPackageId.Size = new System.Drawing.Size(421, 25);
            this.txtPackageId.TabIndex = 4;
            this.txtPackageId.TextAlignment = System.Windows.Forms.HorizontalAlignment.Left;
            // 
            // btnInstall
            // 
            this.btnInstall.Anchor = System.Windows.Forms.AnchorStyles.Top;
            this.btnInstall.BorderRadius = 8;
            this.btnInstall.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnInstall.Enabled = false;
            this.btnInstall.FlatAppearance.BorderSize = 0;
            this.btnInstall.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnInstall.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnInstall.ForeColor = System.Drawing.Color.White;
            this.btnInstall.HoverColor = System.Drawing.Color.Empty;
            this.btnInstall.Icon = null;
            this.btnInstall.Location = new System.Drawing.Point(377, 258);
            this.btnInstall.Name = "btnInstall";
            this.btnInstall.Progress = 0;
            this.btnInstall.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(103)))), ((int)(((byte)(225)))));
            this.btnInstall.Size = new System.Drawing.Size(150, 40);
            this.btnInstall.TabIndex = 5;
            this.btnInstall.Text = "Install";
            this.btnInstall.UseVisualStyleBackColor = true;
            this.btnInstall.UseWindowsAccentColor = true;
            this.btnInstall.Click += new System.EventHandler(this.BtnInstall_Click);
            // 
            // btnSelectFile
            // 
            this.btnSelectFile.Anchor = System.Windows.Forms.AnchorStyles.Top;
            this.btnSelectFile.BorderRadius = 8;
            this.btnSelectFile.FlatAppearance.BorderSize = 0;
            this.btnSelectFile.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnSelectFile.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnSelectFile.ForeColor = System.Drawing.Color.White;
            this.btnSelectFile.HoverColor = System.Drawing.Color.Empty;
            this.btnSelectFile.Icon = null;
            this.btnSelectFile.Location = new System.Drawing.Point(377, 39);
            this.btnSelectFile.Name = "btnSelectFile";
            this.btnSelectFile.Progress = 0;
            this.btnSelectFile.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(103)))), ((int)(((byte)(225)))));
            this.btnSelectFile.Size = new System.Drawing.Size(150, 40);
            this.btnSelectFile.TabIndex = 8;
            this.btnSelectFile.Text = "Select...";
            this.btnSelectFile.UseVisualStyleBackColor = true;
            this.btnSelectFile.UseWindowsAccentColor = true;
            this.btnSelectFile.Click += new System.EventHandler(this.BtnSelectFile_Click);
            // 
            // roundedTextBox1
            // 
            this.roundedTextBox1.Anchor = System.Windows.Forms.AnchorStyles.Top;
            this.roundedTextBox1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.roundedTextBox1.Cursor = System.Windows.Forms.Cursors.Hand;
            this.roundedTextBox1.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Italic, System.Drawing.GraphicsUnit.Point);
            this.roundedTextBox1.Icon = null;
            this.roundedTextBox1.Location = new System.Drawing.Point(16, 211);
            this.roundedTextBox1.MaxCharacters = 32767;
            this.roundedTextBox1.Multiline = true;
            this.roundedTextBox1.Name = "roundedTextBox1";
            this.roundedTextBox1.Padding = new System.Windows.Forms.Padding(8, 5, 8, 5);
            this.roundedTextBox1.PasswordChar = false;
            this.roundedTextBox1.PlaceHolderColor = System.Drawing.Color.Gold;
            this.roundedTextBox1.PlaceHolderText = "Warning: You should only install plugins from trusted sources. Installing untrust" +
    "ed and/or untested plugins can be harmful to your device!";
            this.roundedTextBox1.ReadOnly = true;
            this.roundedTextBox1.ScrollBars = System.Windows.Forms.ScrollBars.None;
            this.roundedTextBox1.SelectionStart = 0;
            this.roundedTextBox1.Size = new System.Drawing.Size(511, 41);
            this.roundedTextBox1.TabIndex = 11;
            this.roundedTextBox1.TextAlignment = System.Windows.Forms.HorizontalAlignment.Left;
            // 
            // ExtensionZipInstallerView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.roundedTextBox1);
            this.Controls.Add(this.btnInstall);
            this.Controls.Add(this.tableLayoutPanel1);
            this.Controls.Add(this.btnClose);
            this.Controls.Add(this.btnSelectFile);
            this.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.Name = "ExtensionZipInstallerView";
            this.Padding = new System.Windows.Forms.Padding(3);
            this.Size = new System.Drawing.Size(533, 310);
            ((System.ComponentModel.ISupportInitialize)(this.btnClose)).EndInit();
            this.tableLayoutPanel1.ResumeLayout(false);
            this.tableLayoutPanel1.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion
        private PictureButton btnClose;
        private System.Windows.Forms.OpenFileDialog dlgSelectPluginFile;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label lblPackageId;
        private RoundedTextBox txtPackageId;
        private RoundedTextBox txtAuthor;
        private RoundedTextBox txtMainFile;
        private ButtonPrimary btnInstall;
        private ButtonPrimary btnSelectFile;
        private RoundedTextBox txtZipPath;
        private System.Windows.Forms.Label lblZipPath;
        private RoundedTextBox roundedTextBox1;
    }
}
