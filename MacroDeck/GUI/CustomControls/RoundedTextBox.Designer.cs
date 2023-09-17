
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
            textBox1 = new TextBox();
            SuspendLayout();
            // 
            // textBox1
            // 
            textBox1.BackColor = Color.FromArgb(65, 65, 65);
            textBox1.BorderStyle = BorderStyle.None;
            textBox1.Dock = DockStyle.Fill;
            textBox1.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            textBox1.ForeColor = Color.White;
            textBox1.Location = new Point(8, 5);
            textBox1.Name = "textBox1";
            textBox1.Size = new Size(234, 19);
            textBox1.TabIndex = 0;
            textBox1.Click += TextBox1_Click;
            textBox1.TextChanged += TextBox1_TextChanged;
            textBox1.Enter += TextBox1_Enter;
            textBox1.GotFocus += TextBox1_GotFocus;
            textBox1.KeyPress += TextBox1_KeyPress;
            textBox1.LostFocus += TextBox1_LostFocus;
            textBox1.MouseEnter += TextBox1_MouseEnter;
            textBox1.MouseLeave += TextBox1_MouseLeave;
            // 
            // RoundedTextBox
            // 
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(65, 65, 65);
            Controls.Add(textBox1);
            Cursor = Cursors.Hand;
            Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            Name = "RoundedTextBox";
            Padding = new Padding(8, 5, 8, 5);
            Size = new Size(250, 30);
            ResumeLayout(false);
            PerformLayout();
        }



        #endregion

        private TextBox textBox1;
    }
}
