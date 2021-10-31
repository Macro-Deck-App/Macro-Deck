
using SuchByte.MacroDeck.GUI.CustomControls;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.MainWindowContents
{
    partial class PackageManagerView
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
           /* foreach (PluginDetails plugin in this.installedPluginsPanel.Controls)
            {
                plugin.OnBtnDeleteClick -= this.OnBtnDeleteClick;
            }*/
            foreach (Control control in this.panelAvailablePlugins.Controls)
            {
                if (control.GetType() == typeof(PackageManagerPluginItem))
                {
                    PackageManagerPluginItem packageManagerItem = control as PackageManagerPluginItem;
                    this.Invoke(new Action(() =>
                        packageManagerItem.OnInstallationFinished -= this.OnInstallationFinished
                    ));

                    this.Invoke(new Action(() =>
                        packageManagerItem.Dispose()
                    ));
                }
                if (control.GetType() == typeof(PackageManagerIconPackItem))
                {
                    PackageManagerIconPackItem packageManagerItem = control as PackageManagerIconPackItem;
                    this.Invoke(new Action(() =>
                        packageManagerItem.OnInstallationFinished -= this.OnInstallationFinished
                    ));

                    this.Invoke(new Action(() =>
                        packageManagerItem.Dispose()
                    ));
                }
            }
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
            this.radioInstalled = new SuchByte.MacroDeck.GUI.CustomControls.TabRadioButton();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.checkIconPacks = new System.Windows.Forms.CheckBox();
            this.checkPlugins = new System.Windows.Forms.CheckBox();
            this.radioOnlyUpdates = new SuchByte.MacroDeck.GUI.CustomControls.TabRadioButton();
            this.radioAll = new SuchByte.MacroDeck.GUI.CustomControls.TabRadioButton();
            this.searchBox = new SuchByte.MacroDeck.GUI.CustomControls.RoundedTextBox();
            this.panelAvailablePlugins = new System.Windows.Forms.FlowLayoutPanel();
            this.flowLayoutPanel1 = new System.Windows.Forms.FlowLayoutPanel();
            this.flowLayoutPanel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // radioInstalled
            // 
            this.radioInstalled.AutoSize = true;
            this.radioInstalled.Cursor = System.Windows.Forms.Cursors.Hand;
            this.radioInstalled.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioInstalled.ForeColor = System.Drawing.Color.White;
            this.radioInstalled.Location = new System.Drawing.Point(182, 0);
            this.radioInstalled.Margin = new System.Windows.Forms.Padding(5, 0, 5, 0);
            this.radioInstalled.Name = "radioInstalled";
            this.radioInstalled.Size = new System.Drawing.Size(87, 23);
            this.radioInstalled.TabIndex = 24;
            this.radioInstalled.Text = "Installed";
            this.radioInstalled.UseVisualStyleBackColor = true;
            this.radioInstalled.CheckedChanged += new System.EventHandler(this.RadioAll_CheckedChanged);
            // 
            // progressBar
            // 
            this.progressBar.Location = new System.Drawing.Point(4, 532);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(251, 5);
            this.progressBar.Style = System.Windows.Forms.ProgressBarStyle.Marquee;
            this.progressBar.TabIndex = 21;
            this.progressBar.Visible = false;
            // 
            // checkIconPacks
            // 
            this.checkIconPacks.AutoSize = true;
            this.checkIconPacks.Checked = true;
            this.checkIconPacks.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkIconPacks.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.checkIconPacks.ForeColor = System.Drawing.Color.White;
            this.checkIconPacks.Location = new System.Drawing.Point(120, 29);
            this.checkIconPacks.Name = "checkIconPacks";
            this.checkIconPacks.Size = new System.Drawing.Size(98, 22);
            this.checkIconPacks.TabIndex = 23;
            this.checkIconPacks.Text = "Icon packs";
            this.checkIconPacks.UseVisualStyleBackColor = true;
            this.checkIconPacks.CheckedChanged += new System.EventHandler(this.Type_CheckedChanged);
            // 
            // checkPlugins
            // 
            this.checkPlugins.AutoSize = true;
            this.checkPlugins.Checked = true;
            this.checkPlugins.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkPlugins.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.checkPlugins.ForeColor = System.Drawing.Color.White;
            this.checkPlugins.Location = new System.Drawing.Point(8, 29);
            this.checkPlugins.Name = "checkPlugins";
            this.checkPlugins.Size = new System.Drawing.Size(70, 22);
            this.checkPlugins.TabIndex = 22;
            this.checkPlugins.Text = "Plugins";
            this.checkPlugins.UseVisualStyleBackColor = true;
            this.checkPlugins.CheckedChanged += new System.EventHandler(this.Type_CheckedChanged);
            // 
            // radioOnlyUpdates
            // 
            this.radioOnlyUpdates.AutoSize = true;
            this.radioOnlyUpdates.Cursor = System.Windows.Forms.Cursors.Hand;
            this.radioOnlyUpdates.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioOnlyUpdates.ForeColor = System.Drawing.Color.White;
            this.radioOnlyUpdates.Location = new System.Drawing.Point(88, 0);
            this.radioOnlyUpdates.Margin = new System.Windows.Forms.Padding(5, 0, 5, 0);
            this.radioOnlyUpdates.Name = "radioOnlyUpdates";
            this.radioOnlyUpdates.Size = new System.Drawing.Size(84, 23);
            this.radioOnlyUpdates.TabIndex = 20;
            this.radioOnlyUpdates.Text = "Updates";
            this.radioOnlyUpdates.UseVisualStyleBackColor = true;
            this.radioOnlyUpdates.CheckedChanged += new System.EventHandler(this.RadioAll_CheckedChanged);
            // 
            // radioAll
            // 
            this.radioAll.AutoSize = true;
            this.radioAll.Checked = true;
            this.radioAll.Cursor = System.Windows.Forms.Cursors.Hand;
            this.radioAll.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioAll.ForeColor = System.Drawing.Color.White;
            this.radioAll.Location = new System.Drawing.Point(5, 0);
            this.radioAll.Margin = new System.Windows.Forms.Padding(5, 0, 5, 0);
            this.radioAll.Name = "radioAll";
            this.radioAll.Size = new System.Drawing.Size(73, 23);
            this.radioAll.TabIndex = 19;
            this.radioAll.TabStop = true;
            this.radioAll.Text = "Online";
            this.radioAll.UseVisualStyleBackColor = true;
            this.radioAll.CheckedChanged += new System.EventHandler(this.RadioAll_CheckedChanged);
            // 
            // searchBox
            // 
            this.searchBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.searchBox.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.searchBox.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Italic, System.Drawing.GraphicsUnit.Point);
            this.searchBox.ForeColor = System.Drawing.Color.Gray;
            this.searchBox.Icon = global::SuchByte.MacroDeck.Properties.Resources.magnifying_glass;
            this.searchBox.Location = new System.Drawing.Point(861, 3);
            this.searchBox.Multiline = false;
            this.searchBox.Name = "searchBox";
            this.searchBox.Padding = new System.Windows.Forms.Padding(31, 5, 5, 8);
            this.searchBox.PasswordChar = false;
            this.searchBox.PlaceHolderColor = System.Drawing.Color.Gray;
            this.searchBox.PlaceHolderText = "";
            this.searchBox.ReadOnly = false;
            this.searchBox.SelectionStart = 0;
            this.searchBox.Size = new System.Drawing.Size(272, 30);
            this.searchBox.TabIndex = 17;
            this.searchBox.TextAlignment = System.Windows.Forms.HorizontalAlignment.Left;
            this.searchBox.TextChanged += new System.EventHandler(this.SearchBox_TextChanged);
            // 
            // panelAvailablePlugins
            // 
            this.panelAvailablePlugins.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.panelAvailablePlugins.AutoScroll = true;
            this.panelAvailablePlugins.Location = new System.Drawing.Point(4, 52);
            this.panelAvailablePlugins.Name = "panelAvailablePlugins";
            this.panelAvailablePlugins.Size = new System.Drawing.Size(1129, 474);
            this.panelAvailablePlugins.TabIndex = 16;
            // 
            // flowLayoutPanel1
            // 
            this.flowLayoutPanel1.AutoSize = true;
            this.flowLayoutPanel1.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.flowLayoutPanel1.Controls.Add(this.radioAll);
            this.flowLayoutPanel1.Controls.Add(this.radioOnlyUpdates);
            this.flowLayoutPanel1.Controls.Add(this.radioInstalled);
            this.flowLayoutPanel1.Location = new System.Drawing.Point(8, 3);
            this.flowLayoutPanel1.Name = "flowLayoutPanel1";
            this.flowLayoutPanel1.Size = new System.Drawing.Size(274, 23);
            this.flowLayoutPanel1.TabIndex = 25;
            // 
            // PackageManagerView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.flowLayoutPanel1);
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.checkIconPacks);
            this.Controls.Add(this.checkPlugins);
            this.Controls.Add(this.searchBox);
            this.Controls.Add(this.panelAvailablePlugins);
            this.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.Name = "PackageManagerView";
            this.Size = new System.Drawing.Size(1137, 540);
            this.Load += new System.EventHandler(this.PackageManager_Load);
            this.flowLayoutPanel1.ResumeLayout(false);
            this.flowLayoutPanel1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private GUI.CustomControls.TabRadioButton radioInstalled;
        private ProgressBar progressBar;
        private CheckBox checkIconPacks;
        private CheckBox checkPlugins;
        private GUI.CustomControls.TabRadioButton radioOnlyUpdates;
        private GUI.CustomControls.TabRadioButton radioAll;
        private RoundedTextBox searchBox;
        private FlowLayoutPanel panelAvailablePlugins;
        private FlowLayoutPanel flowLayoutPanel1;
    }
}
