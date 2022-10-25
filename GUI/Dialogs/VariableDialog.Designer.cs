
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
            this.variableType = new SuchByte.MacroDeck.GUI.CustomControls.RoundedComboBox();
            this.lblType = new System.Windows.Forms.Label();
            this.btnOk = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.lblName = new System.Windows.Forms.Label();
            this.variableName = new SuchByte.MacroDeck.GUI.CustomControls.RoundedTextBox();
            this.lblValue = new System.Windows.Forms.Label();
            this.variableValue = new SuchByte.MacroDeck.GUI.CustomControls.RoundedTextBox();
            this.btnDelete = new System.Windows.Forms.LinkLabel();
            this.SuspendLayout();
            // 
            // variableType
            // 
            this.variableType.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.variableType.Cursor = System.Windows.Forms.Cursors.Hand;
            this.variableType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.variableType.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.variableType.Icon = null;
            this.variableType.Location = new System.Drawing.Point(72, 10);
            this.variableType.Margin = new System.Windows.Forms.Padding(4);
            this.variableType.Name = "variableType";
            this.variableType.Padding = new System.Windows.Forms.Padding(8, 2, 8, 2);
            this.variableType.SelectedIndex = -1;
            this.variableType.SelectedItem = null;
            this.variableType.Size = new System.Drawing.Size(177, 31);
            this.variableType.TabIndex = 3;
            this.variableType.SelectedIndexChanged += new System.EventHandler(this.variableType_SelectedIndexChanged);
            // 
            // lblType
            // 
            this.lblType.AutoSize = true;
            this.lblType.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblType.Location = new System.Drawing.Point(10, 14);
            this.lblType.Name = "lblType";
            this.lblType.Size = new System.Drawing.Size(50, 19);
            this.lblType.TabIndex = 4;
            this.lblType.Text = "Type:";
            this.lblType.UseMnemonic = false;
            // 
            // btnOk
            // 
            this.btnOk.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOk.BorderRadius = 8;
            this.btnOk.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnOk.FlatAppearance.BorderSize = 0;
            this.btnOk.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOk.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnOk.ForeColor = System.Drawing.Color.White;
            this.btnOk.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnOk.Icon = null;
            this.btnOk.Location = new System.Drawing.Point(278, 111);
            this.btnOk.Name = "btnOk";
            this.btnOk.Progress = 0;
            this.btnOk.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnOk.Size = new System.Drawing.Size(75, 25);
            this.btnOk.TabIndex = 5;
            this.btnOk.Text = "Ok";
            this.btnOk.UseMnemonic = false;
            this.btnOk.UseVisualStyleBackColor = false;
            this.btnOk.UseWindowsAccentColor = true;
            this.btnOk.Click += new System.EventHandler(this.BtnOk_Click);
            // 
            // lblName
            // 
            this.lblName.AutoSize = true;
            this.lblName.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblName.Location = new System.Drawing.Point(10, 46);
            this.lblName.Name = "lblName";
            this.lblName.Size = new System.Drawing.Size(56, 19);
            this.lblName.TabIndex = 6;
            this.lblName.Text = "Name:";
            this.lblName.UseMnemonic = false;
            // 
            // variableName
            // 
            this.variableName.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.variableName.Cursor = System.Windows.Forms.Cursors.Hand;
            this.variableName.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.variableName.ForeColor = System.Drawing.Color.White;
            this.variableName.Icon = null;
            this.variableName.Location = new System.Drawing.Point(72, 44);
            this.variableName.MaxCharacters = 32767;
            this.variableName.Multiline = false;
            this.variableName.Name = "variableName";
            this.variableName.Padding = new System.Windows.Forms.Padding(8, 5, 8, 5);
            this.variableName.PasswordChar = false;
            this.variableName.PlaceHolderColor = System.Drawing.Color.Gray;
            this.variableName.PlaceHolderText = "";
            this.variableName.ReadOnly = false;
            this.variableName.ScrollBars = System.Windows.Forms.ScrollBars.None;
            this.variableName.SelectionStart = 0;
            this.variableName.Size = new System.Drawing.Size(279, 30);
            this.variableName.TabIndex = 7;
            this.variableName.TextAlignment = System.Windows.Forms.HorizontalAlignment.Left;
            // 
            // lblValue
            // 
            this.lblValue.AutoSize = true;
            this.lblValue.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblValue.Location = new System.Drawing.Point(10, 79);
            this.lblValue.Name = "lblValue";
            this.lblValue.Size = new System.Drawing.Size(54, 19);
            this.lblValue.TabIndex = 8;
            this.lblValue.Text = "Value:";
            this.lblValue.UseMnemonic = false;
            // 
            // variableValue
            // 
            this.variableValue.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.variableValue.Cursor = System.Windows.Forms.Cursors.Hand;
            this.variableValue.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.variableValue.ForeColor = System.Drawing.Color.White;
            this.variableValue.Icon = null;
            this.variableValue.Location = new System.Drawing.Point(72, 77);
            this.variableValue.MaxCharacters = 32767;
            this.variableValue.Multiline = false;
            this.variableValue.Name = "variableValue";
            this.variableValue.Padding = new System.Windows.Forms.Padding(8, 5, 8, 5);
            this.variableValue.PasswordChar = false;
            this.variableValue.PlaceHolderColor = System.Drawing.Color.Gray;
            this.variableValue.PlaceHolderText = "";
            this.variableValue.ReadOnly = false;
            this.variableValue.ScrollBars = System.Windows.Forms.ScrollBars.None;
            this.variableValue.SelectionStart = 0;
            this.variableValue.Size = new System.Drawing.Size(279, 30);
            this.variableValue.TabIndex = 9;
            this.variableValue.TextAlignment = System.Windows.Forms.HorizontalAlignment.Left;
            // 
            // btnDelete
            // 
            this.btnDelete.AutoSize = true;
            this.btnDelete.LinkColor = System.Drawing.Color.Silver;
            this.btnDelete.Location = new System.Drawing.Point(12, 116);
            this.btnDelete.Name = "btnDelete";
            this.btnDelete.Size = new System.Drawing.Size(92, 16);
            this.btnDelete.TabIndex = 10;
            this.btnDelete.TabStop = true;
            this.btnDelete.Text = "Delete variable";
            this.btnDelete.UseMnemonic = false;
            this.btnDelete.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.BtnDelete_LinkClicked);
            // 
            // VariableDialog
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.ClientSize = new System.Drawing.Size(363, 149);
            this.Controls.Add(this.btnDelete);
            this.Controls.Add(this.variableValue);
            this.Controls.Add(this.lblValue);
            this.Controls.Add(this.variableName);
            this.Controls.Add(this.lblName);
            this.Controls.Add(this.btnOk);
            this.Controls.Add(this.lblType);
            this.Controls.Add(this.variableType);
            this.Location = new System.Drawing.Point(0, 0);
            this.MinimumSize = new System.Drawing.Size(363, 136);
            this.Name = "VariableDialog";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "VariableDialog";
            this.Load += new System.EventHandler(this.VariableDialog_Load);
            this.Controls.SetChildIndex(this.variableType, 0);
            this.Controls.SetChildIndex(this.lblType, 0);
            this.Controls.SetChildIndex(this.btnOk, 0);
            this.Controls.SetChildIndex(this.lblName, 0);
            this.Controls.SetChildIndex(this.variableName, 0);
            this.Controls.SetChildIndex(this.lblValue, 0);
            this.Controls.SetChildIndex(this.variableValue, 0);
            this.Controls.SetChildIndex(this.btnDelete, 0);
            this.ResumeLayout(false);
            this.PerformLayout();

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