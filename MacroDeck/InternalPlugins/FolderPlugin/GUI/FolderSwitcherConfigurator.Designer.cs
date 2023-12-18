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
            foldersView = new TreeView();
            SuspendLayout();
            // 
            // foldersView
            // 
            foldersView.BackColor = Color.FromArgb(65, 65, 65);
            foldersView.BorderStyle = BorderStyle.FixedSingle;
            foldersView.Dock = DockStyle.Fill;
            foldersView.Font = new Font("Tahoma", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            foldersView.ForeColor = Color.White;
            foldersView.ItemHeight = 26;
            foldersView.Location = new Point(0, 0);
            foldersView.Margin = new Padding(4);
            foldersView.Name = "foldersView";
            foldersView.PathSeparator = "/";
            foldersView.Size = new Size(857, 424);
            foldersView.TabIndex = 0;
            // 
            // FolderSwitcherConfigurator
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(45, 45, 45);
            Controls.Add(foldersView);
            Margin = new Padding(6, 7, 6, 7);
            Name = "FolderSwitcherConfigurator";
            ResumeLayout(false);
        }

        #endregion

        private TreeView foldersView;
    }
}