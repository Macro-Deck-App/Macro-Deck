
namespace SuchByte.MacroDeck.GUI.MainWindowViews
{
    partial class ExtensionStoreView
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
            this.verticalTabControl = new SuchByte.MacroDeck.GUI.CustomControls.VerticalTabControl();
            this.tabPlugins = new System.Windows.Forms.TabPage();
            this.tabIconPacks = new System.Windows.Forms.TabPage();
            this.tabInstalled = new System.Windows.Forms.TabPage();
            this.verticalTabControl.SuspendLayout();
            this.SuspendLayout();
            // 
            // verticalTabControl
            // 
            this.verticalTabControl.Alignment = System.Windows.Forms.TabAlignment.Left;
            this.verticalTabControl.Controls.Add(this.tabPlugins);
            this.verticalTabControl.Controls.Add(this.tabIconPacks);
            this.verticalTabControl.Controls.Add(this.tabInstalled);
            this.verticalTabControl.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.verticalTabControl.ItemSize = new System.Drawing.Size(50, 150);
            this.verticalTabControl.Location = new System.Drawing.Point(3, 3);
            this.verticalTabControl.Multiline = true;
            this.verticalTabControl.Name = "verticalTabControl";
            this.verticalTabControl.SelectedIndex = 0;
            this.verticalTabControl.SelectedTabColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(103)))), ((int)(((byte)(225)))));
            this.verticalTabControl.Size = new System.Drawing.Size(1131, 537);
            this.verticalTabControl.SizeMode = System.Windows.Forms.TabSizeMode.Fixed;
            this.verticalTabControl.TabIndex = 13;
            // 
            // tabPlugins
            // 
            this.tabPlugins.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.tabPlugins.ForeColor = System.Drawing.Color.White;
            this.tabPlugins.Location = new System.Drawing.Point(154, 4);
            this.tabPlugins.Name = "tabPlugins";
            this.tabPlugins.Size = new System.Drawing.Size(973, 529);
            this.tabPlugins.TabIndex = 0;
            this.tabPlugins.Text = "Plugins";
            // 
            // tabIconPacks
            // 
            this.tabIconPacks.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.tabIconPacks.ForeColor = System.Drawing.Color.White;
            this.tabIconPacks.Location = new System.Drawing.Point(154, 4);
            this.tabIconPacks.Name = "tabIconPacks";
            this.tabIconPacks.Size = new System.Drawing.Size(973, 526);
            this.tabIconPacks.TabIndex = 1;
            this.tabIconPacks.Text = "Icon packs";
            // 
            // tabInstalled
            // 
            this.tabInstalled.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.tabInstalled.Location = new System.Drawing.Point(154, 4);
            this.tabInstalled.Name = "tabInstalled";
            this.tabInstalled.Size = new System.Drawing.Size(973, 526);
            this.tabInstalled.TabIndex = 2;
            this.tabInstalled.Text = "Installed";
            // 
            // ExtensionStoreView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.verticalTabControl);
            this.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.Name = "ExtensionStoreView";
            this.Size = new System.Drawing.Size(1137, 540);
            this.Load += new System.EventHandler(this.ExtensionStoreView_Load);
            this.verticalTabControl.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private CustomControls.VerticalTabControl verticalTabControl;
        private System.Windows.Forms.TabPage tabPlugins;
        private System.Windows.Forms.TabPage tabIconPacks;
        private System.Windows.Forms.TabPage tabInstalled;
    }
}
