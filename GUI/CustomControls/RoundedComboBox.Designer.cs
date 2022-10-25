
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    partial class RoundedComboBox
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
            this.borderlessComboBox1 = new BorderlessComboBox();
            this.SuspendLayout();
            // 
            // borderlessComboBox1
            // 
            this.borderlessComboBox1.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.borderlessComboBox1.Dock = DockStyle.Fill;
            this.borderlessComboBox1.DropDownStyle = ComboBoxStyle.DropDownList;
            this.borderlessComboBox1.FlatStyle = FlatStyle.Popup;
            this.borderlessComboBox1.ForeColor = Color.White;
            this.borderlessComboBox1.FormattingEnabled = true;
            this.borderlessComboBox1.Location = new Point(8, 2);
            this.borderlessComboBox1.Margin = new Padding(0);
            this.borderlessComboBox1.Name = "borderlessComboBox1";
            this.borderlessComboBox1.Size = new Size(234, 22);
            this.borderlessComboBox1.TabIndex = 0;
            this.borderlessComboBox1.SelectedIndexChanged += new EventHandler(this.BorderlessComboBox1_SelectedIndexChanged);
            this.borderlessComboBox1.Click += new EventHandler(this.BorderlessComboBox1_Click);
            this.borderlessComboBox1.Enter += new EventHandler(this.BorderlessComboBox1_Enter);
            this.borderlessComboBox1.GotFocus += new EventHandler(this.BorderlessComboBox1_GotFocus);
            this.borderlessComboBox1.KeyPress += new KeyPressEventHandler(this.BorderlessComboBox1_KeyPress);
            this.borderlessComboBox1.LostFocus += new EventHandler(this.BorderlessComboBox1_LostFocus);
            this.borderlessComboBox1.TextChanged += new EventHandler(this.BorderlessComboBox1_TextChanged);
            // 
            // RoundedComboBox
            // 
            this.AutoScaleMode = AutoScaleMode.None;
            this.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.Controls.Add(this.borderlessComboBox1);
            this.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.Name = "RoundedComboBox";
            this.Padding = new Padding(8, 2, 8, 2);
            this.Size = new Size(250, 26);
            this.ResumeLayout(false);

        }

        


        #endregion

        private BorderlessComboBox borderlessComboBox1;
    }
}
