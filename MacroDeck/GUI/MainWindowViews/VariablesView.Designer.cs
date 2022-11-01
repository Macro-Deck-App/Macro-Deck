
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Variables;

namespace SuchByte.MacroDeck.GUI.MainWindowContents
{
    partial class VariablesView
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
            VariableManager.OnVariableChanged -= this.VariableChanged;
            VariableManager.OnVariableRemoved -= this.VariableRemoved;
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
            this.variablesPanel = new FlowLayoutPanel();
            this.lblCreator = new Label();
            this.lblValue = new Label();
            this.lblType = new Label();
            this.lblName = new Label();
            this.btnCreateVariable = new ButtonPrimary();
            this.creatorFilter = new FlowLayoutPanel();
            this.SuspendLayout();
            // 
            // variablesPanel
            // 
            this.variablesPanel.Anchor = ((AnchorStyles)((((AnchorStyles.Top | AnchorStyles.Bottom) 
                                                           | AnchorStyles.Left) 
                                                          | AnchorStyles.Right)));
            this.variablesPanel.AutoScroll = true;
            this.variablesPanel.Location = new Point(240, 53);
            this.variablesPanel.Name = "variablesPanel";
            this.variablesPanel.Size = new Size(897, 451);
            this.variablesPanel.TabIndex = 11;
            // 
            // lblCreator
            // 
            this.lblCreator.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblCreator.Location = new Point(860, 0);
            this.lblCreator.Name = "lblCreator";
            this.lblCreator.Size = new Size(163, 50);
            this.lblCreator.TabIndex = 15;
            this.lblCreator.Text = "Creator";
            this.lblCreator.TextAlign = ContentAlignment.MiddleLeft;
            this.lblCreator.UseMnemonic = false;
            // 
            // lblValue
            // 
            this.lblValue.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblValue.Location = new Point(596, 0);
            this.lblValue.Name = "lblValue";
            this.lblValue.Size = new Size(258, 50);
            this.lblValue.TabIndex = 14;
            this.lblValue.Text = "Value";
            this.lblValue.TextAlign = ContentAlignment.MiddleLeft;
            this.lblValue.UseMnemonic = false;
            // 
            // lblType
            // 
            this.lblType.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblType.Location = new Point(476, 0);
            this.lblType.Name = "lblType";
            this.lblType.Size = new Size(114, 50);
            this.lblType.TabIndex = 13;
            this.lblType.Text = "Type";
            this.lblType.TextAlign = ContentAlignment.MiddleLeft;
            this.lblType.UseMnemonic = false;
            // 
            // lblName
            // 
            this.lblName.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblName.Location = new Point(247, 0);
            this.lblName.Name = "lblName";
            this.lblName.Size = new Size(223, 50);
            this.lblName.TabIndex = 12;
            this.lblName.Text = "Name";
            this.lblName.TextAlign = ContentAlignment.MiddleLeft;
            this.lblName.UseMnemonic = false;
            // 
            // btnCreateVariable
            // 
            this.btnCreateVariable.Anchor = ((AnchorStyles)((AnchorStyles.Bottom | AnchorStyles.Right)));
            this.btnCreateVariable.BorderRadius = 8;
            this.btnCreateVariable.Cursor = Cursors.Hand;
            this.btnCreateVariable.FlatAppearance.BorderSize = 0;
            this.btnCreateVariable.FlatStyle = FlatStyle.Flat;
            this.btnCreateVariable.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnCreateVariable.ForeColor = Color.White;
            this.btnCreateVariable.HoverColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnCreateVariable.Icon = null;
            this.btnCreateVariable.Location = new Point(1014, 510);
            this.btnCreateVariable.Name = "btnCreateVariable";
            this.btnCreateVariable.Progress = 0;
            this.btnCreateVariable.ProgressColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnCreateVariable.Size = new Size(120, 27);
            this.btnCreateVariable.TabIndex = 16;
            this.btnCreateVariable.Text = "Create variable";
            this.btnCreateVariable.UseMnemonic = false;
            this.btnCreateVariable.UseVisualStyleBackColor = false;
            this.btnCreateVariable.UseWindowsAccentColor = true;
            this.btnCreateVariable.Click += new EventHandler(this.BtnCreateVariable_Click);
            // 
            // creatorFilter
            // 
            this.creatorFilter.Anchor = ((AnchorStyles)(((AnchorStyles.Top | AnchorStyles.Bottom) 
                                                         | AnchorStyles.Left)));
            this.creatorFilter.AutoScroll = true;
            this.creatorFilter.Location = new Point(0, 53);
            this.creatorFilter.Name = "creatorFilter";
            this.creatorFilter.Size = new Size(234, 451);
            this.creatorFilter.TabIndex = 17;
            // 
            // VariablesView
            // 
            this.AutoScaleDimensions = new SizeF(96F, 96F);
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.creatorFilter);
            this.Controls.Add(this.btnCreateVariable);
            this.Controls.Add(this.lblCreator);
            this.Controls.Add(this.lblValue);
            this.Controls.Add(this.lblType);
            this.Controls.Add(this.lblName);
            this.Controls.Add(this.variablesPanel);
            this.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.ForeColor = Color.White;
            this.Name = "VariablesView";
            this.Size = new Size(1137, 540);
            this.Load += new EventHandler(this.VariablesPage_Load);
            this.ResumeLayout(false);

        }

        #endregion
        private FlowLayoutPanel variablesPanel;
        private Label lblCreator;
        private Label lblValue;
        private Label lblType;
        private Label lblName;
        private ButtonPrimary btnCreateVariable;
        private FlowLayoutPanel creatorFilter;
    }
}
