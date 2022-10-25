
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Properties;

namespace SuchByte.MacroDeck.Variables.Plugin.Views
{
    partial class ReadVariableFromFileActionConfigView
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
            this.lblPath = new Label();
            this.lblVariable = new Label();
            this.btnChoosePath = new ButtonPrimary();
            this.variable = new RoundedComboBox();
            this.path = new RoundedTextBox();
            this.pictureBox1 = new PictureBox();
            this.btnCreateVariable = new ButtonPrimary();
            ((ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // lblPath
            // 
            this.lblPath.Anchor = AnchorStyles.None;
            this.lblPath.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblPath.Location = new Point(95, 158);
            this.lblPath.Name = "lblPath";
            this.lblPath.Size = new Size(135, 25);
            this.lblPath.TabIndex = 9;
            this.lblPath.Text = "Path";
            this.lblPath.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // lblVariable
            // 
            this.lblVariable.Anchor = AnchorStyles.None;
            this.lblVariable.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblVariable.Location = new Point(95, 240);
            this.lblVariable.Name = "lblVariable";
            this.lblVariable.Size = new Size(135, 26);
            this.lblVariable.TabIndex = 8;
            this.lblVariable.Text = "Variable";
            this.lblVariable.TextAlign = ContentAlignment.MiddleLeft;
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
            this.btnChoosePath.Location = new Point(730, 158);
            this.btnChoosePath.Name = "btnChoosePath";
            this.btnChoosePath.Progress = 0;
            this.btnChoosePath.ProgressColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(103)))), ((int)(((byte)(205)))));
            this.btnChoosePath.Size = new Size(32, 25);
            this.btnChoosePath.TabIndex = 7;
            this.btnChoosePath.Text = "...";
            this.btnChoosePath.UseVisualStyleBackColor = true;
            this.btnChoosePath.UseWindowsAccentColor = true;
            this.btnChoosePath.Click += new EventHandler(this.BtnChoosePath_Click);
            // 
            // variable
            // 
            this.variable.Anchor = AnchorStyles.None;
            this.variable.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.variable.Cursor = Cursors.Hand;
            this.variable.DropDownStyle = ComboBoxStyle.DropDownList;
            this.variable.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.variable.Icon = null;
            this.variable.Location = new Point(236, 240);
            this.variable.Name = "variable";
            this.variable.Padding = new Padding(8, 2, 8, 2);
            this.variable.SelectedIndex = -1;
            this.variable.SelectedItem = null;
            this.variable.Size = new Size(332, 26);
            this.variable.TabIndex = 6;
            // 
            // path
            // 
            this.path.Anchor = AnchorStyles.None;
            this.path.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.path.Cursor = Cursors.Hand;
            this.path.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.path.Icon = null;
            this.path.Location = new Point(236, 158);
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
            this.path.TabIndex = 5;
            this.path.TextAlignment = HorizontalAlignment.Left;
            // 
            // pictureBox1
            // 
            this.pictureBox1.BackgroundImage = Resources.Arrow_Down_Hover;
            this.pictureBox1.BackgroundImageLayout = ImageLayout.Center;
            this.pictureBox1.Location = new Point(404, 189);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new Size(48, 45);
            this.pictureBox1.TabIndex = 10;
            this.pictureBox1.TabStop = false;
            // 
            // btnCreateVariable
            // 
            this.btnCreateVariable.Anchor = AnchorStyles.None;
            this.btnCreateVariable.BorderRadius = 8;
            this.btnCreateVariable.Cursor = Cursors.Hand;
            this.btnCreateVariable.FlatAppearance.BorderSize = 0;
            this.btnCreateVariable.FlatStyle = FlatStyle.Flat;
            this.btnCreateVariable.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnCreateVariable.ForeColor = Color.White;
            this.btnCreateVariable.HoverColor = Color.Empty;
            this.btnCreateVariable.Icon = null;
            this.btnCreateVariable.Location = new Point(574, 241);
            this.btnCreateVariable.Name = "btnCreateVariable";
            this.btnCreateVariable.Progress = 0;
            this.btnCreateVariable.ProgressColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(103)))), ((int)(((byte)(205)))));
            this.btnCreateVariable.Size = new Size(32, 25);
            this.btnCreateVariable.TabIndex = 11;
            this.btnCreateVariable.Text = "+";
            this.btnCreateVariable.UseVisualStyleBackColor = true;
            this.btnCreateVariable.UseWindowsAccentColor = true;
            this.btnCreateVariable.Click += new EventHandler(this.BtnCreateVariable_Click);
            // 
            // ReadVariableFromFileActionConfigView
            // 
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.Controls.Add(this.btnCreateVariable);
            this.Controls.Add(this.pictureBox1);
            this.Controls.Add(this.lblPath);
            this.Controls.Add(this.lblVariable);
            this.Controls.Add(this.btnChoosePath);
            this.Controls.Add(this.variable);
            this.Controls.Add(this.path);
            this.Name = "ReadVariableFromFileActionConfigView";
            this.Load += new EventHandler(this.ReadVariableFromFileActionConfigView_Load);
            ((ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private Label lblPath;
        private Label lblVariable;
        private ButtonPrimary btnChoosePath;
        private RoundedComboBox variable;
        private RoundedTextBox path;
        private PictureBox pictureBox1;
        private ButtonPrimary btnCreateVariable;
    }
}
