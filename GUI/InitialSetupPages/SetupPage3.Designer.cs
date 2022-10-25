
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.InitialSetupPages
{
    partial class SetupPage3
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
            this.lblConfigureGridPreferences = new Label();
            this.panel1 = new Panel();
            this.rows = new NumericUpDown();
            this.columns = new NumericUpDown();
            this.panel1.SuspendLayout();
            ((ISupportInitialize)(this.rows)).BeginInit();
            ((ISupportInitialize)(this.columns)).BeginInit();
            this.SuspendLayout();
            // 
            // lblConfigureGridPreferences
            // 
            this.lblConfigureGridPreferences.Font = new Font("Tahoma", 24F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblConfigureGridPreferences.ForeColor = Color.White;
            this.lblConfigureGridPreferences.ImageAlign = ContentAlignment.BottomCenter;
            this.lblConfigureGridPreferences.Location = new Point(3, 0);
            this.lblConfigureGridPreferences.Name = "lblConfigureGridPreferences";
            this.lblConfigureGridPreferences.Size = new Size(685, 98);
            this.lblConfigureGridPreferences.TabIndex = 4;
            this.lblConfigureGridPreferences.Text = "Now let\'s configure the grid to your preferences";
            this.lblConfigureGridPreferences.TextAlign = ContentAlignment.TopCenter;
            this.lblConfigureGridPreferences.UseMnemonic = false;
            // 
            // panel1
            // 
            this.panel1.Anchor = ((AnchorStyles)((AnchorStyles.Top | AnchorStyles.Bottom)));
            this.panel1.BackgroundImageLayout = ImageLayout.Center;
            this.panel1.Controls.Add(this.rows);
            this.panel1.Controls.Add(this.columns);
            this.panel1.Location = new Point(46, 130);
            this.panel1.Name = "panel1";
            this.panel1.Size = new Size(591, 328);
            this.panel1.TabIndex = 5;
            // 
            // rows
            // 
            this.rows.Anchor = AnchorStyles.None;
            this.rows.BackColor = Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.rows.BorderStyle = BorderStyle.FixedSingle;
            this.rows.Font = new Font("Tahoma", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.rows.ForeColor = Color.White;
            this.rows.Location = new Point(25, 178);
            this.rows.Maximum = new decimal(new int[] {
            20,
            0,
            0,
            0});
            this.rows.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.rows.Name = "rows";
            this.rows.Size = new Size(80, 30);
            this.rows.TabIndex = 1;
            this.rows.Value = new decimal(new int[] {
            3,
            0,
            0,
            0});
            this.rows.ValueChanged += new EventHandler(this.Rows_ValueChanged);
            // 
            // columns
            // 
            this.columns.Anchor = AnchorStyles.None;
            this.columns.BackColor = Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.columns.BorderStyle = BorderStyle.FixedSingle;
            this.columns.Font = new Font("Tahoma", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.columns.ForeColor = Color.White;
            this.columns.Location = new Point(255, 38);
            this.columns.Maximum = new decimal(new int[] {
            20,
            0,
            0,
            0});
            this.columns.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.columns.Name = "columns";
            this.columns.Size = new Size(80, 30);
            this.columns.TabIndex = 0;
            this.columns.Value = new decimal(new int[] {
            5,
            0,
            0,
            0});
            this.columns.ValueChanged += new EventHandler(this.Columns_ValueChanged);
            // 
            // SetupPage3
            // 
            
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.lblConfigureGridPreferences);
            this.Name = "SetupPage3";
            this.Size = new Size(691, 571);
            this.Load += new EventHandler(this.SetupPage3_Load);
            this.panel1.ResumeLayout(false);
            ((ISupportInitialize)(this.rows)).EndInit();
            ((ISupportInitialize)(this.columns)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private Label lblConfigureGridPreferences;
        private Panel panel1;
        private NumericUpDown rows;
        private NumericUpDown columns;
    }
}
