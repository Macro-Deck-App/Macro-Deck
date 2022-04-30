using SuchByte.MacroDeck.GUI.CustomControls;

namespace SuchByte.MacroDeck.GUI
{
    partial class ActionConfigurator
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

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.btnApply = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.label2 = new System.Windows.Forms.Label();
            this.labelDescription = new System.Windows.Forms.Label();
            this.configurationPanel = new System.Windows.Forms.Panel();
            this.lblSelectToBegin = new System.Windows.Forms.Label();
            this.pluginSearch = new SuchByte.MacroDeck.GUI.CustomControls.RoundedTextBox();
            this.pluginsList = new System.Windows.Forms.FlowLayoutPanel();
            this.selectedPluginIcon = new System.Windows.Forms.PictureBox();
            this.lblSelectedActionName = new System.Windows.Forms.Label();
            this.configurationPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.selectedPluginIcon)).BeginInit();
            this.SuspendLayout();
            // 
            // btnApply
            // 
            this.btnApply.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnApply.BorderRadius = 8;
            this.btnApply.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnApply.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnApply.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnApply.ForeColor = System.Drawing.Color.White;
            this.btnApply.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnApply.Icon = null;
            this.btnApply.Location = new System.Drawing.Point(1110, 596);
            this.btnApply.Name = "btnApply";
            this.btnApply.Progress = 0;
            this.btnApply.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnApply.Size = new System.Drawing.Size(75, 25);
            this.btnApply.TabIndex = 1;
            this.btnApply.Text = "Ok";
            this.btnApply.UseMnemonic = false;
            this.btnApply.UseVisualStyleBackColor = true;
            this.btnApply.Click += new System.EventHandler(this.BtnApply_Click);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("Segoe UI Semibold", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.label2.ForeColor = System.Drawing.Color.White;
            this.label2.Location = new System.Drawing.Point(270, 33);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(58, 21);
            this.label2.TabIndex = 2;
            this.label2.Text = "Action";
            this.label2.UseMnemonic = false;
            // 
            // labelDescription
            // 
            this.labelDescription.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.labelDescription.ForeColor = System.Drawing.Color.White;
            this.labelDescription.Location = new System.Drawing.Point(335, 105);
            this.labelDescription.Name = "labelDescription";
            this.labelDescription.Size = new System.Drawing.Size(854, 58);
            this.labelDescription.TabIndex = 3;
            this.labelDescription.UseMnemonic = false;
            // 
            // configurationPanel
            // 
            this.configurationPanel.Controls.Add(this.lblSelectToBegin);
            this.configurationPanel.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.configurationPanel.Location = new System.Drawing.Point(332, 166);
            this.configurationPanel.Name = "configurationPanel";
            this.configurationPanel.Size = new System.Drawing.Size(857, 424);
            this.configurationPanel.TabIndex = 9;
            // 
            // lblSelectToBegin
            // 
            this.lblSelectToBegin.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblSelectToBegin.Location = new System.Drawing.Point(0, 0);
            this.lblSelectToBegin.Name = "lblSelectToBegin";
            this.lblSelectToBegin.Size = new System.Drawing.Size(857, 424);
            this.lblSelectToBegin.TabIndex = 0;
            this.lblSelectToBegin.Text = "Select a plugin and a action to begin";
            this.lblSelectToBegin.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.lblSelectToBegin.UseMnemonic = false;
            // 
            // pluginSearch
            // 
            this.pluginSearch.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.pluginSearch.Cursor = System.Windows.Forms.Cursors.Hand;
            this.pluginSearch.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.pluginSearch.ForeColor = System.Drawing.Color.White;
            this.pluginSearch.Icon = global::SuchByte.MacroDeck.Properties.Resources.magnifying_glass;
            this.pluginSearch.Location = new System.Drawing.Point(11, 16);
            this.pluginSearch.MaxCharacters = 32767;
            this.pluginSearch.Multiline = false;
            this.pluginSearch.Name = "pluginSearch";
            this.pluginSearch.Padding = new System.Windows.Forms.Padding(30, 5, 5, 8);
            this.pluginSearch.PasswordChar = false;
            this.pluginSearch.PlaceHolderColor = System.Drawing.Color.Gray;
            this.pluginSearch.PlaceHolderText = "";
            this.pluginSearch.ReadOnly = false;
            this.pluginSearch.ScrollBars = System.Windows.Forms.ScrollBars.None;
            this.pluginSearch.SelectionStart = 0;
            this.pluginSearch.Size = new System.Drawing.Size(310, 33);
            this.pluginSearch.TabIndex = 10;
            this.pluginSearch.TabStop = false;
            this.pluginSearch.TextAlignment = System.Windows.Forms.HorizontalAlignment.Left;
            this.pluginSearch.TextChanged += new System.EventHandler(this.PluginSearch_TextChanged);
            // 
            // pluginsList
            // 
            this.pluginsList.AutoScroll = true;
            this.pluginsList.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.pluginsList.Location = new System.Drawing.Point(11, 55);
            this.pluginsList.Name = "pluginsList";
            this.pluginsList.Size = new System.Drawing.Size(310, 566);
            this.pluginsList.TabIndex = 11;
            // 
            // selectedPluginIcon
            // 
            this.selectedPluginIcon.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.selectedPluginIcon.Location = new System.Drawing.Point(335, 43);
            this.selectedPluginIcon.Name = "selectedPluginIcon";
            this.selectedPluginIcon.Size = new System.Drawing.Size(50, 50);
            this.selectedPluginIcon.TabIndex = 12;
            this.selectedPluginIcon.TabStop = false;
            // 
            // lblSelectedActionName
            // 
            this.lblSelectedActionName.Font = new System.Drawing.Font("Tahoma", 15.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblSelectedActionName.Location = new System.Drawing.Point(391, 43);
            this.lblSelectedActionName.Name = "lblSelectedActionName";
            this.lblSelectedActionName.Size = new System.Drawing.Size(798, 50);
            this.lblSelectedActionName.TabIndex = 13;
            this.lblSelectedActionName.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblSelectedActionName.UseMnemonic = false;
            // 
            // ActionConfigurator
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.ClientSize = new System.Drawing.Size(1200, 635);
            this.Controls.Add(this.lblSelectedActionName);
            this.Controls.Add(this.selectedPluginIcon);
            this.Controls.Add(this.pluginsList);
            this.Controls.Add(this.pluginSearch);
            this.Controls.Add(this.configurationPanel);
            this.Controls.Add(this.labelDescription);
            this.Controls.Add(this.btnApply);
            this.Name = "ActionConfigurator";
            this.Text = "Macro Deck :: Configure action";
            this.Controls.SetChildIndex(this.btnApply, 0);
            this.Controls.SetChildIndex(this.labelDescription, 0);
            this.Controls.SetChildIndex(this.configurationPanel, 0);
            this.Controls.SetChildIndex(this.pluginSearch, 0);
            this.Controls.SetChildIndex(this.pluginsList, 0);
            this.Controls.SetChildIndex(this.selectedPluginIcon, 0);
            this.Controls.SetChildIndex(this.lblSelectedActionName, 0);
            this.configurationPanel.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.selectedPluginIcon)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion
        private CustomControls.ButtonPrimary btnApply;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label labelDescription;
        private System.Windows.Forms.Panel configurationPanel;
        private System.Windows.Forms.Label lblSelectToBegin;
        private RoundedTextBox pluginSearch;
        private System.Windows.Forms.FlowLayoutPanel pluginsList;
        private System.Windows.Forms.PictureBox selectedPluginIcon;
        private System.Windows.Forms.Label lblSelectedActionName;
    }
}