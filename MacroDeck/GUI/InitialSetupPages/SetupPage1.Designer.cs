
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.Properties;

namespace SuchByte.MacroDeck.GUI.InitialSetupPages
{
    partial class SetupPage1
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
            this.lblWelcome = new Label();
            this.pictureBox1 = new PictureBox();
            this.lblLetsConfigure = new Label();
            this.languages = new ListBox();
            this.lblSelectLanguage = new Label();
            ((ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // lblWelcome
            // 
            this.lblWelcome.Font = new Font("Tahoma", 26.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblWelcome.ForeColor = Color.White;
            this.lblWelcome.Location = new Point(129, 3);
            this.lblWelcome.Name = "lblWelcome";
            this.lblWelcome.Size = new Size(559, 109);
            this.lblWelcome.TabIndex = 0;
            this.lblWelcome.Text = "Welcome to Macro Deck 2!";
            this.lblWelcome.TextAlign = ContentAlignment.MiddleCenter;
            this.lblWelcome.UseMnemonic = false;
            // 
            // pictureBox1
            // 
            this.pictureBox1.BackgroundImageLayout = ImageLayout.Stretch;
            this.pictureBox1.Image = Resources.Macro_Deck_2021;
            this.pictureBox1.Location = new Point(3, 3);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new Size(120, 112);
            this.pictureBox1.SizeMode = PictureBoxSizeMode.StretchImage;
            this.pictureBox1.TabIndex = 1;
            this.pictureBox1.TabStop = false;
            // 
            // lblLetsConfigure
            // 
            this.lblLetsConfigure.Font = new Font("Tahoma", 18F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblLetsConfigure.ForeColor = Color.White;
            this.lblLetsConfigure.Location = new Point(3, 118);
            this.lblLetsConfigure.Name = "lblLetsConfigure";
            this.lblLetsConfigure.Size = new Size(685, 67);
            this.lblLetsConfigure.TabIndex = 2;
            this.lblLetsConfigure.Text = "Let\'s configure your Macro Deck experience";
            this.lblLetsConfigure.TextAlign = ContentAlignment.MiddleCenter;
            this.lblLetsConfigure.UseMnemonic = false;
            // 
            // languages
            // 
            this.languages.BackColor = Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.languages.BorderStyle = BorderStyle.FixedSingle;
            this.languages.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.languages.ForeColor = Color.White;
            this.languages.FormattingEnabled = true;
            this.languages.ItemHeight = 18;
            this.languages.Location = new Point(231, 297);
            this.languages.Name = "languages";
            this.languages.Size = new Size(228, 218);
            this.languages.TabIndex = 4;
            this.languages.SelectedIndexChanged += new EventHandler(this.Languages_SelectedIndexChanged);
            // 
            // lblSelectLanguage
            // 
            this.lblSelectLanguage.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblSelectLanguage.ForeColor = Color.White;
            this.lblSelectLanguage.Location = new Point(231, 264);
            this.lblSelectLanguage.Name = "lblSelectLanguage";
            this.lblSelectLanguage.Size = new Size(228, 26);
            this.lblSelectLanguage.TabIndex = 5;
            this.lblSelectLanguage.Text = "Select your language";
            this.lblSelectLanguage.TextAlign = ContentAlignment.MiddleCenter;
            this.lblSelectLanguage.UseMnemonic = false;
            // 
            // SetupPage1
            // 
            
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.lblSelectLanguage);
            this.Controls.Add(this.languages);
            this.Controls.Add(this.lblLetsConfigure);
            this.Controls.Add(this.pictureBox1);
            this.Controls.Add(this.lblWelcome);
            this.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.Name = "SetupPage1";
            this.Size = new Size(691, 571);
            this.Load += new EventHandler(this.SetupPage1_Load);
            ((ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private Label lblWelcome;
        private PictureBox pictureBox1;
        private Label lblLetsConfigure;
        private ListBox languages;
        private Label lblSelectLanguage;
    }
}
