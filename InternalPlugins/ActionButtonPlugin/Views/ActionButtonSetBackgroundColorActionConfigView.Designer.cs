
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Properties;

namespace SuchByte.MacroDeck.InternalPlugins.ActionButtonPlugin.Views
{
    partial class ActionButtonSetBackgroundColorActionConfigView
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
            this.btnChangeColor = new RoundedButton();
            this.radioFixed = new TabRadioButton();
            this.radioRandom = new TabRadioButton();
            ((ISupportInitialize)(this.btnChangeColor)).BeginInit();
            this.SuspendLayout();
            // 
            // btnChangeColor
            // 
            this.btnChangeColor.BackColor = Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.btnChangeColor.BackgroundImage = Resources.Palette;
            this.btnChangeColor.BackgroundImageLayout = ImageLayout.Center;
            this.btnChangeColor.Column = 0;
            this.btnChangeColor.Cursor = Cursors.Hand;
            this.btnChangeColor.ForegroundImage = null;
            this.btnChangeColor.Location = new Point(378, 191);
            this.btnChangeColor.Name = "btnChangeColor";
            this.btnChangeColor.Radius = 40;
            this.btnChangeColor.Row = 0;
            this.btnChangeColor.ShowGIFIndicator = false;
            this.btnChangeColor.ShowKeyboardHotkeyIndicator = false;
            this.btnChangeColor.Size = new Size(100, 100);
            this.btnChangeColor.SizeMode = PictureBoxSizeMode.StretchImage;
            this.btnChangeColor.TabIndex = 0;
            this.btnChangeColor.TabStop = false;
            this.btnChangeColor.Click += new EventHandler(this.BtnChangeColor_Click);
            // 
            // radioFixed
            // 
            this.radioFixed.Checked = true;
            this.radioFixed.Cursor = Cursors.Hand;
            this.radioFixed.Location = new Point(258, 134);
            this.radioFixed.Name = "radioFixed";
            this.radioFixed.Size = new Size(167, 30);
            this.radioFixed.TabIndex = 1;
            this.radioFixed.TabStop = true;
            this.radioFixed.Text = "Fixed";
            this.radioFixed.UseVisualStyleBackColor = true;
            this.radioFixed.CheckedChanged += new EventHandler(this.RadioFixed_CheckedChanged);
            // 
            // radioRandom
            // 
            this.radioRandom.Cursor = Cursors.Hand;
            this.radioRandom.Location = new Point(431, 134);
            this.radioRandom.Name = "radioRandom";
            this.radioRandom.Size = new Size(167, 30);
            this.radioRandom.TabIndex = 2;
            this.radioRandom.Text = "Random";
            this.radioRandom.UseVisualStyleBackColor = true;
            this.radioRandom.CheckedChanged += new EventHandler(this.RadioRandom_CheckedChanged);
            // 
            // ActionButtonSetBackgroundColorActionConfigView
            // 
            this.AutoScaleDimensions = new SizeF(10F, 23F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.Controls.Add(this.radioRandom);
            this.Controls.Add(this.radioFixed);
            this.Controls.Add(this.btnChangeColor);
            this.Name = "ActionButtonSetBackgroundColorActionConfigView";
            this.Load += new EventHandler(this.ActionButtonSetBackgroundColorActionConfigView_Load);
            ((ISupportInitialize)(this.btnChangeColor)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private RoundedButton btnChangeColor;
        private TabRadioButton radioFixed;
        private TabRadioButton radioRandom;
    }
}
