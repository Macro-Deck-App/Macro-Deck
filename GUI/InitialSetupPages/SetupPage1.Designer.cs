
namespace SuchByte.MacroDeck.GUI.InitialSetupPages
{
    partial class SetupPage1
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
            this.lblWelcome = new System.Windows.Forms.Label();
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.lblLetsConfigure = new System.Windows.Forms.Label();
            this.languages = new System.Windows.Forms.ListBox();
            this.lblSelectLanguage = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // lblWelcome
            // 
            this.lblWelcome.Font = new System.Drawing.Font("Tahoma", 26.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblWelcome.ForeColor = System.Drawing.Color.White;
            this.lblWelcome.Location = new System.Drawing.Point(129, 3);
            this.lblWelcome.Name = "lblWelcome";
            this.lblWelcome.Size = new System.Drawing.Size(559, 109);
            this.lblWelcome.TabIndex = 0;
            this.lblWelcome.Text = "Welcome to Macro Deck 2!";
            this.lblWelcome.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // pictureBox1
            // 
            this.pictureBox1.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.pictureBox1.Image = global::SuchByte.MacroDeck.Properties.Resources.Macro_Deck_2021;
            this.pictureBox1.Location = new System.Drawing.Point(3, 3);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(120, 112);
            this.pictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.pictureBox1.TabIndex = 1;
            this.pictureBox1.TabStop = false;
            // 
            // lblLetsConfigure
            // 
            this.lblLetsConfigure.Font = new System.Drawing.Font("Tahoma", 18F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblLetsConfigure.ForeColor = System.Drawing.Color.White;
            this.lblLetsConfigure.Location = new System.Drawing.Point(3, 118);
            this.lblLetsConfigure.Name = "lblLetsConfigure";
            this.lblLetsConfigure.Size = new System.Drawing.Size(685, 67);
            this.lblLetsConfigure.TabIndex = 2;
            this.lblLetsConfigure.Text = "Let\'s configure your Macro Deck experience";
            this.lblLetsConfigure.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // languages
            // 
            this.languages.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.languages.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.languages.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.languages.ForeColor = System.Drawing.Color.White;
            this.languages.FormattingEnabled = true;
            this.languages.ItemHeight = 18;
            this.languages.Location = new System.Drawing.Point(231, 297);
            this.languages.Name = "languages";
            this.languages.Size = new System.Drawing.Size(228, 218);
            this.languages.TabIndex = 4;
            this.languages.SelectedIndexChanged += new System.EventHandler(this.Languages_SelectedIndexChanged);
            // 
            // lblSelectLanguage
            // 
            this.lblSelectLanguage.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblSelectLanguage.ForeColor = System.Drawing.Color.White;
            this.lblSelectLanguage.Location = new System.Drawing.Point(231, 264);
            this.lblSelectLanguage.Name = "lblSelectLanguage";
            this.lblSelectLanguage.Size = new System.Drawing.Size(228, 26);
            this.lblSelectLanguage.TabIndex = 5;
            this.lblSelectLanguage.Text = "Select your language";
            this.lblSelectLanguage.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // SetupPage1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.lblSelectLanguage);
            this.Controls.Add(this.languages);
            this.Controls.Add(this.lblLetsConfigure);
            this.Controls.Add(this.pictureBox1);
            this.Controls.Add(this.lblWelcome);
            this.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.Name = "SetupPage1";
            this.Size = new System.Drawing.Size(691, 571);
            this.Load += new System.EventHandler(this.SetupPage1_Load);
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label lblWelcome;
        private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.Label lblLetsConfigure;
        private System.Windows.Forms.ListBox languages;
        private System.Windows.Forms.Label lblSelectLanguage;
    }
}
