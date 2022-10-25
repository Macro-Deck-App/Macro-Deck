using System.ComponentModel;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.Folders.Plugin.GUI
{
    partial class FolderSwitcherConfigurator
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private IContainer components = null;

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
            this.foldersView = new System.Windows.Forms.TreeView();
            this.SuspendLayout();
            // 
            // foldersView
            // 
            this.foldersView.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.foldersView.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.foldersView.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.foldersView.ForeColor = System.Drawing.Color.White;
            this.foldersView.ItemHeight = 26;
            this.foldersView.Location = new System.Drawing.Point(4, 4);
            this.foldersView.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.foldersView.Name = "foldersView";
            this.foldersView.PathSeparator = "/";
            this.foldersView.Size = new System.Drawing.Size(707, 262);
            this.foldersView.TabIndex = 0;
            // 
            // FolderSwitcherConfigurator
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(10F, 23F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.foldersView);
            this.Margin = new System.Windows.Forms.Padding(6, 7, 6, 7);
            this.Name = "FolderSwitcherConfigurator";
            this.ResumeLayout(false);

        }

        #endregion

        private TreeView foldersView;
    }
}