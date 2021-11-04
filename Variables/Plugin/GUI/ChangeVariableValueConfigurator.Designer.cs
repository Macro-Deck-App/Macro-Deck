
using SuchByte.MacroDeck.GUI.CustomControls;

namespace SuchByte.MacroDeck.Variables.Plugin.GUI
{
    partial class ChangeVariableValueConfigurator
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
            this.panel1 = new System.Windows.Forms.Panel();
            this.radioToggle = new System.Windows.Forms.RadioButton();
            this.radioSet = new System.Windows.Forms.RadioButton();
            this.radioCountDown = new System.Windows.Forms.RadioButton();
            this.radioCountUp = new System.Windows.Forms.RadioButton();
            this.variables = new SuchByte.MacroDeck.GUI.CustomControls.RoundedComboBox();
            this.value = new SuchByte.MacroDeck.GUI.CustomControls.RoundedTextBox();
            this.lblVariable = new System.Windows.Forms.Label();
            this.lblValue = new System.Windows.Forms.Label();
            this.panel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.radioToggle);
            this.panel1.Controls.Add(this.radioSet);
            this.panel1.Controls.Add(this.radioCountDown);
            this.panel1.Controls.Add(this.radioCountUp);
            this.panel1.Location = new System.Drawing.Point(3, 62);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(174, 146);
            this.panel1.TabIndex = 0;
            // 
            // radioToggle
            // 
            this.radioToggle.AutoSize = true;
            this.radioToggle.Location = new System.Drawing.Point(3, 102);
            this.radioToggle.Name = "radioToggle";
            this.radioToggle.Size = new System.Drawing.Size(85, 27);
            this.radioToggle.TabIndex = 3;
            this.radioToggle.TabStop = true;
            this.radioToggle.Text = "Toggle";
            this.radioToggle.UseVisualStyleBackColor = true;
            this.radioToggle.CheckedChanged += new System.EventHandler(this.MethodChanged);
            // 
            // radioSet
            // 
            this.radioSet.AutoSize = true;
            this.radioSet.Location = new System.Drawing.Point(3, 69);
            this.radioSet.Name = "radioSet";
            this.radioSet.Size = new System.Drawing.Size(55, 27);
            this.radioSet.TabIndex = 2;
            this.radioSet.TabStop = true;
            this.radioSet.Text = "Set";
            this.radioSet.UseVisualStyleBackColor = true;
            this.radioSet.CheckedChanged += new System.EventHandler(this.MethodChanged);
            // 
            // radioCountDown
            // 
            this.radioCountDown.AutoSize = true;
            this.radioCountDown.Location = new System.Drawing.Point(3, 36);
            this.radioCountDown.Name = "radioCountDown";
            this.radioCountDown.Size = new System.Drawing.Size(129, 27);
            this.radioCountDown.TabIndex = 1;
            this.radioCountDown.TabStop = true;
            this.radioCountDown.Text = "Count down";
            this.radioCountDown.UseVisualStyleBackColor = true;
            this.radioCountDown.CheckedChanged += new System.EventHandler(this.MethodChanged);
            // 
            // radioCountUp
            // 
            this.radioCountUp.AutoSize = true;
            this.radioCountUp.Location = new System.Drawing.Point(3, 3);
            this.radioCountUp.Name = "radioCountUp";
            this.radioCountUp.Size = new System.Drawing.Size(105, 27);
            this.radioCountUp.TabIndex = 0;
            this.radioCountUp.TabStop = true;
            this.radioCountUp.Text = "Count up";
            this.radioCountUp.UseVisualStyleBackColor = true;
            this.radioCountUp.CheckedChanged += new System.EventHandler(this.MethodChanged);
            // 
            // variables
            // 
            this.variables.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.variables.Cursor = System.Windows.Forms.Cursors.Hand;
            this.variables.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.variables.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.variables.Icon = null;
            this.variables.Location = new System.Drawing.Point(183, 120);
            this.variables.Name = "variables";
            this.variables.Padding = new System.Windows.Forms.Padding(8, 2, 8, 2);
            this.variables.SelectedIndex = -1;
            this.variables.SelectedItem = null;
            this.variables.Size = new System.Drawing.Size(348, 26);
            this.variables.TabIndex = 1;
            // 
            // value
            // 
            this.value.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.value.Cursor = System.Windows.Forms.Cursors.Hand;
            this.value.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.value.Icon = null;
            this.value.Location = new System.Drawing.Point(573, 120);
            this.value.Multiline = false;
            this.value.Name = "value";
            this.value.Padding = new System.Windows.Forms.Padding(8, 5, 8, 5);
            this.value.PasswordChar = false;
            this.value.PlaceHolderColor = System.Drawing.Color.Gray;
            this.value.PlaceHolderText = "";
            this.value.ReadOnly = false;
            this.value.SelectionStart = 0;
            this.value.Size = new System.Drawing.Size(139, 25);
            this.value.TabIndex = 2;
            this.value.TextAlignment = System.Windows.Forms.HorizontalAlignment.Left;
            this.value.Visible = false;
            // 
            // lblVariable
            // 
            this.lblVariable.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblVariable.Location = new System.Drawing.Point(183, 94);
            this.lblVariable.Name = "lblVariable";
            this.lblVariable.Size = new System.Drawing.Size(348, 23);
            this.lblVariable.TabIndex = 4;
            this.lblVariable.Text = "Variable";
            this.lblVariable.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // lblValue
            // 
            this.lblValue.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblValue.Location = new System.Drawing.Point(573, 94);
            this.lblValue.Name = "lblValue";
            this.lblValue.Size = new System.Drawing.Size(139, 23);
            this.lblValue.TabIndex = 5;
            this.lblValue.Text = "Value";
            this.lblValue.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.lblValue.Visible = false;
            // 
            // ChangeVariableValueConfigurator
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(10F, 23F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.lblValue);
            this.Controls.Add(this.lblVariable);
            this.Controls.Add(this.value);
            this.Controls.Add(this.variables);
            this.Controls.Add(this.panel1);
            this.Name = "ChangeVariableValueConfigurator";
            this.Load += new System.EventHandler(this.ChangeVariableValueConfigurator_Load);
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.RadioButton radioToggle;
        private System.Windows.Forms.RadioButton radioSet;
        private System.Windows.Forms.RadioButton radioCountDown;
        private System.Windows.Forms.RadioButton radioCountUp;
        private RoundedComboBox variables;
        private RoundedTextBox value;
        private System.Windows.Forms.Label lblVariable;
        private System.Windows.Forms.Label lblValue;
    }
}
