
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Properties;

namespace SuchByte.MacroDeck.Variables.Plugin.GUI
{
    partial class ChangeVariableValueActionConfigView
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
            this.radioToggle = new RadioButton();
            this.radioSet = new RadioButton();
            this.radioCountDown = new RadioButton();
            this.radioCountUp = new RadioButton();
            this.variables = new RoundedComboBox();
            this.value = new RoundedTextBox();
            this.lblVariable = new Label();
            this.lblOnlyUserCreatedVariablesVisible = new Label();
            this.btnTemplateEditor = new PictureButton();
            ((ISupportInitialize)(this.btnTemplateEditor)).BeginInit();
            this.SuspendLayout();
            // 
            // radioToggle
            // 
            this.radioToggle.Cursor = Cursors.Hand;
            this.radioToggle.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.radioToggle.Location = new Point(247, 210);
            this.radioToggle.Name = "radioToggle";
            this.radioToggle.Size = new Size(220, 27);
            this.radioToggle.TabIndex = 3;
            this.radioToggle.TabStop = true;
            this.radioToggle.Text = "Toggle";
            this.radioToggle.TextAlign = ContentAlignment.MiddleCenter;
            this.radioToggle.UseMnemonic = false;
            this.radioToggle.UseVisualStyleBackColor = true;
            this.radioToggle.CheckedChanged += new EventHandler(this.MethodChanged);
            // 
            // radioSet
            // 
            this.radioSet.Cursor = Cursors.Hand;
            this.radioSet.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.radioSet.Location = new Point(247, 177);
            this.radioSet.Name = "radioSet";
            this.radioSet.Size = new Size(220, 27);
            this.radioSet.TabIndex = 2;
            this.radioSet.TabStop = true;
            this.radioSet.Text = "Set";
            this.radioSet.TextAlign = ContentAlignment.MiddleCenter;
            this.radioSet.UseMnemonic = false;
            this.radioSet.UseVisualStyleBackColor = true;
            this.radioSet.CheckedChanged += new EventHandler(this.MethodChanged);
            // 
            // radioCountDown
            // 
            this.radioCountDown.Cursor = Cursors.Hand;
            this.radioCountDown.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.radioCountDown.Location = new Point(247, 144);
            this.radioCountDown.Name = "radioCountDown";
            this.radioCountDown.Size = new Size(220, 27);
            this.radioCountDown.TabIndex = 1;
            this.radioCountDown.TabStop = true;
            this.radioCountDown.Text = "Count down";
            this.radioCountDown.TextAlign = ContentAlignment.MiddleCenter;
            this.radioCountDown.UseMnemonic = false;
            this.radioCountDown.UseVisualStyleBackColor = true;
            this.radioCountDown.CheckedChanged += new EventHandler(this.MethodChanged);
            // 
            // radioCountUp
            // 
            this.radioCountUp.Cursor = Cursors.Hand;
            this.radioCountUp.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.radioCountUp.Location = new Point(247, 111);
            this.radioCountUp.Name = "radioCountUp";
            this.radioCountUp.Size = new Size(220, 27);
            this.radioCountUp.TabIndex = 0;
            this.radioCountUp.TabStop = true;
            this.radioCountUp.Text = "Count up";
            this.radioCountUp.TextAlign = ContentAlignment.MiddleCenter;
            this.radioCountUp.UseMnemonic = false;
            this.radioCountUp.UseVisualStyleBackColor = true;
            this.radioCountUp.CheckedChanged += new EventHandler(this.MethodChanged);
            // 
            // variables
            // 
            this.variables.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.variables.Cursor = Cursors.Hand;
            this.variables.DropDownStyle = ComboBoxStyle.DropDownList;
            this.variables.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.variables.Icon = null;
            this.variables.Location = new Point(183, 48);
            this.variables.Name = "variables";
            this.variables.Padding = new Padding(8, 2, 8, 2);
            this.variables.SelectedIndex = -1;
            this.variables.SelectedItem = null;
            this.variables.Size = new Size(348, 26);
            this.variables.TabIndex = 1;
            // 
            // value
            // 
            this.value.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.value.Cursor = Cursors.Hand;
            this.value.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.value.Icon = null;
            this.value.Location = new Point(473, 179);
            this.value.MaxCharacters = 32767;
            this.value.Multiline = false;
            this.value.Name = "value";
            this.value.Padding = new Padding(8, 5, 8, 5);
            this.value.PasswordChar = false;
            this.value.PlaceHolderColor = Color.Gray;
            this.value.PlaceHolderText = "";
            this.value.ReadOnly = false;
            this.value.ScrollBars = ScrollBars.None;
            this.value.SelectionStart = 0;
            this.value.Size = new Size(171, 25);
            this.value.TabIndex = 2;
            this.value.TextAlignment = HorizontalAlignment.Left;
            // 
            // lblVariable
            // 
            this.lblVariable.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblVariable.Location = new Point(11, 48);
            this.lblVariable.Name = "lblVariable";
            this.lblVariable.Size = new Size(166, 23);
            this.lblVariable.TabIndex = 4;
            this.lblVariable.Text = "Variable";
            this.lblVariable.TextAlign = ContentAlignment.MiddleRight;
            this.lblVariable.UseMnemonic = false;
            // 
            // lblOnlyUserCreatedVariablesVisible
            // 
            this.lblOnlyUserCreatedVariablesVisible.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblOnlyUserCreatedVariablesVisible.Location = new Point(183, 77);
            this.lblOnlyUserCreatedVariablesVisible.Name = "lblOnlyUserCreatedVariablesVisible";
            this.lblOnlyUserCreatedVariablesVisible.Size = new Size(348, 31);
            this.lblOnlyUserCreatedVariablesVisible.TabIndex = 5;
            this.lblOnlyUserCreatedVariablesVisible.Text = "Only user-created variables are visible";
            this.lblOnlyUserCreatedVariablesVisible.TextAlign = ContentAlignment.TopCenter;
            this.lblOnlyUserCreatedVariablesVisible.UseMnemonic = false;
            // 
            // btnTemplateEditor
            // 
            this.btnTemplateEditor.BackColor = Color.Transparent;
            this.btnTemplateEditor.BackgroundImage = Resources.Arrow_Top_Right_Normal;
            this.btnTemplateEditor.BackgroundImageLayout = ImageLayout.Stretch;
            this.btnTemplateEditor.Cursor = Cursors.Hand;
            this.btnTemplateEditor.HoverImage = Resources.Arrow_Top_Right_Hover;
            this.btnTemplateEditor.Location = new Point(645, 179);
            this.btnTemplateEditor.Name = "btnTemplateEditor";
            this.btnTemplateEditor.Size = new Size(25, 25);
            this.btnTemplateEditor.TabIndex = 6;
            this.btnTemplateEditor.TabStop = false;
            this.btnTemplateEditor.Click += new EventHandler(this.BtnTemplateEditor_Click);
            // 
            // ChangeVariableValueConfigurator
            // 
            this.AutoScaleMode = AutoScaleMode.Dpi;
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
            this.Load += new EventHandler(this.ChangeVariableValueConfigurator_Load);
            ((ISupportInitialize)(this.btnTemplateEditor)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion
        private RadioButton radioToggle;
        private RadioButton radioSet;
        private RadioButton radioCountDown;
        private RadioButton radioCountUp;
        private RoundedComboBox variables;
        private RoundedTextBox value;
        private Label lblVariable;
        private Label lblOnlyUserCreatedVariablesVisible;
        private PictureButton btnTemplateEditor;
    }
}
