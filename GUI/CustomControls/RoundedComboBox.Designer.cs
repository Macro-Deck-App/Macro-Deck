
using System;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    partial class RoundedComboBox
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
            this.borderlessComboBox1 = new SuchByte.MacroDeck.GUI.CustomControls.BorderlessComboBox();
            this.SuspendLayout();
            // 
            // borderlessComboBox1
            // 
            this.borderlessComboBox1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.borderlessComboBox1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.borderlessComboBox1.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.borderlessComboBox1.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
            this.borderlessComboBox1.ForeColor = System.Drawing.Color.White;
            this.borderlessComboBox1.FormattingEnabled = true;
            this.borderlessComboBox1.Location = new System.Drawing.Point(8, 2);
            this.borderlessComboBox1.Margin = new System.Windows.Forms.Padding(0);
            this.borderlessComboBox1.Name = "borderlessComboBox1";
            this.borderlessComboBox1.Size = new System.Drawing.Size(234, 22);
            this.borderlessComboBox1.TabIndex = 0;
            this.borderlessComboBox1.SelectedIndexChanged += new System.EventHandler(this.BorderlessComboBox1_SelectedIndexChanged);
            this.borderlessComboBox1.Click += new System.EventHandler(this.BorderlessComboBox1_Click);
            this.borderlessComboBox1.Enter += new System.EventHandler(this.BorderlessComboBox1_Enter);
            this.borderlessComboBox1.GotFocus += new System.EventHandler(this.BorderlessComboBox1_GotFocus);
            this.borderlessComboBox1.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.BorderlessComboBox1_KeyPress);
            this.borderlessComboBox1.LostFocus += new System.EventHandler(this.BorderlessComboBox1_LostFocus);
            this.borderlessComboBox1.TextChanged += new System.EventHandler(this.BorderlessComboBox1_TextChanged);
            // 
            // RoundedComboBox
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.Controls.Add(this.borderlessComboBox1);
            this.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.Name = "RoundedComboBox";
            this.Padding = new System.Windows.Forms.Padding(8, 2, 8, 2);
            this.Size = new System.Drawing.Size(250, 26);
            this.ResumeLayout(false);

        }

        


        #endregion

        private BorderlessComboBox borderlessComboBox1;
    }
}
