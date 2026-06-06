
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.Properties;

namespace SuchByte.MacroDeck.GUI.CustomControls.ExtensionsView
{
    partial class ExtensionStoreView
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
            _cancellationTokenSource.Cancel();
            pagination.PageUpdated -= Pagination_PageUpdated;

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
            extensionStoreIcon = new PictureBox();
            label1 = new Label();
            pagination = new Pagination();
            extensionsList = new FlowLayoutPanel();
            checkPlugins = new CheckBox();
            checkIconPacks = new CheckBox();
            ((ISupportInitialize)extensionStoreIcon).BeginInit();
            SuspendLayout();
            // 
            // extensionStoreIcon
            // 
            extensionStoreIcon.Image = Resources.Macro_Deck_2021;
            extensionStoreIcon.Location = new Point(9, 3);
            extensionStoreIcon.Name = "extensionStoreIcon";
            extensionStoreIcon.Size = new Size(41, 41);
            extensionStoreIcon.SizeMode = PictureBoxSizeMode.StretchImage;
            extensionStoreIcon.TabIndex = 1;
            extensionStoreIcon.TabStop = false;
            // 
            // label1
            // 
            label1.Font = new Font("Tahoma", 15.75F, FontStyle.Regular, GraphicsUnit.Point);
            label1.ForeColor = Color.Silver;
            label1.Location = new Point(56, 3);
            label1.Name = "label1";
            label1.Size = new Size(250, 41);
            label1.TabIndex = 2;
            label1.Text = "Extension Store";
            label1.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // pagination
            // 
            pagination.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            pagination.BackColor = Color.FromArgb(45, 45, 45);
            pagination.CurrentPage = 1;
            pagination.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            pagination.ForeColor = Color.White;
            pagination.Location = new Point(0, 503);
            pagination.Margin = new Padding(41, 19, 41, 19);
            pagination.Name = "pagination";
            pagination.Pages = 1;
            pagination.Size = new Size(371, 33);
            pagination.TabIndex = 4;
            // 
            // extensionsList
            // 
            extensionsList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            extensionsList.AutoScroll = true;
            extensionsList.Location = new Point(176, 47);
            extensionsList.Name = "extensionsList";
            extensionsList.Size = new Size(958, 449);
            extensionsList.TabIndex = 5;
            // 
            // checkPlugins
            // 
            checkPlugins.Checked = true;
            checkPlugins.CheckState = CheckState.Checked;
            checkPlugins.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            checkPlugins.Location = new Point(9, 61);
            checkPlugins.Name = "checkPlugins";
            checkPlugins.Size = new Size(161, 28);
            checkPlugins.TabIndex = 6;
            checkPlugins.Text = "Plugins";
            checkPlugins.UseVisualStyleBackColor = true;
            checkPlugins.CheckedChanged += CheckPlugins_CheckedChanged;
            // 
            // checkIconPacks
            // 
            checkIconPacks.Checked = true;
            checkIconPacks.CheckState = CheckState.Checked;
            checkIconPacks.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            checkIconPacks.Location = new Point(9, 95);
            checkIconPacks.Name = "checkIconPacks";
            checkIconPacks.Size = new Size(161, 28);
            checkIconPacks.TabIndex = 7;
            checkIconPacks.Text = "Icon packs";
            checkIconPacks.UseVisualStyleBackColor = true;
            checkIconPacks.CheckedChanged += CheckIconPacks_CheckedChanged;
            // 
            // ExtensionStoreView
            // 
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(45, 45, 45);
            Controls.Add(checkIconPacks);
            Controls.Add(checkPlugins);
            Controls.Add(extensionsList);
            Controls.Add(pagination);
            Controls.Add(label1);
            Controls.Add(extensionStoreIcon);
            Font = new Font("Tahoma", 8.25F, FontStyle.Regular, GraphicsUnit.Point);
            ForeColor = Color.White;
            Name = "ExtensionStoreView";
            Size = new Size(1137, 540);
            Load += ExtensionStoreView_Load;
            ((ISupportInitialize)extensionStoreIcon).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private PictureBox extensionStoreIcon;
        private Label label1;
        private Pagination pagination;
        private FlowLayoutPanel extensionsList;
        private CheckBox checkPlugins;
        private CheckBox checkIconPacks;
    }
}
