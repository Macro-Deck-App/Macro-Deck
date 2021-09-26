
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
            foreach (PluginDetails plugin in this.installedPluginsPanel.Controls)
            {
                plugin.OnBtnDeleteClick -= this.OnBtnDeleteClick;
            }
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
            this.tabControl = new SuchByte.MacroDeck.GUI.CustomControls.HorizontalTabControl();
            this.tabAvailable = new System.Windows.Forms.TabPage();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.checkIconPacks = new System.Windows.Forms.CheckBox();
            this.checkPlugins = new System.Windows.Forms.CheckBox();
            this.radioOnlyUpdates = new System.Windows.Forms.RadioButton();
            this.radioAll = new System.Windows.Forms.RadioButton();
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.searchBox = new SuchByte.MacroDeck.GUI.CustomControls.PlaceHolderTextBox();
            this.panelAvailablePlugins = new System.Windows.Forms.FlowLayoutPanel();
            this.tabInstalled = new System.Windows.Forms.TabPage();
            this.pictureBox2 = new System.Windows.Forms.PictureBox();
            this.seachBoxInstalledPlugins = new SuchByte.MacroDeck.GUI.CustomControls.PlaceHolderTextBox();
            this.btnInstallDll = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.installedPluginsPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.tabControl.SuspendLayout();
            this.tabAvailable.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.tabInstalled.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox2)).BeginInit();
            this.SuspendLayout();
            // 
            // tabControl
            // 
            this.tabControl.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tabControl.BackgroundColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.tabControl.Controls.Add(this.tabAvailable);
            this.tabControl.Controls.Add(this.tabInstalled);
            this.tabControl.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.tabControl.ItemSize = new System.Drawing.Size(200, 30);
            this.tabControl.Location = new System.Drawing.Point(0, 3);
            this.tabControl.Multiline = true;
            this.tabControl.Name = "tabControl";
            this.tabControl.SelectedIndex = 0;
            this.tabControl.Size = new System.Drawing.Size(1137, 537);
            this.tabControl.SizeMode = System.Windows.Forms.TabSizeMode.Fixed;
            this.tabControl.TabIndex = 11;
            // 
            // tabAvailable
            // 
            this.tabAvailable.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.tabAvailable.Controls.Add(this.progressBar);
            this.tabAvailable.Controls.Add(this.checkIconPacks);
            this.tabAvailable.Controls.Add(this.checkPlugins);
            this.tabAvailable.Controls.Add(this.radioOnlyUpdates);
            this.tabAvailable.Controls.Add(this.radioAll);
            this.tabAvailable.Controls.Add(this.pictureBox1);
            this.tabAvailable.Controls.Add(this.searchBox);
            this.tabAvailable.Controls.Add(this.panelAvailablePlugins);
            this.tabAvailable.Location = new System.Drawing.Point(4, 34);
            this.tabAvailable.Name = "tabAvailable";
            this.tabAvailable.Padding = new System.Windows.Forms.Padding(3);
            this.tabAvailable.Size = new System.Drawing.Size(1129, 499);
            this.tabAvailable.TabIndex = 0;
            this.tabAvailable.Text = "Available";
            // 
            // progressBar
            // 
            this.progressBar.Location = new System.Drawing.Point(6, 483);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(251, 12);
            this.progressBar.Style = System.Windows.Forms.ProgressBarStyle.Marquee;
            this.progressBar.TabIndex = 12;
            this.progressBar.Visible = false;
            // 
            // checkIconPacks
            // 
            this.checkIconPacks.AutoSize = true;
            this.checkIconPacks.Checked = true;
            this.checkIconPacks.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkIconPacks.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.checkIconPacks.ForeColor = System.Drawing.Color.White;
            this.checkIconPacks.Location = new System.Drawing.Point(119, 6);
            this.checkIconPacks.Name = "checkIconPacks";
            this.checkIconPacks.Size = new System.Drawing.Size(119, 27);
            this.checkIconPacks.TabIndex = 14;
            this.checkIconPacks.Text = "Icon packs";
            this.checkIconPacks.UseVisualStyleBackColor = true;
            this.checkIconPacks.CheckedChanged += new System.EventHandler(this.Type_CheckedChanged);
            // 
            // checkPlugins
            // 
            this.checkPlugins.AutoSize = true;
            this.checkPlugins.Checked = true;
            this.checkPlugins.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkPlugins.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.checkPlugins.ForeColor = System.Drawing.Color.White;
            this.checkPlugins.Location = new System.Drawing.Point(6, 6);
            this.checkPlugins.Name = "checkPlugins";
            this.checkPlugins.Size = new System.Drawing.Size(88, 27);
            this.checkPlugins.TabIndex = 13;
            this.checkPlugins.Text = "Plugins";
            this.checkPlugins.UseVisualStyleBackColor = true;
            this.checkPlugins.CheckedChanged += new System.EventHandler(this.Type_CheckedChanged);
            // 
            // radioOnlyUpdates
            // 
            this.radioOnlyUpdates.AutoSize = true;
            this.radioOnlyUpdates.ForeColor = System.Drawing.Color.White;
            this.radioOnlyUpdates.Location = new System.Drawing.Point(71, 36);
            this.radioOnlyUpdates.Name = "radioOnlyUpdates";
            this.radioOnlyUpdates.Size = new System.Drawing.Size(84, 23);
            this.radioOnlyUpdates.TabIndex = 11;
            this.radioOnlyUpdates.Text = "Updates";
            this.radioOnlyUpdates.UseVisualStyleBackColor = true;
            this.radioOnlyUpdates.CheckedChanged += new System.EventHandler(this.RadioAll_CheckedChanged);
            // 
            // radioAll
            // 
            this.radioAll.AutoSize = true;
            this.radioAll.Checked = true;
            this.radioAll.ForeColor = System.Drawing.Color.White;
            this.radioAll.Location = new System.Drawing.Point(6, 36);
            this.radioAll.Name = "radioAll";
            this.radioAll.Size = new System.Drawing.Size(46, 23);
            this.radioAll.TabIndex = 10;
            this.radioAll.TabStop = true;
            this.radioAll.Text = "All";
            this.radioAll.UseVisualStyleBackColor = true;
            this.radioAll.CheckedChanged += new System.EventHandler(this.RadioAll_CheckedChanged);
            // 
            // pictureBox1
            // 
            this.pictureBox1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.pictureBox1.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.magnifying_glass;
            this.pictureBox1.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.pictureBox1.Location = new System.Drawing.Point(824, 7);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(27, 25);
            this.pictureBox1.TabIndex = 9;
            this.pictureBox1.TabStop = false;
            // 
            // searchBox
            // 
            this.searchBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.searchBox.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.searchBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.searchBox.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Italic, System.Drawing.GraphicsUnit.Point);
            this.searchBox.ForeColor = System.Drawing.Color.Gray;
            this.searchBox.Location = new System.Drawing.Point(851, 6);
            this.searchBox.Name = "searchBox";
            this.searchBox.PlaceHolderText = "";
            this.searchBox.Size = new System.Drawing.Size(272, 27);
            this.searchBox.TabIndex = 8;
            this.searchBox.TextChanged += new System.EventHandler(this.SearchBox_TextChanged);
            // 
            // panelAvailablePlugins
            // 
            this.panelAvailablePlugins.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.panelAvailablePlugins.AutoScroll = true;
            this.panelAvailablePlugins.Location = new System.Drawing.Point(20, 63);
            this.panelAvailablePlugins.Name = "panelAvailablePlugins";
            this.panelAvailablePlugins.Size = new System.Drawing.Size(1103, 412);
            this.panelAvailablePlugins.TabIndex = 7;
            // 
            // tabInstalled
            // 
            this.tabInstalled.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.tabInstalled.Controls.Add(this.pictureBox2);
            this.tabInstalled.Controls.Add(this.seachBoxInstalledPlugins);
            this.tabInstalled.Controls.Add(this.btnInstallDll);
            this.tabInstalled.Controls.Add(this.installedPluginsPanel);
            this.tabInstalled.Location = new System.Drawing.Point(4, 34);
            this.tabInstalled.Name = "tabInstalled";
            this.tabInstalled.Padding = new System.Windows.Forms.Padding(3);
            this.tabInstalled.Size = new System.Drawing.Size(1115, 501);
            this.tabInstalled.TabIndex = 1;
            this.tabInstalled.Text = "Installed";
            // 
            // pictureBox2
            // 
            this.pictureBox2.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.magnifying_glass;
            this.pictureBox2.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.pictureBox2.Location = new System.Drawing.Point(5, 7);
            this.pictureBox2.Name = "pictureBox2";
            this.pictureBox2.Size = new System.Drawing.Size(27, 25);
            this.pictureBox2.TabIndex = 5;
            this.pictureBox2.TabStop = false;
            // 
            // seachBoxInstalledPlugins
            // 
            this.seachBoxInstalledPlugins.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.seachBoxInstalledPlugins.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.seachBoxInstalledPlugins.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Italic, System.Drawing.GraphicsUnit.Point);
            this.seachBoxInstalledPlugins.ForeColor = System.Drawing.Color.Gray;
            this.seachBoxInstalledPlugins.Location = new System.Drawing.Point(32, 6);
            this.seachBoxInstalledPlugins.Name = "seachBoxInstalledPlugins";
            this.seachBoxInstalledPlugins.PlaceHolderText = "";
            this.seachBoxInstalledPlugins.Size = new System.Drawing.Size(271, 27);
            this.seachBoxInstalledPlugins.TabIndex = 4;
            this.seachBoxInstalledPlugins.TextChanged += new System.EventHandler(this.SeachBoxInstalledPlugins_TextChanged);
            // 
            // btnInstallDll
            // 
            this.btnInstallDll.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnInstallDll.BorderRadius = 8;
            this.btnInstallDll.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnInstallDll.FlatAppearance.BorderSize = 0;
            this.btnInstallDll.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnInstallDll.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnInstallDll.ForeColor = System.Drawing.Color.White;
            this.btnInstallDll.Location = new System.Drawing.Point(897, 8);
            this.btnInstallDll.Name = "btnInstallDll";
            this.btnInstallDll.Size = new System.Drawing.Size(212, 23);
            this.btnInstallDll.TabIndex = 1;
            this.btnInstallDll.Text = "Install .dll (not recommended)";
            this.btnInstallDll.UseVisualStyleBackColor = false;
            this.btnInstallDll.Click += new System.EventHandler(this.BtnInstallDll_Click);
            // 
            // installedPluginsPanel
            // 
            this.installedPluginsPanel.AutoScroll = true;
            this.installedPluginsPanel.Location = new System.Drawing.Point(3, 36);
            this.installedPluginsPanel.Name = "installedPluginsPanel";
            this.installedPluginsPanel.Size = new System.Drawing.Size(1106, 459);
            this.installedPluginsPanel.TabIndex = 0;
            // 
            // PackageManagerView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.tabControl);
            this.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.Name = "PackageManagerView";
            this.Size = new System.Drawing.Size(1137, 540);
            this.Load += new System.EventHandler(this.PackageManager_Load);
            this.tabControl.ResumeLayout(false);
            this.tabAvailable.ResumeLayout(false);
            this.tabAvailable.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.tabInstalled.ResumeLayout(false);
            this.tabInstalled.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox2)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion
        private HorizontalTabControl tabControl;
        private System.Windows.Forms.TabPage tabAvailable;
        private System.Windows.Forms.TabPage tabInstalled;
        private System.Windows.Forms.FlowLayoutPanel installedPluginsPanel;
        private ButtonPrimary btnInstallDll;
        private System.Windows.Forms.PictureBox pictureBox2;
        private PlaceHolderTextBox seachBoxInstalledPlugins;
        private System.Windows.Forms.CheckBox checkIconPacks;
        private System.Windows.Forms.CheckBox checkPlugins;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.RadioButton radioOnlyUpdates;
        private System.Windows.Forms.RadioButton radioAll;
        private System.Windows.Forms.PictureBox pictureBox1;
        private PlaceHolderTextBox searchBox;
        private System.Windows.Forms.FlowLayoutPanel panelAvailablePlugins;
    }
}
