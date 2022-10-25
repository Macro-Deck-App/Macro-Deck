
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.Properties;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    partial class ActionItem
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
            this.lblPlugin = new Label();
            this.btnRemove = new PictureButton();
            this.btnEdit = new PictureButton();
            this.btnDown = new PictureButton();
            this.btnUp = new PictureButton();
            this.panelEdit = new FlowLayoutPanel();
            this.lblAction = new Label();
            this.panel1 = new Panel();
            this.flowLayoutPanel1 = new FlowLayoutPanel();
            this.lblConfigurationSummary = new Label();
            ((ISupportInitialize)(this.btnRemove)).BeginInit();
            ((ISupportInitialize)(this.btnEdit)).BeginInit();
            ((ISupportInitialize)(this.btnDown)).BeginInit();
            ((ISupportInitialize)(this.btnUp)).BeginInit();
            this.panelEdit.SuspendLayout();
            this.panel1.SuspendLayout();
            this.flowLayoutPanel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // lblPlugin
            // 
            this.lblPlugin.AutoSize = true;
            this.lblPlugin.Font = new Font("Tahoma", 11.25F, FontStyle.Bold, GraphicsUnit.Point);
            this.lblPlugin.Location = new Point(0, 0);
            this.lblPlugin.MinimumSize = new Size(0, 22);
            this.lblPlugin.Name = "lblPlugin";
            this.lblPlugin.Size = new Size(223, 22);
            this.lblPlugin.TabIndex = 1;
            this.lblPlugin.Text = "Macro-Deck OBS-WebSocket";
            this.lblPlugin.TextAlign = ContentAlignment.MiddleLeft;
            this.lblPlugin.UseMnemonic = false;
            // 
            // btnRemove
            // 
            this.btnRemove.Anchor = ((AnchorStyles)((AnchorStyles.Top | AnchorStyles.Right)));
            this.btnRemove.BackColor = Color.Transparent;
            this.btnRemove.BackgroundImage = Resources.Delete_Normal;
            this.btnRemove.BackgroundImageLayout = ImageLayout.Stretch;
            this.btnRemove.Cursor = Cursors.Hand;
            this.btnRemove.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnRemove.ForeColor = Color.White;
            this.btnRemove.HoverImage = Resources.Delete_Hover;
            this.btnRemove.Location = new Point(94, 0);
            this.btnRemove.Margin = new Padding(2, 0, 2, 0);
            this.btnRemove.Name = "btnRemove";
            this.btnRemove.Size = new Size(25, 25);
            this.btnRemove.TabIndex = 2;
            this.btnRemove.TabStop = false;
            this.btnRemove.Click += new EventHandler(this.BtnRemove_Click);
            // 
            // btnEdit
            // 
            this.btnEdit.Anchor = ((AnchorStyles)((AnchorStyles.Top | AnchorStyles.Right)));
            this.btnEdit.BackColor = Color.Transparent;
            this.btnEdit.BackgroundImage = Resources.Edit_Normal;
            this.btnEdit.BackgroundImageLayout = ImageLayout.Stretch;
            this.btnEdit.Cursor = Cursors.Hand;
            this.btnEdit.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnEdit.ForeColor = Color.White;
            this.btnEdit.HoverImage = Resources.Edit_Hover;
            this.btnEdit.Location = new Point(65, 0);
            this.btnEdit.Margin = new Padding(2, 0, 2, 0);
            this.btnEdit.Name = "btnEdit";
            this.btnEdit.Size = new Size(25, 25);
            this.btnEdit.TabIndex = 3;
            this.btnEdit.TabStop = false;
            this.btnEdit.Click += new EventHandler(this.BtnEdit_Click);
            // 
            // btnDown
            // 
            this.btnDown.Anchor = ((AnchorStyles)((AnchorStyles.Top | AnchorStyles.Right)));
            this.btnDown.BackColor = Color.Transparent;
            this.btnDown.BackgroundImage = Resources.Arrow_Down_Normal;
            this.btnDown.BackgroundImageLayout = ImageLayout.Stretch;
            this.btnDown.Cursor = Cursors.Hand;
            this.btnDown.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnDown.ForeColor = Color.White;
            this.btnDown.HoverImage = Resources.Arrow_Down_Hover;
            this.btnDown.Location = new Point(36, 0);
            this.btnDown.Margin = new Padding(2, 0, 2, 0);
            this.btnDown.Name = "btnDown";
            this.btnDown.Size = new Size(25, 25);
            this.btnDown.TabIndex = 4;
            this.btnDown.TabStop = false;
            this.btnDown.Click += new EventHandler(this.btnDown_Click);
            // 
            // btnUp
            // 
            this.btnUp.Anchor = ((AnchorStyles)((AnchorStyles.Top | AnchorStyles.Right)));
            this.btnUp.BackColor = Color.Transparent;
            this.btnUp.BackgroundImage = Resources.Arrow_Up_Normal;
            this.btnUp.BackgroundImageLayout = ImageLayout.Stretch;
            this.btnUp.Cursor = Cursors.Hand;
            this.btnUp.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnUp.ForeColor = Color.White;
            this.btnUp.HoverImage = Resources.Arrow_Up_Hover;
            this.btnUp.Location = new Point(7, 0);
            this.btnUp.Margin = new Padding(2, 0, 2, 0);
            this.btnUp.Name = "btnUp";
            this.btnUp.Size = new Size(25, 25);
            this.btnUp.TabIndex = 5;
            this.btnUp.TabStop = false;
            this.btnUp.Click += new EventHandler(this.btnUp_Click);
            // 
            // panelEdit
            // 
            this.panelEdit.Anchor = AnchorStyles.None;
            this.panelEdit.Controls.Add(this.btnRemove);
            this.panelEdit.Controls.Add(this.btnEdit);
            this.panelEdit.Controls.Add(this.btnDown);
            this.panelEdit.Controls.Add(this.btnUp);
            this.panelEdit.FlowDirection = FlowDirection.RightToLeft;
            this.panelEdit.Location = new Point(717, 6);
            this.panelEdit.Name = "panelEdit";
            this.panelEdit.Size = new Size(121, 26);
            this.panelEdit.TabIndex = 6;
            // 
            // lblAction
            // 
            this.lblAction.AutoSize = true;
            this.lblAction.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblAction.Location = new Point(0, 21);
            this.lblAction.MinimumSize = new Size(0, 22);
            this.lblAction.Name = "lblAction";
            this.lblAction.Size = new Size(47, 22);
            this.lblAction.TabIndex = 7;
            this.lblAction.Text = "Action";
            this.lblAction.TextAlign = ContentAlignment.MiddleLeft;
            this.lblAction.UseMnemonic = false;
            // 
            // panel1
            // 
            this.panel1.AutoSize = true;
            this.panel1.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.panel1.Controls.Add(this.lblPlugin);
            this.panel1.Controls.Add(this.lblAction);
            this.panel1.Location = new Point(0, 0);
            this.panel1.Margin = new Padding(0);
            this.panel1.Name = "panel1";
            this.panel1.Size = new Size(226, 43);
            this.panel1.TabIndex = 8;
            // 
            // flowLayoutPanel1
            // 
            this.flowLayoutPanel1.Controls.Add(this.panel1);
            this.flowLayoutPanel1.Controls.Add(this.lblConfigurationSummary);
            this.flowLayoutPanel1.Dock = DockStyle.Left;
            this.flowLayoutPanel1.Location = new Point(2, 2);
            this.flowLayoutPanel1.Margin = new Padding(0);
            this.flowLayoutPanel1.Name = "flowLayoutPanel1";
            this.flowLayoutPanel1.Size = new Size(713, 46);
            this.flowLayoutPanel1.TabIndex = 9;
            // 
            // lblConfigurationSummary
            // 
            this.lblConfigurationSummary.Location = new Point(229, 0);
            this.lblConfigurationSummary.Name = "lblConfigurationSummary";
            this.lblConfigurationSummary.Size = new Size(481, 43);
            this.lblConfigurationSummary.TabIndex = 9;
            this.lblConfigurationSummary.Text = "ConfigurationSummary";
            this.lblConfigurationSummary.TextAlign = ContentAlignment.MiddleLeft;
            this.lblConfigurationSummary.UseMnemonic = false;
            // 
            // ActionItem
            // 
            
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = Color.FromArgb(((int)(((byte)(8)))), ((int)(((byte)(107)))), ((int)(((byte)(138)))));
            this.Controls.Add(this.flowLayoutPanel1);
            this.Controls.Add(this.panelEdit);
            this.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.ForeColor = Color.White;
            this.Margin = new Padding(0, 3, 0, 3);
            this.Name = "ActionItem";
            this.Padding = new Padding(2);
            this.Size = new Size(842, 50);
            ((ISupportInitialize)(this.btnRemove)).EndInit();
            ((ISupportInitialize)(this.btnEdit)).EndInit();
            ((ISupportInitialize)(this.btnDown)).EndInit();
            ((ISupportInitialize)(this.btnUp)).EndInit();
            this.panelEdit.ResumeLayout(false);
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.flowLayoutPanel1.ResumeLayout(false);
            this.flowLayoutPanel1.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion
        private Label lblPlugin;
        private PictureButton btnRemove;
        private PictureButton btnEdit;
        private PictureButton btnDown;
        private PictureButton btnUp;
        private FlowLayoutPanel panelEdit;
        private Label lblAction;
        private Panel panel1;
        private FlowLayoutPanel flowLayoutPanel1;
        private Label lblConfigurationSummary;
    }
}
