
namespace SuchByte.MacroDeck.GUI.CustomControls
{
    partial class Form
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form));
            this.header = new SuchByte.MacroDeck.GUI.CustomControls.BufferedPanel();
            this.btnClose = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.btnHelp = new System.Windows.Forms.LinkLabel();
            this.lblSafeMode = new System.Windows.Forms.Label();
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.label2 = new System.Windows.Forms.Label();
            this.helpMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.helpMenuDiscordSupport = new System.Windows.Forms.ToolStripMenuItem();
            this.helpMenuWiki = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.helpMenuExportLog = new System.Windows.Forms.ToolStripMenuItem();
            this.header.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.btnClose)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.helpMenu.SuspendLayout();
            this.SuspendLayout();
            // 
            // header
            // 
            this.header.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(32)))), ((int)(((byte)(32)))), ((int)(((byte)(32)))));
            this.header.Controls.Add(this.btnClose);
            this.header.Controls.Add(this.btnHelp);
            this.header.Controls.Add(this.lblSafeMode);
            this.header.Controls.Add(this.pictureBox1);
            this.header.Controls.Add(this.label2);
            this.header.Dock = System.Windows.Forms.DockStyle.Top;
            this.header.Location = new System.Drawing.Point(0, 0);
            this.header.Name = "header";
            this.header.Size = new System.Drawing.Size(1200, 32);
            this.header.TabIndex = 0;
            this.header.MouseDown += new System.Windows.Forms.MouseEventHandler(this.TitleBar_MouseDown);
            // 
            // btnClose
            // 
            this.btnClose.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnClose.BackColor = System.Drawing.Color.Transparent;
            this.btnClose.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Close_Normal;
            this.btnClose.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnClose.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnClose.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.btnClose.ForeColor = System.Drawing.Color.White;
            this.btnClose.HoverImage = global::SuchByte.MacroDeck.Properties.Resources.Close_Hover;
            this.btnClose.Location = new System.Drawing.Point(1172, 3);
            this.btnClose.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.btnClose.Name = "btnClose";
            this.btnClose.Size = new System.Drawing.Size(25, 25);
            this.btnClose.TabIndex = 2;
            this.btnClose.TabStop = false;
            this.btnClose.Click += new System.EventHandler(this.BtnClose_Click);
            // 
            // btnHelp
            // 
            this.btnHelp.ActiveLinkColor = System.Drawing.Color.White;
            this.btnHelp.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnHelp.AutoSize = true;
            this.btnHelp.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnHelp.LinkColor = System.Drawing.Color.Silver;
            this.btnHelp.Location = new System.Drawing.Point(1098, 7);
            this.btnHelp.Name = "btnHelp";
            this.btnHelp.Size = new System.Drawing.Size(36, 18);
            this.btnHelp.TabIndex = 9;
            this.btnHelp.TabStop = true;
            this.btnHelp.Text = "Help";
            this.btnHelp.VisitedLinkColor = System.Drawing.Color.Silver;
            this.btnHelp.UseMnemonic = false;
            this.btnHelp.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.BtnHelp_LinkClicked);
            // 
            // lblSafeMode
            // 
            this.lblSafeMode.Font = new System.Drawing.Font("Tahoma", 18F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblSafeMode.ForeColor = System.Drawing.Color.Silver;
            this.lblSafeMode.Location = new System.Drawing.Point(530, 0);
            this.lblSafeMode.Name = "lblSafeMode";
            this.lblSafeMode.Size = new System.Drawing.Size(140, 32);
            this.lblSafeMode.TabIndex = 8;
            this.lblSafeMode.Text = "Safe Mode";
            this.lblSafeMode.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.lblSafeMode.Visible = false;
            this.lblSafeMode.UseMnemonic = false;
            // 
            // pictureBox1
            // 
            this.pictureBox1.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.pictureBox1.Image = global::SuchByte.MacroDeck.Properties.Resources.Macro_Deck_2021;
            this.pictureBox1.Location = new System.Drawing.Point(5, 5);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(23, 23);
            this.pictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.pictureBox1.TabIndex = 7;
            this.pictureBox1.TabStop = false;
            // 
            // label2
            // 
            this.label2.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.label2.ForeColor = System.Drawing.Color.Silver;
            this.label2.Location = new System.Drawing.Point(34, 5);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(140, 23);
            this.label2.TabIndex = 4;
            this.label2.Text = "Macro Deck";
            this.label2.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.label2.UseMnemonic = false;
            // 
            // helpMenu
            // 
            this.helpMenu.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.helpMenu.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.helpMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.helpMenuDiscordSupport,
            this.helpMenuWiki,
            this.toolStripSeparator1,
            this.helpMenuExportLog});
            this.helpMenu.Name = "helpMenu";
            this.helpMenu.ShowImageMargin = false;
            this.helpMenu.ShowItemToolTips = false;
            this.helpMenu.Size = new System.Drawing.Size(191, 94);
            // 
            // helpMenuDiscordSupport
            // 
            this.helpMenuDiscordSupport.ForeColor = System.Drawing.Color.White;
            this.helpMenuDiscordSupport.Name = "helpMenuDiscordSupport";
            this.helpMenuDiscordSupport.Size = new System.Drawing.Size(190, 28);
            this.helpMenuDiscordSupport.Text = "Discord support";
            this.helpMenuDiscordSupport.Click += new System.EventHandler(this.HelpMenuDiscordSupport_Click);
            // 
            // helpMenuWiki
            // 
            this.helpMenuWiki.ForeColor = System.Drawing.Color.White;
            this.helpMenuWiki.Name = "helpMenuWiki";
            this.helpMenuWiki.Size = new System.Drawing.Size(190, 28);
            this.helpMenuWiki.Text = "Wiki";
            this.helpMenuWiki.Click += new System.EventHandler(this.HelpMenuWiki_Click);
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(187, 6);
            // 
            // helpMenuExportLog
            // 
            this.helpMenuExportLog.ForeColor = System.Drawing.Color.White;
            this.helpMenuExportLog.Name = "helpMenuExportLog";
            this.helpMenuExportLog.Size = new System.Drawing.Size(190, 28);
            this.helpMenuExportLog.Text = "Export latest log";
            this.helpMenuExportLog.Click += new System.EventHandler(this.HelpMenuExportLog_Click);
            // 
            // Form
            // 
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.ClientSize = new System.Drawing.Size(1200, 650);
            this.ControlBox = false;
            this.Controls.Add(this.header);
            this.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.ForeColor = System.Drawing.Color.White;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "Form";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.header.ResumeLayout(false);
            this.header.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.btnClose)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.helpMenu.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private BufferedPanel header;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.PictureBox pictureBox1;
        public System.Windows.Forms.Label lblSafeMode;
        private System.Windows.Forms.LinkLabel btnHelp;
        private System.Windows.Forms.ContextMenuStrip helpMenu;
        private System.Windows.Forms.ToolStripMenuItem helpMenuDiscordSupport;
        private System.Windows.Forms.ToolStripMenuItem helpMenuWiki;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripMenuItem helpMenuExportLog;
        private PictureButton btnClose;
    }
}
