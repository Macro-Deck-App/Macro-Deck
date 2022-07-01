
namespace SuchByte.MacroDeck.InternalPlugins.ActionButtonPlugin.Views
{
    partial class ActionButtonSetBackgroundColorActionConfigView
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
            this.btnChangeColor = new SuchByte.MacroDeck.GUI.CustomControls.RoundedButton();
            this.radioFixed = new SuchByte.MacroDeck.GUI.CustomControls.TabRadioButton();
            this.radioRandom = new SuchByte.MacroDeck.GUI.CustomControls.TabRadioButton();
            ((System.ComponentModel.ISupportInitialize)(this.btnChangeColor)).BeginInit();
            this.SuspendLayout();
            // 
            // btnChangeColor
            // 
            this.btnChangeColor.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.btnChangeColor.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Palette;
            this.btnChangeColor.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Center;
            this.btnChangeColor.Column = 0;
            this.btnChangeColor.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnChangeColor.ForegroundImage = null;
            this.btnChangeColor.Location = new System.Drawing.Point(378, 191);
            this.btnChangeColor.Name = "btnChangeColor";
            this.btnChangeColor.Radius = 40;
            this.btnChangeColor.Row = 0;
            this.btnChangeColor.ShowGIFIndicator = false;
            this.btnChangeColor.ShowKeyboardHotkeyIndicator = false;
            this.btnChangeColor.Size = new System.Drawing.Size(100, 100);
            this.btnChangeColor.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.btnChangeColor.TabIndex = 0;
            this.btnChangeColor.TabStop = false;
            this.btnChangeColor.Click += new System.EventHandler(this.BtnChangeColor_Click);
            // 
            // radioFixed
            // 
            this.radioFixed.Checked = true;
            this.radioFixed.Cursor = System.Windows.Forms.Cursors.Hand;
            this.radioFixed.Location = new System.Drawing.Point(258, 134);
            this.radioFixed.Name = "radioFixed";
            this.radioFixed.Size = new System.Drawing.Size(167, 30);
            this.radioFixed.TabIndex = 1;
            this.radioFixed.TabStop = true;
            this.radioFixed.Text = "Fixed";
            this.radioFixed.UseVisualStyleBackColor = true;
            this.radioFixed.CheckedChanged += new System.EventHandler(this.RadioFixed_CheckedChanged);
            // 
            // radioRandom
            // 
            this.radioRandom.Cursor = System.Windows.Forms.Cursors.Hand;
            this.radioRandom.Location = new System.Drawing.Point(431, 134);
            this.radioRandom.Name = "radioRandom";
            this.radioRandom.Size = new System.Drawing.Size(167, 30);
            this.radioRandom.TabIndex = 2;
            this.radioRandom.Text = "Random";
            this.radioRandom.UseVisualStyleBackColor = true;
            this.radioRandom.CheckedChanged += new System.EventHandler(this.RadioRandom_CheckedChanged);
            // 
            // ActionButtonSetBackgroundColorActionConfigView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(10F, 23F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.radioRandom);
            this.Controls.Add(this.radioFixed);
            this.Controls.Add(this.btnChangeColor);
            this.Name = "ActionButtonSetBackgroundColorActionConfigView";
            this.Load += new System.EventHandler(this.ActionButtonSetBackgroundColorActionConfigView_Load);
            ((System.ComponentModel.ISupportInitialize)(this.btnChangeColor)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private GUI.CustomControls.RoundedButton btnChangeColor;
        private GUI.CustomControls.TabRadioButton radioFixed;
        private GUI.CustomControls.TabRadioButton radioRandom;
    }
}
