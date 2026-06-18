
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

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
            _searchDebounce?.Dispose();

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
            label1 = new Label();
            txtSearch = new RoundedTextBox();
            pagination = new Pagination();
            extensionsGrid = new ExtensionGrid();
            checkPlugins = new CheckBox();
            checkIconPacks = new CheckBox();
            SuspendLayout();
            //
            // label1
            //
            label1.Font = new Font("Tahoma", 15.75F, FontStyle.Regular, GraphicsUnit.Point);
            label1.ForeColor = Color.Silver;
            label1.Location = new Point(9, 8);
            label1.Name = "label1";
            label1.Size = new Size(300, 33);
            label1.TabIndex = 2;
            label1.Text = "Extension Store";
            label1.TextAlign = ContentAlignment.MiddleLeft;
            //
            // txtSearch
            //
            txtSearch.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            txtSearch.BackColor = Color.FromArgb(65, 65, 65);
            txtSearch.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            txtSearch.ForeColor = Color.White;
            txtSearch.Icon = null;
            txtSearch.Location = new Point(789, 8);
            txtSearch.MaxCharacters = 32767;
            txtSearch.Multiline = false;
            txtSearch.Name = "txtSearch";
            txtSearch.Padding = new Padding(8, 5, 8, 5);
            txtSearch.PasswordChar = false;
            txtSearch.PlaceHolderColor = Color.Gray;
            txtSearch.PlaceHolderText = "Search extensions…";
            txtSearch.ReadOnly = false;
            txtSearch.ScrollBars = ScrollBars.None;
            txtSearch.SelectionStart = 0;
            txtSearch.Size = new Size(345, 30);
            txtSearch.TabIndex = 0;
            txtSearch.TextAlignment = HorizontalAlignment.Left;
            txtSearch.TextChanged += TxtSearch_TextChanged;
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
            // extensionsGrid
            //
            extensionsGrid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            extensionsGrid.BackColor = Color.FromArgb(45, 45, 45);
            extensionsGrid.Location = new Point(176, 47);
            extensionsGrid.Name = "extensionsGrid";
            extensionsGrid.Size = new Size(958, 449);
            extensionsGrid.TabIndex = 5;
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
            Controls.Add(extensionsGrid);
            Controls.Add(pagination);
            Controls.Add(txtSearch);
            Controls.Add(label1);
            Font = new Font("Tahoma", 8.25F, FontStyle.Regular, GraphicsUnit.Point);
            ForeColor = Color.White;
            Name = "ExtensionStoreView";
            Size = new Size(1137, 540);
            Load += ExtensionStoreView_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private RoundedTextBox txtSearch;
        private Pagination pagination;
        private ExtensionGrid extensionsGrid;
        private CheckBox checkPlugins;
        private CheckBox checkIconPacks;
    }
}
