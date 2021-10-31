
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
            this.radioDecrement = new System.Windows.Forms.RadioButton();
            this.radioIncrement = new System.Windows.Forms.RadioButton();
            this.variables = new System.Windows.Forms.ComboBox();
            this.value = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.lblValue = new System.Windows.Forms.Label();
            this.panel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.radioToggle);
            this.panel1.Controls.Add(this.radioSet);
            this.panel1.Controls.Add(this.radioDecrement);
            this.panel1.Controls.Add(this.radioIncrement);
            this.panel1.Location = new System.Drawing.Point(3, 62);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(141, 146);
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
            // radioDecrement
            // 
            this.radioDecrement.AutoSize = true;
            this.radioDecrement.Location = new System.Drawing.Point(3, 36);
            this.radioDecrement.Name = "radioDecrement";
            this.radioDecrement.Size = new System.Drawing.Size(120, 27);
            this.radioDecrement.TabIndex = 1;
            this.radioDecrement.TabStop = true;
            this.radioDecrement.Text = "Decrement";
            this.radioDecrement.UseVisualStyleBackColor = true;
            this.radioDecrement.CheckedChanged += new System.EventHandler(this.MethodChanged);
            // 
            // radioIncrement
            // 
            this.radioIncrement.AutoSize = true;
            this.radioIncrement.Location = new System.Drawing.Point(3, 3);
            this.radioIncrement.Name = "radioIncrement";
            this.radioIncrement.Size = new System.Drawing.Size(115, 27);
            this.radioIncrement.TabIndex = 0;
            this.radioIncrement.TabStop = true;
            this.radioIncrement.Text = "Increment";
            this.radioIncrement.UseVisualStyleBackColor = true;
            this.radioIncrement.CheckedChanged += new System.EventHandler(this.MethodChanged);
            // 
            // variables
            // 
            this.variables.AutoCompleteMode = System.Windows.Forms.AutoCompleteMode.SuggestAppend;
            this.variables.AutoCompleteSource = System.Windows.Forms.AutoCompleteSource.ListItems;
            this.variables.FormattingEnabled = true;
            this.variables.Location = new System.Drawing.Point(183, 120);
            this.variables.Name = "variables";
            this.variables.Size = new System.Drawing.Size(348, 31);
            this.variables.TabIndex = 1;
            this.variables.SelectedIndexChanged += new System.EventHandler(this.variables_SelectedIndexChanged);
            // 
            // value
            // 
            this.value.AutoCompleteCustomSource.AddRange(new string[] {
            "True",
            "False",
            "On",
            "Off"});
            this.value.AutoCompleteMode = System.Windows.Forms.AutoCompleteMode.Suggest;
            this.value.AutoCompleteSource = System.Windows.Forms.AutoCompleteSource.CustomSource;
            this.value.Location = new System.Drawing.Point(573, 120);
            this.value.Name = "value";
            this.value.Size = new System.Drawing.Size(139, 30);
            this.value.TabIndex = 2;
            this.value.Visible = false;
            this.value.TextChanged += new System.EventHandler(this.value_TextChanged);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(33, 36);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(73, 23);
            this.label1.TabIndex = 3;
            this.label1.Text = "Method";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(319, 36);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(77, 23);
            this.label2.TabIndex = 4;
            this.label2.Text = "Variable";
            // 
            // lblValue
            // 
            this.lblValue.AutoSize = true;
            this.lblValue.Location = new System.Drawing.Point(614, 36);
            this.lblValue.Name = "lblValue";
            this.lblValue.Size = new System.Drawing.Size(56, 23);
            this.lblValue.TabIndex = 5;
            this.lblValue.Text = "Value";
            this.lblValue.Visible = false;
            // 
            // ChangeVariableValueConfigurator
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(10F, 23F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.lblValue);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.value);
            this.Controls.Add(this.variables);
            this.Controls.Add(this.panel1);
            this.Name = "ChangeVariableValueConfigurator";
            this.Load += new System.EventHandler(this.ChangeVariableValueConfigurator_Load);
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.RadioButton radioToggle;
        private System.Windows.Forms.RadioButton radioSet;
        private System.Windows.Forms.RadioButton radioDecrement;
        private System.Windows.Forms.RadioButton radioIncrement;
        private System.Windows.Forms.ComboBox variables;
        private System.Windows.Forms.TextBox value;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label lblValue;
    }
}
