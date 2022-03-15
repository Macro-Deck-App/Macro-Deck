
using SuchByte.MacroDeck.GUI.CustomControls;

namespace SuchByte.MacroDeck.Variables.Plugin.GUI
{
    partial class ChangeVariableValueActionConfigView
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
            this.radioToggle = new System.Windows.Forms.RadioButton();
            this.radioSet = new System.Windows.Forms.RadioButton();
            this.radioCountDown = new System.Windows.Forms.RadioButton();
            this.radioCountUp = new System.Windows.Forms.RadioButton();
            this.variables = new SuchByte.MacroDeck.GUI.CustomControls.RoundedComboBox();
            this.value = new SuchByte.MacroDeck.GUI.CustomControls.RoundedTextBox();
            this.lblVariable = new System.Windows.Forms.Label();
            this.lblOnlyUserCreatedVariablesVisible = new System.Windows.Forms.Label();
            this.btnTemplateEditor = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            ((System.ComponentModel.ISupportInitialize)(this.btnTemplateEditor)).BeginInit();
            this.SuspendLayout();
            // 
            // radioToggle
            // 
            this.radioToggle.Cursor = System.Windows.Forms.Cursors.Hand;
            this.radioToggle.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioToggle.Location = new System.Drawing.Point(247, 210);
            this.radioToggle.Name = "radioToggle";
            this.radioToggle.Size = new System.Drawing.Size(220, 27);
            this.radioToggle.TabIndex = 3;
            this.radioToggle.TabStop = true;
            this.radioToggle.Text = "Toggle";
            this.radioToggle.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.radioToggle.UseMnemonic = false;
            this.radioToggle.UseVisualStyleBackColor = true;
            this.radioToggle.CheckedChanged += new System.EventHandler(this.MethodChanged);
            // 
            // radioSet
            // 
            this.radioSet.Cursor = System.Windows.Forms.Cursors.Hand;
            this.radioSet.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioSet.Location = new System.Drawing.Point(247, 177);
            this.radioSet.Name = "radioSet";
            this.radioSet.Size = new System.Drawing.Size(220, 27);
            this.radioSet.TabIndex = 2;
            this.radioSet.TabStop = true;
            this.radioSet.Text = "Set";
            this.radioSet.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.radioSet.UseMnemonic = false;
            this.radioSet.UseVisualStyleBackColor = true;
            this.radioSet.CheckedChanged += new System.EventHandler(this.MethodChanged);
            // 
            // radioCountDown
            // 
            this.radioCountDown.Cursor = System.Windows.Forms.Cursors.Hand;
            this.radioCountDown.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioCountDown.Location = new System.Drawing.Point(247, 144);
            this.radioCountDown.Name = "radioCountDown";
            this.radioCountDown.Size = new System.Drawing.Size(220, 27);
            this.radioCountDown.TabIndex = 1;
            this.radioCountDown.TabStop = true;
            this.radioCountDown.Text = "Count down";
            this.radioCountDown.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.radioCountDown.UseMnemonic = false;
            this.radioCountDown.UseVisualStyleBackColor = true;
            this.radioCountDown.CheckedChanged += new System.EventHandler(this.MethodChanged);
            // 
            // radioCountUp
            // 
            this.radioCountUp.Cursor = System.Windows.Forms.Cursors.Hand;
            this.radioCountUp.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioCountUp.Location = new System.Drawing.Point(247, 111);
            this.radioCountUp.Name = "radioCountUp";
            this.radioCountUp.Size = new System.Drawing.Size(220, 27);
            this.radioCountUp.TabIndex = 0;
            this.radioCountUp.TabStop = true;
            this.radioCountUp.Text = "Count up";
            this.radioCountUp.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.radioCountUp.UseMnemonic = false;
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
            this.variables.Location = new System.Drawing.Point(183, 48);
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
            this.value.Location = new System.Drawing.Point(473, 179);
            this.value.MaxCharacters = 32767;
            this.value.Multiline = false;
            this.value.Name = "value";
            this.value.Padding = new System.Windows.Forms.Padding(8, 5, 8, 5);
            this.value.PasswordChar = false;
            this.value.PlaceHolderColor = System.Drawing.Color.Gray;
            this.value.PlaceHolderText = "";
            this.value.ReadOnly = false;
            this.value.ScrollBars = System.Windows.Forms.ScrollBars.None;
            this.value.SelectionStart = 0;
            this.value.Size = new System.Drawing.Size(171, 25);
            this.value.TabIndex = 2;
            this.value.TextAlignment = System.Windows.Forms.HorizontalAlignment.Left;
            // 
            // lblVariable
            // 
            this.lblVariable.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblVariable.Location = new System.Drawing.Point(11, 48);
            this.lblVariable.Name = "lblVariable";
            this.lblVariable.Size = new System.Drawing.Size(166, 23);
            this.lblVariable.TabIndex = 4;
            this.lblVariable.Text = "Variable";
            this.lblVariable.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblVariable.UseMnemonic = false;
            // 
            // lblOnlyUserCreatedVariablesVisible
            // 
            this.lblOnlyUserCreatedVariablesVisible.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblOnlyUserCreatedVariablesVisible.Location = new System.Drawing.Point(183, 77);
            this.lblOnlyUserCreatedVariablesVisible.Name = "lblOnlyUserCreatedVariablesVisible";
            this.lblOnlyUserCreatedVariablesVisible.Size = new System.Drawing.Size(348, 31);
            this.lblOnlyUserCreatedVariablesVisible.TabIndex = 5;
            this.lblOnlyUserCreatedVariablesVisible.Text = "Only user-created variables are visible";
            this.lblOnlyUserCreatedVariablesVisible.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            this.lblOnlyUserCreatedVariablesVisible.UseMnemonic = false;
            // 
            // btnTemplateEditor
            // 
            this.btnTemplateEditor.BackColor = System.Drawing.Color.Transparent;
            this.btnTemplateEditor.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Arrow_Top_Right_Normal;
            this.btnTemplateEditor.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnTemplateEditor.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnTemplateEditor.HoverImage = global::SuchByte.MacroDeck.Properties.Resources.Arrow_Top_Right_Hover;
            this.btnTemplateEditor.Location = new System.Drawing.Point(645, 179);
            this.btnTemplateEditor.Name = "btnTemplateEditor";
            this.btnTemplateEditor.Size = new System.Drawing.Size(25, 25);
            this.btnTemplateEditor.TabIndex = 6;
            this.btnTemplateEditor.TabStop = false;
            this.btnTemplateEditor.Click += new System.EventHandler(this.btnTemplateEditor_Click);
            // 
            // ChangeVariableValueConfigurator
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(10F, 23F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.btnTemplateEditor);
            this.Controls.Add(this.lblOnlyUserCreatedVariablesVisible);
            this.Controls.Add(this.radioToggle);
            this.Controls.Add(this.radioSet);
            this.Controls.Add(this.lblVariable);
            this.Controls.Add(this.radioCountDown);
            this.Controls.Add(this.value);
            this.Controls.Add(this.radioCountUp);
            this.Controls.Add(this.variables);
            this.Name = "ChangeVariableValueConfigurator";
            this.Load += new System.EventHandler(this.ChangeVariableValueConfigurator_Load);
            ((System.ComponentModel.ISupportInitialize)(this.btnTemplateEditor)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.RadioButton radioToggle;
        private System.Windows.Forms.RadioButton radioSet;
        private System.Windows.Forms.RadioButton radioCountDown;
        private System.Windows.Forms.RadioButton radioCountUp;
        private RoundedComboBox variables;
        private RoundedTextBox value;
        private System.Windows.Forms.Label lblVariable;
        private System.Windows.Forms.Label lblOnlyUserCreatedVariablesVisible;
        private PictureButton btnTemplateEditor;
    }
}
