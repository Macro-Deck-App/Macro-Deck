
namespace SuchByte.MacroDeck.Variables.Plugin.Views
{
    partial class SaveVariableToFileActionConfigView
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
            this.path = new SuchByte.MacroDeck.GUI.CustomControls.RoundedTextBox();
            this.variable = new SuchByte.MacroDeck.GUI.CustomControls.RoundedComboBox();
            this.btnChoosePath = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.lblVariable = new System.Windows.Forms.Label();
            this.lblPath = new System.Windows.Forms.Label();
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // path
            // 
            this.path.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.path.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.path.Cursor = System.Windows.Forms.Cursors.Hand;
            this.path.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.path.Icon = null;
            this.path.Location = new System.Drawing.Point(236, 241);
            this.path.MaxCharacters = 32767;
            this.path.Multiline = false;
            this.path.Name = "path";
            this.path.Padding = new System.Windows.Forms.Padding(8, 5, 8, 5);
            this.path.PasswordChar = false;
            this.path.PlaceHolderColor = System.Drawing.Color.Gray;
            this.path.PlaceHolderText = "";
            this.path.ReadOnly = false;
            this.path.ScrollBars = System.Windows.Forms.ScrollBars.None;
            this.path.SelectionStart = 0;
            this.path.Size = new System.Drawing.Size(488, 25);
            this.path.TabIndex = 0;
            this.path.TextAlignment = System.Windows.Forms.HorizontalAlignment.Left;
            // 
            // variable
            // 
            this.variable.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.variable.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.variable.Cursor = System.Windows.Forms.Cursors.Hand;
            this.variable.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.variable.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.variable.Icon = null;
            this.variable.Location = new System.Drawing.Point(236, 158);
            this.variable.Name = "variable";
            this.variable.Padding = new System.Windows.Forms.Padding(8, 2, 8, 2);
            this.variable.SelectedIndex = -1;
            this.variable.SelectedItem = null;
            this.variable.Size = new System.Drawing.Size(332, 26);
            this.variable.TabIndex = 1;
            // 
            // btnChoosePath
            // 
            this.btnChoosePath.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.btnChoosePath.BorderRadius = 8;
            this.btnChoosePath.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnChoosePath.FlatAppearance.BorderSize = 0;
            this.btnChoosePath.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnChoosePath.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnChoosePath.ForeColor = System.Drawing.Color.White;
            this.btnChoosePath.HoverColor = System.Drawing.Color.Empty;
            this.btnChoosePath.Icon = null;
            this.btnChoosePath.Location = new System.Drawing.Point(730, 241);
            this.btnChoosePath.Name = "btnChoosePath";
            this.btnChoosePath.Progress = 0;
            this.btnChoosePath.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(103)))), ((int)(((byte)(205)))));
            this.btnChoosePath.Size = new System.Drawing.Size(32, 25);
            this.btnChoosePath.TabIndex = 2;
            this.btnChoosePath.Text = "...";
            this.btnChoosePath.UseVisualStyleBackColor = true;
            this.btnChoosePath.UseWindowsAccentColor = true;
            this.btnChoosePath.Click += new System.EventHandler(this.BtnChoosePath_Click);
            // 
            // lblVariable
            // 
            this.lblVariable.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.lblVariable.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblVariable.Location = new System.Drawing.Point(95, 158);
            this.lblVariable.Name = "lblVariable";
            this.lblVariable.Size = new System.Drawing.Size(135, 26);
            this.lblVariable.TabIndex = 3;
            this.lblVariable.Text = "Variable";
            this.lblVariable.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblPath
            // 
            this.lblPath.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.lblPath.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblPath.Location = new System.Drawing.Point(95, 241);
            this.lblPath.Name = "lblPath";
            this.lblPath.Size = new System.Drawing.Size(135, 25);
            this.lblPath.TabIndex = 4;
            this.lblPath.Text = "Path";
            this.lblPath.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // pictureBox1
            // 
            this.pictureBox1.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Arrow_Down_Hover;
            this.pictureBox1.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Center;
            this.pictureBox1.Location = new System.Drawing.Point(404, 190);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(48, 45);
            this.pictureBox1.TabIndex = 11;
            this.pictureBox1.TabStop = false;
            // 
            // SaveVariableToFileActionConfigView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.Controls.Add(this.pictureBox1);
            this.Controls.Add(this.lblPath);
            this.Controls.Add(this.lblVariable);
            this.Controls.Add(this.btnChoosePath);
            this.Controls.Add(this.variable);
            this.Controls.Add(this.path);
            this.Name = "SaveVariableToFileActionConfigView";
            this.Load += new System.EventHandler(this.SaveVariableToFileActionConfigView_Load);
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private SuchByte.MacroDeck.GUI.CustomControls.RoundedTextBox path;
        private SuchByte.MacroDeck.GUI.CustomControls.RoundedComboBox variable;
        private SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary btnChoosePath;
        private System.Windows.Forms.Label lblVariable;
        private System.Windows.Forms.Label lblPath;
        private System.Windows.Forms.PictureBox pictureBox1;
    }
}
