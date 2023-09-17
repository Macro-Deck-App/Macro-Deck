namespace SuchByte.MacroDeck.GUI.CustomControls
{
    partial class Pagination
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
            btnFirstPage = new ButtonPrimary();
            btnPreviousPage = new ButtonPrimary();
            btnLastPage = new ButtonPrimary();
            btnNextPage = new ButtonPrimary();
            lblPage = new System.Windows.Forms.Label();
            SuspendLayout();
            // 
            // btnFirstPage
            // 
            btnFirstPage.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
            btnFirstPage.BorderRadius = 8;
            btnFirstPage.FlatAppearance.BorderSize = 0;
            btnFirstPage.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnFirstPage.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            btnFirstPage.ForeColor = System.Drawing.Color.White;
            btnFirstPage.HoverColor = System.Drawing.Color.Empty;
            btnFirstPage.Icon = null;
            btnFirstPage.Location = new System.Drawing.Point(0, 0);
            btnFirstPage.Margin = new System.Windows.Forms.Padding(41, 19, 41, 19);
            btnFirstPage.Name = "btnFirstPage";
            btnFirstPage.Progress = 0;
            btnFirstPage.ProgressColor = System.Drawing.Color.FromArgb(0, 103, 205);
            btnFirstPage.Size = new System.Drawing.Size(50, 50);
            btnFirstPage.TabIndex = 0;
            btnFirstPage.Text = "<<";
            btnFirstPage.UseVisualStyleBackColor = true;
            btnFirstPage.UseWindowsAccentColor = true;
            btnFirstPage.Click += BtnFirstPage_Click;
            // 
            // btnPreviousPage
            // 
            btnPreviousPage.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
            btnPreviousPage.BorderRadius = 8;
            btnPreviousPage.FlatAppearance.BorderSize = 0;
            btnPreviousPage.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnPreviousPage.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            btnPreviousPage.ForeColor = System.Drawing.Color.White;
            btnPreviousPage.HoverColor = System.Drawing.Color.Empty;
            btnPreviousPage.Icon = null;
            btnPreviousPage.Location = new System.Drawing.Point(55, 0);
            btnPreviousPage.Margin = new System.Windows.Forms.Padding(41, 19, 41, 19);
            btnPreviousPage.Name = "btnPreviousPage";
            btnPreviousPage.Progress = 0;
            btnPreviousPage.ProgressColor = System.Drawing.Color.FromArgb(0, 103, 205);
            btnPreviousPage.Size = new System.Drawing.Size(50, 50);
            btnPreviousPage.TabIndex = 1;
            btnPreviousPage.Text = "<";
            btnPreviousPage.UseVisualStyleBackColor = true;
            btnPreviousPage.UseWindowsAccentColor = true;
            btnPreviousPage.Click += BtnPreviousPage_Click;
            // 
            // btnLastPage
            // 
            btnLastPage.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            btnLastPage.BorderRadius = 8;
            btnLastPage.FlatAppearance.BorderSize = 0;
            btnLastPage.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnLastPage.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            btnLastPage.ForeColor = System.Drawing.Color.White;
            btnLastPage.HoverColor = System.Drawing.Color.Empty;
            btnLastPage.Icon = null;
            btnLastPage.Location = new System.Drawing.Point(321, 0);
            btnLastPage.Margin = new System.Windows.Forms.Padding(41, 19, 41, 19);
            btnLastPage.Name = "btnLastPage";
            btnLastPage.Progress = 0;
            btnLastPage.ProgressColor = System.Drawing.Color.FromArgb(0, 103, 205);
            btnLastPage.Size = new System.Drawing.Size(50, 50);
            btnLastPage.TabIndex = 3;
            btnLastPage.Text = ">>";
            btnLastPage.UseVisualStyleBackColor = true;
            btnLastPage.UseWindowsAccentColor = true;
            btnLastPage.Click += BtnLastPage_Click;
            // 
            // btnNextPage
            // 
            btnNextPage.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            btnNextPage.BorderRadius = 8;
            btnNextPage.FlatAppearance.BorderSize = 0;
            btnNextPage.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnNextPage.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            btnNextPage.ForeColor = System.Drawing.Color.White;
            btnNextPage.HoverColor = System.Drawing.Color.Empty;
            btnNextPage.Icon = null;
            btnNextPage.Location = new System.Drawing.Point(266, 0);
            btnNextPage.Margin = new System.Windows.Forms.Padding(41, 19, 41, 19);
            btnNextPage.Name = "btnNextPage";
            btnNextPage.Progress = 0;
            btnNextPage.ProgressColor = System.Drawing.Color.FromArgb(0, 103, 205);
            btnNextPage.Size = new System.Drawing.Size(50, 50);
            btnNextPage.TabIndex = 2;
            btnNextPage.Text = ">";
            btnNextPage.UseVisualStyleBackColor = true;
            btnNextPage.UseWindowsAccentColor = true;
            btnNextPage.Click += BtnNextPage_Click;
            // 
            // lblPage
            // 
            lblPage.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            lblPage.Font = new System.Drawing.Font("Tahoma", 15.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            lblPage.Location = new System.Drawing.Point(111, 0);
            lblPage.Name = "lblPage";
            lblPage.Size = new System.Drawing.Size(149, 50);
            lblPage.TabIndex = 4;
            lblPage.Text = "1/1";
            lblPage.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // Pagination
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            BackColor = System.Drawing.Color.FromArgb(45, 45, 45);
            Controls.Add(lblPage);
            Controls.Add(btnLastPage);
            Controls.Add(btnNextPage);
            Controls.Add(btnPreviousPage);
            Controls.Add(btnFirstPage);
            Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            ForeColor = System.Drawing.Color.White;
            Margin = new System.Windows.Forms.Padding(41, 19, 41, 19);
            Name = "Pagination";
            Size = new System.Drawing.Size(371, 50);
            ResumeLayout(false);
        }

        #endregion

        private ButtonPrimary btnFirstPage;
        private ButtonPrimary btnPreviousPage;
        private ButtonPrimary btnLastPage;
        private ButtonPrimary btnNextPage;
        private System.Windows.Forms.Label lblPage;
    }
}
