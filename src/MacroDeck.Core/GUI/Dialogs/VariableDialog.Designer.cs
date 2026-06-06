
using System.ComponentModel;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    partial class VariableDialog
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            variableType = new RoundedComboBox();
            lblType = new Label();
            btnOk = new ButtonPrimary();
            lblName = new Label();
            variableName = new RoundedTextBox();
            lblValue = new Label();
            variableValue = new RoundedTextBox();
            btnDelete = new LinkLabel();
            SuspendLayout();
            // 
            // variableType
            // 
            variableType.BackColor = Color.FromArgb(65, 65, 65);
            variableType.Cursor = Cursors.Hand;
            variableType.DropDownStyle = ComboBoxStyle.DropDownList;
            variableType.Font = new Font("Tahoma", 12F);
            variableType.Icon = null;
            variableType.Location = new Point(72, 10);
            variableType.Margin = new Padding(4);
            variableType.Name = "variableType";
            variableType.Padding = new Padding(8, 2, 8, 2);
            variableType.SelectedIndex = -1;
            variableType.SelectedItem = null;
            variableType.Size = new Size(177, 31);
            variableType.TabIndex = 3;
            variableType.SelectedIndexChanged += variableType_SelectedIndexChanged;
            // 
            // lblType
            // 
            lblType.AutoSize = true;
            lblType.Font = new Font("Tahoma", 12F);
            lblType.Location = new Point(10, 14);
            lblType.Name = "lblType";
            lblType.Size = new Size(50, 19);
            lblType.TabIndex = 4;
            lblType.Text = "Type:";
            lblType.UseMnemonic = false;
            // 
            // btnOk
            // 
            btnOk.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnOk.BorderRadius = 8;
            btnOk.Cursor = Cursors.Hand;
            btnOk.FlatAppearance.BorderSize = 0;
            btnOk.FlatStyle = FlatStyle.Flat;
            btnOk.Font = new Font("Tahoma", 9.75F);
            btnOk.ForeColor = Color.White;
            btnOk.HoverColor = Color.FromArgb(0, 89, 184);
            btnOk.Icon = null;
            btnOk.Location = new Point(278, 111);
            btnOk.Name = "btnOk";
            btnOk.Progress = 0;
            btnOk.ProgressColor = Color.FromArgb(0, 46, 94);
            btnOk.Size = new Size(75, 25);
            btnOk.TabIndex = 5;
            btnOk.Text = "Ok";
            btnOk.UseMnemonic = false;
            btnOk.UseVisualStyleBackColor = false;
            btnOk.UseWindowsAccentColor = true;
            btnOk.WriteProgress = true;
            btnOk.Click += BtnOk_Click;
            // 
            // lblName
            // 
            lblName.AutoSize = true;
            lblName.Font = new Font("Tahoma", 12F);
            lblName.Location = new Point(10, 46);
            lblName.Name = "lblName";
            lblName.Size = new Size(56, 19);
            lblName.TabIndex = 6;
            lblName.Text = "Name:";
            lblName.UseMnemonic = false;
            // 
            // variableName
            // 
            variableName.BackColor = Color.FromArgb(65, 65, 65);
            variableName.Cursor = Cursors.Hand;
            variableName.Font = new Font("Tahoma", 12F);
            variableName.ForeColor = Color.White;
            variableName.Icon = null;
            variableName.Location = new Point(72, 44);
            variableName.MaxCharacters = 32767;
            variableName.Multiline = false;
            variableName.Name = "variableName";
            variableName.Padding = new Padding(8, 5, 8, 5);
            variableName.PasswordChar = false;
            variableName.PlaceHolderColor = Color.Gray;
            variableName.PlaceHolderText = "";
            variableName.ReadOnly = false;
            variableName.ScrollBars = ScrollBars.None;
            variableName.SelectionStart = 0;
            variableName.Size = new Size(279, 30);
            variableName.TabIndex = 7;
            variableName.TextAlignment = HorizontalAlignment.Left;
            // 
            // lblValue
            // 
            lblValue.AutoSize = true;
            lblValue.Font = new Font("Tahoma", 12F);
            lblValue.Location = new Point(10, 79);
            lblValue.Name = "lblValue";
            lblValue.Size = new Size(54, 19);
            lblValue.TabIndex = 8;
            lblValue.Text = "Value:";
            lblValue.UseMnemonic = false;
            // 
            // variableValue
            // 
            variableValue.BackColor = Color.FromArgb(65, 65, 65);
            variableValue.Cursor = Cursors.Hand;
            variableValue.Font = new Font("Tahoma", 12F);
            variableValue.ForeColor = Color.White;
            variableValue.Icon = null;
            variableValue.Location = new Point(72, 77);
            variableValue.MaxCharacters = 32767;
            variableValue.Multiline = false;
            variableValue.Name = "variableValue";
            variableValue.Padding = new Padding(8, 5, 8, 5);
            variableValue.PasswordChar = false;
            variableValue.PlaceHolderColor = Color.Gray;
            variableValue.PlaceHolderText = "";
            variableValue.ReadOnly = false;
            variableValue.ScrollBars = ScrollBars.None;
            variableValue.SelectionStart = 0;
            variableValue.Size = new Size(279, 30);
            variableValue.TabIndex = 9;
            variableValue.TextAlignment = HorizontalAlignment.Left;
            // 
            // btnDelete
            // 
            btnDelete.AutoSize = true;
            btnDelete.LinkColor = Color.Silver;
            btnDelete.Location = new Point(12, 116);
            btnDelete.Name = "btnDelete";
            btnDelete.Size = new Size(92, 16);
            btnDelete.TabIndex = 10;
            btnDelete.TabStop = true;
            btnDelete.Text = "Delete variable";
            btnDelete.UseMnemonic = false;
            btnDelete.LinkClicked += BtnDelete_LinkClicked;
            // 
            // VariableDialog
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(363, 149);
            Controls.Add(btnDelete);
            Controls.Add(variableValue);
            Controls.Add(lblValue);
            Controls.Add(variableName);
            Controls.Add(lblName);
            Controls.Add(btnOk);
            Controls.Add(lblType);
            Controls.Add(variableType);
            MinimumSize = new Size(363, 136);
            Name = "VariableDialog";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Variable";
            Load += VariableDialog_Load;
            ResumeLayout(false);
            PerformLayout();
        }


        #endregion

        private RoundedComboBox variableType;
        private Label lblType;
        private ButtonPrimary btnOk;
        private Label lblName;
        private RoundedTextBox variableName;
        private Label lblValue;
        private RoundedTextBox variableValue;
        private LinkLabel btnDelete;
    }
}