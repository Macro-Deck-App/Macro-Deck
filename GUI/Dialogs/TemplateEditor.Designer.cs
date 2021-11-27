
namespace SuchByte.MacroDeck.GUI.Dialogs
{
    partial class TemplateEditor
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
            this.components = new System.ComponentModel.Container();
            this.template = new SuchByte.MacroDeck.GUI.CustomControls.RoundedTextBox();
            this.btnOk = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.lblResultLabel = new System.Windows.Forms.Label();
            this.lblResult = new System.Windows.Forms.Label();
            this.btnVariables = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.btnIf = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.btnAnd = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.btnOr = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.btnNot = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.lblTemplateEngineInfo = new System.Windows.Forms.LinkLabel();
            this.variablesContextMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.SuspendLayout();
            // 
            // template
            // 
            this.template.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.template.Cursor = System.Windows.Forms.Cursors.Hand;
            this.template.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.template.Icon = null;
            this.template.Location = new System.Drawing.Point(9, 85);
            this.template.MaxCharacters = 32767;
            this.template.Multiline = true;
            this.template.Name = "template";
            this.template.Padding = new System.Windows.Forms.Padding(8, 5, 8, 5);
            this.template.PasswordChar = false;
            this.template.PlaceHolderColor = System.Drawing.Color.Gray;
            this.template.PlaceHolderText = "";
            this.template.ReadOnly = false;
            this.template.ScrollBars = System.Windows.Forms.ScrollBars.None;
            this.template.SelectionStart = 0;
            this.template.Size = new System.Drawing.Size(501, 265);
            this.template.TabIndex = 2;
            this.template.TabStop = false;
            this.template.TextAlignment = System.Windows.Forms.HorizontalAlignment.Left;
            // 
            // btnOk
            // 
            this.btnOk.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnOk.BorderRadius = 8;
            this.btnOk.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnOk.FlatAppearance.BorderSize = 0;
            this.btnOk.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOk.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnOk.ForeColor = System.Drawing.Color.White;
            this.btnOk.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnOk.Icon = null;
            this.btnOk.Location = new System.Drawing.Point(739, 357);
            this.btnOk.Name = "btnOk";
            this.btnOk.Progress = 0;
            this.btnOk.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnOk.Size = new System.Drawing.Size(75, 25);
            this.btnOk.TabIndex = 3;
            this.btnOk.TabStop = false;
            this.btnOk.Text = "Ok";
            this.btnOk.UseVisualStyleBackColor = false;
            this.btnOk.Click += new System.EventHandler(this.BtnOk_Click);
            // 
            // lblResultLabel
            // 
            this.lblResultLabel.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.lblResultLabel.Location = new System.Drawing.Point(516, 85);
            this.lblResultLabel.Name = "lblResultLabel";
            this.lblResultLabel.Size = new System.Drawing.Size(289, 22);
            this.lblResultLabel.TabIndex = 4;
            this.lblResultLabel.Text = "Result";
            this.lblResultLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblResult
            // 
            this.lblResult.Location = new System.Drawing.Point(516, 118);
            this.lblResult.Name = "lblResult";
            this.lblResult.Size = new System.Drawing.Size(306, 232);
            this.lblResult.TabIndex = 5;
            // 
            // btnVariables
            // 
            this.btnVariables.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnVariables.BorderRadius = 8;
            this.btnVariables.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnVariables.FlatAppearance.BorderSize = 0;
            this.btnVariables.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnVariables.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnVariables.ForeColor = System.Drawing.Color.White;
            this.btnVariables.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnVariables.Icon = null;
            this.btnVariables.Location = new System.Drawing.Point(9, 49);
            this.btnVariables.Name = "btnVariables";
            this.btnVariables.Progress = 0;
            this.btnVariables.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnVariables.Size = new System.Drawing.Size(75, 30);
            this.btnVariables.TabIndex = 6;
            this.btnVariables.Text = "Variables";
            this.btnVariables.UseVisualStyleBackColor = false;
            this.btnVariables.Click += new System.EventHandler(this.BtnVariables_Click);
            // 
            // btnIf
            // 
            this.btnIf.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnIf.BorderRadius = 8;
            this.btnIf.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnIf.FlatAppearance.BorderSize = 0;
            this.btnIf.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnIf.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnIf.ForeColor = System.Drawing.Color.White;
            this.btnIf.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnIf.Icon = null;
            this.btnIf.Location = new System.Drawing.Point(90, 49);
            this.btnIf.Name = "btnIf";
            this.btnIf.Progress = 0;
            this.btnIf.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnIf.Size = new System.Drawing.Size(75, 30);
            this.btnIf.TabIndex = 7;
            this.btnIf.Text = "If";
            this.btnIf.UseVisualStyleBackColor = false;
            this.btnIf.Click += new System.EventHandler(this.BtnIf_Click);
            // 
            // btnAnd
            // 
            this.btnAnd.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnAnd.BorderRadius = 8;
            this.btnAnd.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnAnd.FlatAppearance.BorderSize = 0;
            this.btnAnd.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAnd.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnAnd.ForeColor = System.Drawing.Color.White;
            this.btnAnd.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnAnd.Icon = null;
            this.btnAnd.Location = new System.Drawing.Point(171, 49);
            this.btnAnd.Name = "btnAnd";
            this.btnAnd.Progress = 0;
            this.btnAnd.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnAnd.Size = new System.Drawing.Size(75, 30);
            this.btnAnd.TabIndex = 8;
            this.btnAnd.Text = "And";
            this.btnAnd.UseVisualStyleBackColor = false;
            this.btnAnd.Click += new System.EventHandler(this.BtnAnd_Click);
            // 
            // btnOr
            // 
            this.btnOr.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnOr.BorderRadius = 8;
            this.btnOr.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnOr.FlatAppearance.BorderSize = 0;
            this.btnOr.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOr.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnOr.ForeColor = System.Drawing.Color.White;
            this.btnOr.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnOr.Icon = null;
            this.btnOr.Location = new System.Drawing.Point(252, 49);
            this.btnOr.Name = "btnOr";
            this.btnOr.Progress = 0;
            this.btnOr.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnOr.Size = new System.Drawing.Size(75, 30);
            this.btnOr.TabIndex = 9;
            this.btnOr.Text = "Or";
            this.btnOr.UseVisualStyleBackColor = false;
            this.btnOr.Click += new System.EventHandler(this.BtnOr_Click);
            // 
            // btnNot
            // 
            this.btnNot.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnNot.BorderRadius = 8;
            this.btnNot.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnNot.FlatAppearance.BorderSize = 0;
            this.btnNot.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnNot.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnNot.ForeColor = System.Drawing.Color.White;
            this.btnNot.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnNot.Icon = null;
            this.btnNot.Location = new System.Drawing.Point(333, 49);
            this.btnNot.Name = "btnNot";
            this.btnNot.Progress = 0;
            this.btnNot.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnNot.Size = new System.Drawing.Size(75, 30);
            this.btnNot.TabIndex = 10;
            this.btnNot.Text = "Not";
            this.btnNot.UseVisualStyleBackColor = false;
            this.btnNot.Click += new System.EventHandler(this.BtnNot_Click);
            // 
            // lblTemplateEngineInfo
            // 
            this.lblTemplateEngineInfo.ActiveLinkColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.lblTemplateEngineInfo.LinkColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.lblTemplateEngineInfo.Location = new System.Drawing.Point(9, 17);
            this.lblTemplateEngineInfo.Name = "lblTemplateEngineInfo";
            this.lblTemplateEngineInfo.Size = new System.Drawing.Size(743, 29);
            this.lblTemplateEngineInfo.TabIndex = 11;
            this.lblTemplateEngineInfo.TabStop = true;
            this.lblTemplateEngineInfo.Text = "Macro Deck uses the Cottle template engine. Click here for more information.";
            this.lblTemplateEngineInfo.VisitedLinkColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.lblTemplateEngineInfo.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.LblTemplateEngineInfo_LinkClicked);
            // 
            // variablesContextMenu
            // 
            this.variablesContextMenu.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.variablesContextMenu.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.variablesContextMenu.Name = "variablesContextMenu";
            this.variablesContextMenu.ShowImageMargin = false;
            this.variablesContextMenu.Size = new System.Drawing.Size(36, 4);
            // 
            // TemplateEditor
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(831, 395);
            this.Controls.Add(this.lblTemplateEngineInfo);
            this.Controls.Add(this.btnNot);
            this.Controls.Add(this.btnOr);
            this.Controls.Add(this.btnAnd);
            this.Controls.Add(this.btnIf);
            this.Controls.Add(this.btnVariables);
            this.Controls.Add(this.lblResult);
            this.Controls.Add(this.lblResultLabel);
            this.Controls.Add(this.btnOk);
            this.Controls.Add(this.template);
            this.Name = "TemplateEditor";
            this.Text = "TemplateEditor";
            this.Controls.SetChildIndex(this.template, 0);
            this.Controls.SetChildIndex(this.btnOk, 0);
            this.Controls.SetChildIndex(this.lblResultLabel, 0);
            this.Controls.SetChildIndex(this.lblResult, 0);
            this.Controls.SetChildIndex(this.btnVariables, 0);
            this.Controls.SetChildIndex(this.btnIf, 0);
            this.Controls.SetChildIndex(this.btnAnd, 0);
            this.Controls.SetChildIndex(this.btnOr, 0);
            this.Controls.SetChildIndex(this.btnNot, 0);
            this.Controls.SetChildIndex(this.lblTemplateEngineInfo, 0);
            this.ResumeLayout(false);

        }

        #endregion

        private CustomControls.RoundedTextBox template;
        private CustomControls.ButtonPrimary btnOk;
        private System.Windows.Forms.Label lblResultLabel;
        private System.Windows.Forms.Label lblResult;
        private CustomControls.ButtonPrimary btnVariables;
        private CustomControls.ButtonPrimary btnIf;
        private CustomControls.ButtonPrimary btnAnd;
        private CustomControls.ButtonPrimary btnOr;
        private CustomControls.ButtonPrimary btnNot;
        private System.Windows.Forms.LinkLabel lblTemplateEngineInfo;
        private System.Windows.Forms.ContextMenuStrip variablesContextMenu;
    }
}