
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.Properties;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    partial class Form
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
            ComponentResourceManager resources = new ComponentResourceManager(typeof(Form));
            header = new BufferedPanel();
            btnDonate = new LinkLabel();
            btnClose = new PictureButton();
            btnHelp = new LinkLabel();
            lblSafeMode = new Label();
            lblTitle = new Label();
            helpMenu = new ContextMenuStrip(components);
            helpMenuDiscordSupport = new ToolStripMenuItem();
            helpMenuWiki = new ToolStripMenuItem();
            toolStripSeparator1 = new ToolStripSeparator();
            helpMenuExportLog = new ToolStripMenuItem();
            header.SuspendLayout();
            ((ISupportInitialize)btnClose).BeginInit();
            helpMenu.SuspendLayout();
            SuspendLayout();
            // 
            // header
            // 
            header.BackColor = Color.Transparent;
            header.Controls.Add(btnDonate);
            header.Controls.Add(btnClose);
            header.Controls.Add(btnHelp);
            header.Controls.Add(lblSafeMode);
            header.Controls.Add(lblTitle);
            header.Dock = DockStyle.Top;
            header.Location = new Point(0, 0);
            header.Margin = new Padding(4, 3, 4, 3);
            header.Name = "header";
            header.Size = new Size(1400, 34);
            header.TabIndex = 0;
            header.MouseDown += TitleBar_MouseDown;
            // 
            // btnDonate
            // 
            btnDonate.ActiveLinkColor = Color.White;
            btnDonate.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnDonate.AutoSize = true;
            btnDonate.Font = new Font("Tahoma", 11.25F);
            btnDonate.LinkColor = Color.Silver;
            btnDonate.Location = new Point(1190, 8);
            btnDonate.Margin = new Padding(4, 0, 4, 0);
            btnDonate.Name = "btnDonate";
            btnDonate.Size = new Size(55, 18);
            btnDonate.TabIndex = 10;
            btnDonate.TabStop = true;
            btnDonate.Text = "Donate";
            btnDonate.UseMnemonic = false;
            btnDonate.VisitedLinkColor = Color.Silver;
            btnDonate.LinkClicked += btnDonate_LinkClicked;
            // 
            // btnClose
            // 
            btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnClose.BackColor = Color.Transparent;
            btnClose.BackgroundImage = Resources.Close_Normal;
            btnClose.BackgroundImageLayout = ImageLayout.Stretch;
            btnClose.Cursor = Cursors.Hand;
            btnClose.Font = new Font("Tahoma", 9.75F, FontStyle.Bold);
            btnClose.ForeColor = Color.White;
            btnClose.HoverImage = Resources.Close_Hover;
            btnClose.Location = new Point(1367, 3);
            btnClose.Margin = new Padding(4);
            btnClose.Name = "btnClose";
            btnClose.Size = new Size(29, 27);
            btnClose.TabIndex = 2;
            btnClose.TabStop = false;
            btnClose.Click += BtnClose_Click;
            // 
            // btnHelp
            // 
            btnHelp.ActiveLinkColor = Color.White;
            btnHelp.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnHelp.AutoSize = true;
            btnHelp.Font = new Font("Tahoma", 11.25F);
            btnHelp.LinkColor = Color.Silver;
            btnHelp.Location = new Point(1281, 8);
            btnHelp.Margin = new Padding(4, 0, 4, 0);
            btnHelp.Name = "btnHelp";
            btnHelp.Size = new Size(36, 18);
            btnHelp.TabIndex = 9;
            btnHelp.TabStop = true;
            btnHelp.Text = "Help";
            btnHelp.UseMnemonic = false;
            btnHelp.VisitedLinkColor = Color.Silver;
            btnHelp.LinkClicked += BtnHelp_LinkClicked;
            // 
            // lblSafeMode
            // 
            lblSafeMode.Font = new Font("Tahoma", 18F);
            lblSafeMode.ForeColor = Color.Silver;
            lblSafeMode.Location = new Point(13, 3);
            lblSafeMode.Margin = new Padding(4, 0, 4, 0);
            lblSafeMode.Name = "lblSafeMode";
            lblSafeMode.Size = new Size(163, 29);
            lblSafeMode.TabIndex = 8;
            lblSafeMode.Text = "Safe Mode";
            lblSafeMode.TextAlign = ContentAlignment.MiddleCenter;
            lblSafeMode.UseMnemonic = false;
            lblSafeMode.Visible = false;
            // 
            // lblTitle
            // 
            lblTitle.Anchor = AnchorStyles.Top;
            lblTitle.Font = new Font("Tahoma", 12F, FontStyle.Bold);
            lblTitle.ForeColor = Color.Silver;
            lblTitle.Location = new Point(619, 5);
            lblTitle.Margin = new Padding(4, 0, 4, 0);
            lblTitle.Name = "lblTitle";
            lblTitle.Size = new Size(163, 25);
            lblTitle.TabIndex = 4;
            lblTitle.Text = "Macro Deck";
            lblTitle.TextAlign = ContentAlignment.MiddleCenter;
            lblTitle.UseMnemonic = false;
            // 
            // helpMenu
            // 
            helpMenu.BackColor = Color.FromArgb(55, 55, 55);
            helpMenu.Font = new Font("Tahoma", 14.25F);
            helpMenu.Items.AddRange(new ToolStripItem[] { helpMenuDiscordSupport, helpMenuWiki, toolStripSeparator1, helpMenuExportLog });
            helpMenu.Name = "helpMenu";
            helpMenu.ShowImageMargin = false;
            helpMenu.ShowItemToolTips = false;
            helpMenu.Size = new Size(191, 94);
            // 
            // helpMenuDiscordSupport
            // 
            helpMenuDiscordSupport.ForeColor = Color.White;
            helpMenuDiscordSupport.Name = "helpMenuDiscordSupport";
            helpMenuDiscordSupport.Size = new Size(190, 28);
            helpMenuDiscordSupport.Text = "Discord support";
            helpMenuDiscordSupport.Click += HelpMenuDiscordSupport_Click;
            // 
            // helpMenuWiki
            // 
            helpMenuWiki.ForeColor = Color.White;
            helpMenuWiki.Name = "helpMenuWiki";
            helpMenuWiki.Size = new Size(190, 28);
            helpMenuWiki.Text = "Wiki";
            helpMenuWiki.Click += HelpMenuWiki_Click;
            // 
            // toolStripSeparator1
            // 
            toolStripSeparator1.Name = "toolStripSeparator1";
            toolStripSeparator1.Size = new Size(187, 6);
            // 
            // helpMenuExportLog
            // 
            helpMenuExportLog.ForeColor = Color.White;
            helpMenuExportLog.Name = "helpMenuExportLog";
            helpMenuExportLog.Size = new Size(190, 28);
            helpMenuExportLog.Text = "Export latest log";
            helpMenuExportLog.Click += HelpMenuExportLog_Click;
            // 
            // Form
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(45, 45, 45);
            ClientSize = new Size(1400, 700);
            ControlBox = false;
            Controls.Add(header);
            DoubleBuffered = true;
            Font = new Font("Tahoma", 9F);
            ForeColor = Color.White;
            FormBorderStyle = FormBorderStyle.None;
            Icon = (Icon)resources.GetObject("$this.Icon");
            Margin = new Padding(4, 3, 4, 3);
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "Form";
            StartPosition = FormStartPosition.CenterScreen;
            header.ResumeLayout(false);
            header.PerformLayout();
            ((ISupportInitialize)btnClose).EndInit();
            helpMenu.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private BufferedPanel header;
        private Label lblTitle;
        public Label lblSafeMode;
        private LinkLabel btnHelp;
        private ContextMenuStrip helpMenu;
        private ToolStripMenuItem helpMenuDiscordSupport;
        private ToolStripMenuItem helpMenuWiki;
        private ToolStripSeparator toolStripSeparator1;
        private ToolStripMenuItem helpMenuExportLog;
        private PictureButton btnClose;
        private LinkLabel btnDonate;
    }
}
