
namespace SuchByte.MacroDeck.GUI.InitialSetupPages
{
    partial class SetupPage3
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
            this.lblConfigureGridPreferences = new System.Windows.Forms.Label();
            this.panel1 = new System.Windows.Forms.Panel();
            this.rows = new System.Windows.Forms.NumericUpDown();
            this.columns = new System.Windows.Forms.NumericUpDown();
            this.panel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.rows)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.columns)).BeginInit();
            this.SuspendLayout();
            // 
            // lblConfigureGridPreferences
            // 
            this.lblConfigureGridPreferences.Font = new System.Drawing.Font("Tahoma", 24F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblConfigureGridPreferences.ForeColor = System.Drawing.Color.White;
            this.lblConfigureGridPreferences.ImageAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.lblConfigureGridPreferences.Location = new System.Drawing.Point(3, 0);
            this.lblConfigureGridPreferences.Name = "lblConfigureGridPreferences";
            this.lblConfigureGridPreferences.Size = new System.Drawing.Size(685, 98);
            this.lblConfigureGridPreferences.TabIndex = 4;
            this.lblConfigureGridPreferences.Text = "Now let\'s configure the grid to your preferences";
            this.lblConfigureGridPreferences.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            // 
            // panel1
            // 
            this.panel1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)));
            this.panel1.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Center;
            this.panel1.Controls.Add(this.rows);
            this.panel1.Controls.Add(this.columns);
            this.panel1.Location = new System.Drawing.Point(46, 130);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(591, 328);
            this.panel1.TabIndex = 5;
            // 
            // rows
            // 
            this.rows.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.rows.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.rows.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.rows.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.rows.ForeColor = System.Drawing.Color.White;
            this.rows.Location = new System.Drawing.Point(25, 178);
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
            this.rows.Size = new System.Drawing.Size(80, 30);
            this.rows.TabIndex = 1;
            this.rows.Value = new decimal(new int[] {
            3,
            0,
            0,
            0});
            this.rows.ValueChanged += new System.EventHandler(this.Rows_ValueChanged);
            // 
            // columns
            // 
            this.columns.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.columns.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.columns.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.columns.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.columns.ForeColor = System.Drawing.Color.White;
            this.columns.Location = new System.Drawing.Point(255, 38);
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
            this.columns.Size = new System.Drawing.Size(80, 30);
            this.columns.TabIndex = 0;
            this.columns.Value = new decimal(new int[] {
            5,
            0,
            0,
            0});
            this.columns.ValueChanged += new System.EventHandler(this.Columns_ValueChanged);
            // 
            // SetupPage3
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.lblConfigureGridPreferences);
            this.Name = "SetupPage3";
            this.Size = new System.Drawing.Size(691, 571);
            this.Load += new System.EventHandler(this.SetupPage3_Load);
            this.panel1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.rows)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.columns)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label lblConfigureGridPreferences;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.NumericUpDown rows;
        private System.Windows.Forms.NumericUpDown columns;
    }
}
