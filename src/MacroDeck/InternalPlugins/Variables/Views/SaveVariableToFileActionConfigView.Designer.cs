
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Properties;

namespace SuchByte.MacroDeck.Variables.Plugin.Views
{
    partial class SaveVariableToFileActionConfigView
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
            this.path = new RoundedTextBox();
            this.variable = new RoundedComboBox();
            this.btnChoosePath = new ButtonPrimary();
            this.lblVariable = new Label();
            this.lblPath = new Label();
            this.pictureBox1 = new PictureBox();
            ((ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // path
            // 
            this.path.Anchor = AnchorStyles.None;
            this.path.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.path.Cursor = Cursors.Hand;
            this.path.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.path.Icon = null;
            this.path.Location = new Point(236, 241);
            this.path.MaxCharacters = 32767;
            this.path.Multiline = false;
            this.path.Name = "path";
            this.path.Padding = new Padding(8, 5, 8, 5);
            this.path.PasswordChar = false;
            this.path.PlaceHolderColor = Color.Gray;
            this.path.PlaceHolderText = "";
            this.path.ReadOnly = false;
            this.path.ScrollBars = ScrollBars.None;
            this.path.SelectionStart = 0;
            this.path.Size = new Size(488, 25);
            this.path.TabIndex = 0;
            this.path.TextAlignment = HorizontalAlignment.Left;
            // 
            // variable
            // 
            this.variable.Anchor = AnchorStyles.None;
            this.variable.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.variable.Cursor = Cursors.Hand;
            this.variable.DropDownStyle = ComboBoxStyle.DropDownList;
            this.variable.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.variable.Icon = null;
            this.variable.Location = new Point(236, 158);
            this.variable.Name = "variable";
            this.variable.Padding = new Padding(8, 2, 8, 2);
            this.variable.SelectedIndex = -1;
            this.variable.SelectedItem = null;
            this.variable.Size = new Size(332, 26);
            this.variable.TabIndex = 1;
            // 
            // btnChoosePath
            // 
            this.btnChoosePath.Anchor = AnchorStyles.None;
            this.btnChoosePath.BorderRadius = 8;
            this.btnChoosePath.Cursor = Cursors.Hand;
            this.btnChoosePath.FlatAppearance.BorderSize = 0;
            this.btnChoosePath.FlatStyle = FlatStyle.Flat;
            this.btnChoosePath.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnChoosePath.ForeColor = Color.White;
            this.btnChoosePath.HoverColor = Color.Empty;
            this.btnChoosePath.Icon = null;
            this.btnChoosePath.Location = new Point(730, 241);
            this.btnChoosePath.Name = "btnChoosePath";
            this.btnChoosePath.Progress = 0;
            this.btnChoosePath.ProgressColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(103)))), ((int)(((byte)(205)))));
            this.btnChoosePath.Size = new Size(32, 25);
            this.btnChoosePath.TabIndex = 2;
            this.btnChoosePath.Text = "...";
            this.btnChoosePath.UseVisualStyleBackColor = true;
            this.btnChoosePath.UseWindowsAccentColor = true;
            this.btnChoosePath.Click += new EventHandler(this.BtnChoosePath_Click);
            // 
            // lblVariable
            // 
            this.lblVariable.Anchor = AnchorStyles.None;
            this.lblVariable.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblVariable.Location = new Point(95, 158);
            this.lblVariable.Name = "lblVariable";
            this.lblVariable.Size = new Size(135, 26);
            this.lblVariable.TabIndex = 3;
            this.lblVariable.Text = "Variable";
            this.lblVariable.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // lblPath
            // 
            this.lblPath.Anchor = AnchorStyles.None;
            this.lblPath.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblPath.Location = new Point(95, 241);
            this.lblPath.Name = "lblPath";
            this.lblPath.Size = new Size(135, 25);
            this.lblPath.TabIndex = 4;
            this.lblPath.Text = "Path";
            this.lblPath.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // pictureBox1
            // 
            this.pictureBox1.BackgroundImage = Resources.Arrow_Down_Hover;
            this.pictureBox1.BackgroundImageLayout = ImageLayout.Center;
            this.pictureBox1.Location = new Point(404, 190);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new Size(48, 45);
            this.pictureBox1.TabIndex = 11;
            this.pictureBox1.TabStop = false;
            // 
            // SaveVariableToFileActionConfigView
            // 
            this.AutoScaleDimensions = new SizeF(96F, 96F);
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.Controls.Add(this.pictureBox1);
            this.Controls.Add(this.lblPath);
            this.Controls.Add(this.lblVariable);
            this.Controls.Add(this.btnChoosePath);
            this.Controls.Add(this.variable);
            this.Controls.Add(this.path);
            this.Name = "SaveVariableToFileActionConfigView";
            this.Load += new EventHandler(this.SaveVariableToFileActionConfigView_Load);
            ((ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private RoundedTextBox path;
        private RoundedComboBox variable;
        private ButtonPrimary btnChoosePath;
        private Label lblVariable;
        private Label lblPath;
        private PictureBox pictureBox1;
    }
}
