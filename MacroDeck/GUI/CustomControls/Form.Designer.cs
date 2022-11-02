
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
            this.components = new Container();
            ComponentResourceManager resources = new ComponentResourceManager(typeof(Form));
            this.header = new BufferedPanel();
            this.btnClose = new PictureButton();
            this.btnHelp = new LinkLabel();
            this.lblSafeMode = new Label();
            this.label2 = new Label();
            this.helpMenu = new ContextMenuStrip(this.components);
            this.helpMenuDiscordSupport = new ToolStripMenuItem();
            this.helpMenuWiki = new ToolStripMenuItem();
            this.toolStripSeparator1 = new ToolStripSeparator();
            this.helpMenuExportLog = new ToolStripMenuItem();
            this.btnDonate = new LinkLabel();
            this.header.SuspendLayout();
            ((ISupportInitialize)(this.btnClose)).BeginInit();
            this.helpMenu.SuspendLayout();
            this.SuspendLayout();
            // 
            // header
            // 
            this.header.BackColor = Color.Transparent;
            this.header.Controls.Add(this.btnDonate);
            this.header.Controls.Add(this.btnClose);
            this.header.Controls.Add(this.btnHelp);
            this.header.Controls.Add(this.lblSafeMode);
            this.header.Controls.Add(this.label2);
            this.header.Dock = DockStyle.Top;
            this.header.Location = new Point(0, 0);
            this.header.Margin = new Padding(4, 3, 4, 3);
            this.header.Name = "header";
            this.header.Size = new Size(1400, 34);
            this.header.TabIndex = 0;
            this.header.MouseDown += new MouseEventHandler(this.TitleBar_MouseDown);
            // 
            // btnClose
            // 
            this.btnClose.Anchor = ((AnchorStyles)((AnchorStyles.Top | AnchorStyles.Right)));
            this.btnClose.BackColor = Color.Transparent;
            this.btnClose.BackgroundImage = Resources.Close_Normal;
            this.btnClose.BackgroundImageLayout = ImageLayout.Stretch;
            this.btnClose.Cursor = Cursors.Hand;
            this.btnClose.Font = new Font("Tahoma", 9.75F, FontStyle.Bold, GraphicsUnit.Point);
            this.btnClose.ForeColor = Color.White;
            this.btnClose.HoverImage = Resources.Close_Hover;
            this.btnClose.Location = new Point(1367, 3);
            this.btnClose.Margin = new Padding(4);
            this.btnClose.Name = "btnClose";
            this.btnClose.Size = new Size(29, 27);
            this.btnClose.TabIndex = 2;
            this.btnClose.TabStop = false;
            this.btnClose.Click += new EventHandler(this.BtnClose_Click);
            // 
            // btnHelp
            // 
            this.btnHelp.ActiveLinkColor = Color.White;
            this.btnHelp.Anchor = ((AnchorStyles)((AnchorStyles.Top | AnchorStyles.Right)));
            this.btnHelp.AutoSize = true;
            this.btnHelp.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnHelp.LinkColor = Color.Silver;
            this.btnHelp.Location = new Point(1281, 8);
            this.btnHelp.Margin = new Padding(4, 0, 4, 0);
            this.btnHelp.Name = "btnHelp";
            this.btnHelp.Size = new Size(36, 18);
            this.btnHelp.TabIndex = 9;
            this.btnHelp.TabStop = true;
            this.btnHelp.Text = "Help";
            this.btnHelp.UseMnemonic = false;
            this.btnHelp.VisitedLinkColor = Color.Silver;
            this.btnHelp.LinkClicked += new LinkLabelLinkClickedEventHandler(this.BtnHelp_LinkClicked);
            // 
            // lblSafeMode
            // 
            this.lblSafeMode.Font = new Font("Tahoma", 18F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblSafeMode.ForeColor = Color.Silver;
            this.lblSafeMode.Location = new Point(13, 3);
            this.lblSafeMode.Margin = new Padding(4, 0, 4, 0);
            this.lblSafeMode.Name = "lblSafeMode";
            this.lblSafeMode.Size = new Size(163, 29);
            this.lblSafeMode.TabIndex = 8;
            this.lblSafeMode.Text = "Safe Mode";
            this.lblSafeMode.TextAlign = ContentAlignment.MiddleCenter;
            this.lblSafeMode.UseMnemonic = false;
            this.lblSafeMode.Visible = false;
            // 
            // label2
            // 
            this.label2.Anchor = AnchorStyles.Top;
            this.label2.Font = new Font("Tahoma", 12F, FontStyle.Bold, GraphicsUnit.Point);
            this.label2.ForeColor = Color.Silver;
            this.label2.Location = new Point(619, 5);
            this.label2.Margin = new Padding(4, 0, 4, 0);
            this.label2.Name = "label2";
            this.label2.Size = new Size(163, 25);
            this.label2.TabIndex = 4;
            this.label2.Text = "Macro Deck";
            this.label2.TextAlign = ContentAlignment.MiddleCenter;
            this.label2.UseMnemonic = false;
            // 
            // helpMenu
            // 
            this.helpMenu.BackColor = Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.helpMenu.Font = new Font("Tahoma", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.helpMenu.Items.AddRange(new ToolStripItem[] {
            this.helpMenuDiscordSupport,
            this.helpMenuWiki,
            this.toolStripSeparator1,
            this.helpMenuExportLog});
            this.helpMenu.Name = "helpMenu";
            this.helpMenu.ShowImageMargin = false;
            this.helpMenu.ShowItemToolTips = false;
            this.helpMenu.Size = new Size(191, 94);
            // 
            // helpMenuDiscordSupport
            // 
            this.helpMenuDiscordSupport.ForeColor = Color.White;
            this.helpMenuDiscordSupport.Name = "helpMenuDiscordSupport";
            this.helpMenuDiscordSupport.Size = new Size(190, 28);
            this.helpMenuDiscordSupport.Text = "Discord support";
            this.helpMenuDiscordSupport.Click += new EventHandler(this.HelpMenuDiscordSupport_Click);
            // 
            // helpMenuWiki
            // 
            this.helpMenuWiki.ForeColor = Color.White;
            this.helpMenuWiki.Name = "helpMenuWiki";
            this.helpMenuWiki.Size = new Size(190, 28);
            this.helpMenuWiki.Text = "Wiki";
            this.helpMenuWiki.Click += new EventHandler(this.HelpMenuWiki_Click);
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new Size(187, 6);
            // 
            // helpMenuExportLog
            // 
            this.helpMenuExportLog.ForeColor = Color.White;
            this.helpMenuExportLog.Name = "helpMenuExportLog";
            this.helpMenuExportLog.Size = new Size(190, 28);
            this.helpMenuExportLog.Text = "Export latest log";
            this.helpMenuExportLog.Click += new EventHandler(this.HelpMenuExportLog_Click);
            // 
            // btnDonate
            // 
            this.btnDonate.ActiveLinkColor = Color.White;
            this.btnDonate.Anchor = ((AnchorStyles)((AnchorStyles.Top | AnchorStyles.Right)));
            this.btnDonate.AutoSize = true;
            this.btnDonate.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnDonate.LinkColor = Color.Silver;
            this.btnDonate.Location = new Point(1190, 8);
            this.btnDonate.Margin = new Padding(4, 0, 4, 0);
            this.btnDonate.Name = "btnDonate";
            this.btnDonate.Size = new Size(55, 18);
            this.btnDonate.TabIndex = 10;
            this.btnDonate.TabStop = true;
            this.btnDonate.Text = "Donate";
            this.btnDonate.UseMnemonic = false;
            this.btnDonate.VisitedLinkColor = Color.Silver;
            this.btnDonate.LinkClicked += new LinkLabelLinkClickedEventHandler(this.btnDonate_LinkClicked);
            // 
            // Form
            // 
            this.AutoScaleDimensions = new SizeF(96F, 96F);
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.ClientSize = new Size(1400, 700);
            this.ControlBox = false;
            this.Controls.Add(this.header);
            this.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.ForeColor = Color.White;
            this.FormBorderStyle = FormBorderStyle.None;
            this.Icon = ((Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new Padding(4, 3, 4, 3);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "Form";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.header.ResumeLayout(false);
            this.header.PerformLayout();
            ((ISupportInitialize)(this.btnClose)).EndInit();
            this.helpMenu.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private BufferedPanel header;
        private Label label2;
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
