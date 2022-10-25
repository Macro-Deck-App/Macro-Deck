
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    partial class VariableItem
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
            this.lblName = new TextBox();
            this.lblType = new Label();
            this.lblValue = new Label();
            this.lblCreator = new Label();
            this.btnEdit = new ButtonPrimary();
            this.SuspendLayout();
            // 
            // lblName
            // 
            this.lblName.BackColor = Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.lblName.BorderStyle = BorderStyle.None;
            this.lblName.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblName.ForeColor = Color.White;
            this.lblName.Location = new Point(11, 15);
            this.lblName.Margin = new Padding(0);
            this.lblName.Name = "lblName";
            this.lblName.ReadOnly = true;
            this.lblName.Size = new Size(223, 19);
            this.lblName.TabIndex = 0;
            this.lblName.Text = "Name";
            // 
            // lblType
            // 
            this.lblType.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblType.Location = new Point(237, 3);
            this.lblType.Name = "lblType";
            this.lblType.Size = new Size(114, 40);
            this.lblType.TabIndex = 1;
            this.lblType.Text = "Type";
            this.lblType.TextAlign = ContentAlignment.MiddleLeft;
            this.lblType.UseMnemonic = false;
            // 
            // lblValue
            // 
            this.lblValue.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblValue.Location = new Point(357, 5);
            this.lblValue.Name = "lblValue";
            this.lblValue.Size = new Size(258, 40);
            this.lblValue.TabIndex = 2;
            this.lblValue.Text = "Value";
            this.lblValue.TextAlign = ContentAlignment.MiddleLeft;
            this.lblValue.UseMnemonic = false;
            // 
            // lblCreator
            // 
            this.lblCreator.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblCreator.ForeColor = Color.Silver;
            this.lblCreator.Location = new Point(621, 4);
            this.lblCreator.Name = "lblCreator";
            this.lblCreator.Size = new Size(163, 40);
            this.lblCreator.TabIndex = 3;
            this.lblCreator.Text = "Creator";
            this.lblCreator.TextAlign = ContentAlignment.MiddleLeft;
            this.lblCreator.UseMnemonic = false;
            // 
            // btnEdit
            // 
            this.btnEdit.Anchor = ((AnchorStyles)((AnchorStyles.Top | AnchorStyles.Right)));
            this.btnEdit.BorderRadius = 8;
            this.btnEdit.Cursor = Cursors.Hand;
            this.btnEdit.FlatAppearance.BorderSize = 0;
            this.btnEdit.FlatStyle = FlatStyle.Flat;
            this.btnEdit.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnEdit.ForeColor = Color.White;
            this.btnEdit.HoverColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnEdit.Icon = null;
            this.btnEdit.Location = new Point(797, 7);
            this.btnEdit.Name = "btnEdit";
            this.btnEdit.Progress = 0;
            this.btnEdit.ProgressColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnEdit.Size = new Size(35, 35);
            this.btnEdit.TabIndex = 4;
            this.btnEdit.Text = "...";
            this.btnEdit.UseMnemonic = false;
            this.btnEdit.UseVisualStyleBackColor = false;
            this.btnEdit.UseWindowsAccentColor = true;
            this.btnEdit.Click += new EventHandler(this.BtnEdit_Click);
            // 
            // VariableItem
            // 
            this.AutoScaleDimensions = new SizeF(96F, 96F);
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.Controls.Add(this.btnEdit);
            this.Controls.Add(this.lblCreator);
            this.Controls.Add(this.lblValue);
            this.Controls.Add(this.lblType);
            this.Controls.Add(this.lblName);
            this.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.ForeColor = Color.White;
            this.Name = "VariableItem";
            this.Size = new Size(840, 50);
            this.Load += new EventHandler(this.VariableItem_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private TextBox lblName;
        private Label lblType;
        private Label lblValue;
        private Label lblCreator;
        private ButtonPrimary btnEdit;
    }
}
