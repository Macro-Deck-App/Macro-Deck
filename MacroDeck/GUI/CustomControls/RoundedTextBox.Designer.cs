
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    partial class RoundedTextBox
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
            this.textBox1 = new TextBox();
            this.SuspendLayout();
            // 
            // textBox1
            // 
            this.textBox1.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.textBox1.BorderStyle = BorderStyle.None;
            this.textBox1.Dock = DockStyle.Fill;
            this.textBox1.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.textBox1.ForeColor = Color.White;
            this.textBox1.Location = new Point(8, 5);
            this.textBox1.Name = "textBox1";
            this.textBox1.Size = new Size(234, 19);
            this.textBox1.TabIndex = 0;
            this.textBox1.Click += new EventHandler(this.TextBox1_Click);
            this.textBox1.TextChanged += new EventHandler(this.TextBox1_TextChanged);
            this.textBox1.Enter += new EventHandler(this.TextBox1_Enter);
            this.textBox1.GotFocus += new EventHandler(this.TextBox1_GotFocus);
            this.textBox1.KeyPress += new KeyPressEventHandler(this.TextBox1_KeyPress);
            this.textBox1.LostFocus += new EventHandler(this.TextBox1_LostFocus);
            this.textBox1.MouseEnter += new EventHandler(this.TextBox1_MouseEnter);
            this.textBox1.MouseLeave += new EventHandler(this.TextBox1_MouseLeave);
            // 
            // RoundedTextBox
            // 
            this.AutoScaleMode = AutoScaleMode.None;
            this.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.Controls.Add(this.textBox1);
            this.Cursor = Cursors.Hand;
            this.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.Name = "RoundedTextBox";
            this.Padding = new Padding(8, 5, 8, 5);
            this.Size = new Size(250, 30);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        

        #endregion

        private TextBox textBox1;
    }
}
