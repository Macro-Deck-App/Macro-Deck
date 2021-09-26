
namespace SuchByte.MacroDeck.GUI.Dialogs
{
    partial class VariableDialog
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

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
            this.variableType = new System.Windows.Forms.ComboBox();
            this.lblType = new System.Windows.Forms.Label();
            this.btnOk = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.lblName = new System.Windows.Forms.Label();
            this.variableName = new System.Windows.Forms.TextBox();
            this.lblValue = new System.Windows.Forms.Label();
            this.variableValue = new System.Windows.Forms.TextBox();
            this.btnDelete = new System.Windows.Forms.LinkLabel();
            this.SuspendLayout();
            // 
            // variableType
            // 
            this.variableType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.variableType.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.variableType.FormattingEnabled = true;
            this.variableType.Location = new System.Drawing.Point(72, 11);
            this.variableType.Name = "variableType";
            this.variableType.Size = new System.Drawing.Size(160, 27);
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
            // 
            // btnOk
            // 
            this.btnOk.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOk.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnOk.BorderRadius = 8;
            this.btnOk.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnOk.FlatAppearance.BorderSize = 0;
            this.btnOk.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOk.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnOk.ForeColor = System.Drawing.Color.White;
            this.btnOk.Location = new System.Drawing.Point(278, 111);
            this.btnOk.Name = "btnOk";
            this.btnOk.Size = new System.Drawing.Size(75, 25);
            this.btnOk.TabIndex = 5;
            this.btnOk.Text = "Ok";
            this.btnOk.UseVisualStyleBackColor = false;
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
            // 
            // variableName
            // 
            this.variableName.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.variableName.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.variableName.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.variableName.ForeColor = System.Drawing.Color.White;
            this.variableName.Location = new System.Drawing.Point(72, 44);
            this.variableName.Name = "variableName";
            this.variableName.Size = new System.Drawing.Size(279, 27);
            this.variableName.TabIndex = 7;
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
            // 
            // variableValue
            // 
            this.variableValue.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.variableValue.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.variableValue.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.variableValue.ForeColor = System.Drawing.Color.White;
            this.variableValue.Location = new System.Drawing.Point(72, 77);
            this.variableValue.Name = "variableValue";
            this.variableValue.Size = new System.Drawing.Size(279, 27);
            this.variableValue.TabIndex = 9;
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
            this.btnDelete.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.BtnDelete_LinkClicked);
            // 
            // VariableDialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(363, 149);
            this.Controls.Add(this.btnDelete);
            this.Controls.Add(this.variableValue);
            this.Controls.Add(this.lblValue);
            this.Controls.Add(this.variableName);
            this.Controls.Add(this.lblName);
            this.Controls.Add(this.btnOk);
            this.Controls.Add(this.lblType);
            this.Controls.Add(this.variableType);
            this.MinimumSize = new System.Drawing.Size(363, 136);
            this.Name = "VariableDialog";
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

        private System.Windows.Forms.ComboBox variableType;
        private System.Windows.Forms.Label lblType;
        private CustomControls.ButtonPrimary btnOk;
        private System.Windows.Forms.Label lblName;
        private System.Windows.Forms.TextBox variableName;
        private System.Windows.Forms.Label lblValue;
        private System.Windows.Forms.TextBox variableValue;
        private System.Windows.Forms.LinkLabel btnDelete;
    }
}