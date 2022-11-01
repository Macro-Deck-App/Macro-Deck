
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    partial class InitialSetupIconPackItem
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
            this.checkInstall = new CheckBox();
            this.lblDescription = new Label();
            this.preview = new PictureBox();
            this.lblName = new Label();
            ((ISupportInitialize)(this.preview)).BeginInit();
            this.SuspendLayout();
            // 
            // checkInstall
            // 
            this.checkInstall.CheckAlign = ContentAlignment.MiddleCenter;
            this.checkInstall.Font = new Font("Tahoma", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.checkInstall.Location = new Point(3, 5);
            this.checkInstall.Name = "checkInstall";
            this.checkInstall.Size = new Size(25, 42);
            this.checkInstall.TabIndex = 12;
            this.checkInstall.UseMnemonic = false;
            this.checkInstall.UseVisualStyleBackColor = true;
            // 
            // lblDescription
            // 
            this.lblDescription.Location = new Point(81, 21);
            this.lblDescription.Name = "lblDescription";
            this.lblDescription.Size = new Size(325, 29);
            this.lblDescription.TabIndex = 11;
            this.lblDescription.Text = "label1";
            this.lblDescription.UseMnemonic = false;
            // 
            // preview
            // 
            this.preview.BackgroundImageLayout = ImageLayout.Stretch;
            this.preview.Location = new Point(34, 8);
            this.preview.Name = "preview";
            this.preview.Size = new Size(35, 35);
            this.preview.TabIndex = 10;
            this.preview.TabStop = false;
            // 
            // lblName
            // 
            this.lblName.Font = new Font("Tahoma", 9.75F, FontStyle.Bold, GraphicsUnit.Point);
            this.lblName.Location = new Point(81, 3);
            this.lblName.Name = "lblName";
            this.lblName.Size = new Size(325, 18);
            this.lblName.TabIndex = 8;
            this.lblName.Text = "label1";
            this.lblName.UseMnemonic = false;
            // 
            // InitialSetupIconPackItem
            // 
            
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.Controls.Add(this.checkInstall);
            this.Controls.Add(this.lblDescription);
            this.Controls.Add(this.preview);
            this.Controls.Add(this.lblName);
            this.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.ForeColor = Color.White;
            this.Name = "InitialSetupIconPackItem";
            this.Size = new Size(411, 52);
            ((ISupportInitialize)(this.preview)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion
        private CheckBox checkInstall;
        private Label lblDescription;
        private PictureBox preview;
        private Label lblName;
    }
}
