
using FastColoredTextBoxNS;

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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(TemplateEditor));
            this.template = new FastColoredTextBoxNS.FastColoredTextBox();
            this.btnOk = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.lblResultLabel = new System.Windows.Forms.Label();
            this.lblResult = new System.Windows.Forms.Label();
            this.btnVariables = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.lblTemplateEngineInfo = new System.Windows.Forms.LinkLabel();
            this.variablesContextMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.btnIf = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.btnAnd = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.btnOr = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.btnNot = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.checkTrimBlankLines = new System.Windows.Forms.CheckBox();
            ((System.ComponentModel.ISupportInitialize)(this.template)).BeginInit();
            this.SuspendLayout();
            // 
            // template
            // 
            this.template.AutoCompleteBracketsList = new char[] {
        '(',
        ')',
        '{',
        '}',
        '[',
        ']',
        '\"',
        '\"',
        '\'',
        '\''};
            this.template.AutoScrollMinSize = new System.Drawing.Size(25, 13);
            this.template.BackBrush = null;
            this.template.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.template.CaretColor = System.Drawing.Color.White;
            this.template.CharHeight = 13;
            this.template.CharWidth = 7;
            this.template.CommentPrefix = "{_";
            this.template.Cursor = System.Windows.Forms.Cursors.IBeam;
            this.template.DisabledColor = System.Drawing.Color.FromArgb(((int)(((byte)(100)))), ((int)(((byte)(180)))), ((int)(((byte)(180)))), ((int)(((byte)(180)))));
            this.template.Font = new System.Drawing.Font("Courier New", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.template.IsReplaceMode = false;
            this.template.Location = new System.Drawing.Point(12, 85);
            this.template.Name = "template";
            this.template.Paddings = new System.Windows.Forms.Padding(0);
            this.template.SelectionColor = System.Drawing.Color.FromArgb(((int)(((byte)(60)))), ((int)(((byte)(0)))), ((int)(((byte)(0)))), ((int)(((byte)(255)))));
            this.template.ServiceColors = ((FastColoredTextBoxNS.ServiceColors)(resources.GetObject("template.ServiceColors")));
            this.template.Size = new System.Drawing.Size(743, 265);
            this.template.TabIndex = 2;
            this.template.TabStop = false;
            this.template.Zoom = 100;
            // 
            // btnOk
            // 
            this.btnOk.BorderRadius = 8;
            this.btnOk.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnOk.FlatAppearance.BorderSize = 0;
            this.btnOk.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOk.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnOk.ForeColor = System.Drawing.Color.White;
            this.btnOk.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnOk.Icon = null;
            this.btnOk.Location = new System.Drawing.Point(680, 550);
            this.btnOk.Name = "btnOk";
            this.btnOk.Progress = 0;
            this.btnOk.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnOk.Size = new System.Drawing.Size(75, 25);
            this.btnOk.TabIndex = 3;
            this.btnOk.TabStop = false;
            this.btnOk.Text = "Ok";
            this.btnOk.UseMnemonic = false;
            this.btnOk.UseVisualStyleBackColor = false;
            this.btnOk.UseWindowsAccentColor = true;
            this.btnOk.Click += new System.EventHandler(this.BtnOk_Click);
            // 
            // lblResultLabel
            // 
            this.lblResultLabel.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.lblResultLabel.Location = new System.Drawing.Point(12, 386);
            this.lblResultLabel.Name = "lblResultLabel";
            this.lblResultLabel.Size = new System.Drawing.Size(289, 22);
            this.lblResultLabel.TabIndex = 4;
            this.lblResultLabel.Text = "Result";
            this.lblResultLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblResult
            // 
            this.lblResult.Location = new System.Drawing.Point(12, 408);
            this.lblResult.Name = "lblResult";
            this.lblResult.Size = new System.Drawing.Size(501, 147);
            this.lblResult.TabIndex = 5;
            this.lblResult.UseMnemonic = false;
            // 
            // btnVariables
            // 
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
            this.btnVariables.Size = new System.Drawing.Size(116, 30);
            this.btnVariables.TabIndex = 6;
            this.btnVariables.Text = "Variables";
            this.btnVariables.UseMnemonic = false;
            this.btnVariables.UseVisualStyleBackColor = false;
            this.btnVariables.UseWindowsAccentColor = true;
            this.btnVariables.Click += new System.EventHandler(this.BtnVariables_Click);
            // 
            // lblTemplateEngineInfo
            // 
            this.lblTemplateEngineInfo.ActiveLinkColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.lblTemplateEngineInfo.LinkColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.lblTemplateEngineInfo.Location = new System.Drawing.Point(9, 17);
            this.lblTemplateEngineInfo.Name = "lblTemplateEngineInfo";
            this.lblTemplateEngineInfo.Size = new System.Drawing.Size(695, 29);
            this.lblTemplateEngineInfo.TabIndex = 11;
            this.lblTemplateEngineInfo.TabStop = true;
            this.lblTemplateEngineInfo.Text = "Macro Deck uses the Cottle template engine. Click here for more information.";
            this.lblTemplateEngineInfo.UseMnemonic = false;
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
            // btnIf
            // 
            this.btnIf.BorderRadius = 8;
            this.btnIf.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnIf.FlatAppearance.BorderSize = 0;
            this.btnIf.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnIf.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnIf.ForeColor = System.Drawing.Color.White;
            this.btnIf.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnIf.Icon = null;
            this.btnIf.Location = new System.Drawing.Point(131, 49);
            this.btnIf.Name = "btnIf";
            this.btnIf.Progress = 0;
            this.btnIf.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnIf.Size = new System.Drawing.Size(75, 30);
            this.btnIf.TabIndex = 7;
            this.btnIf.Text = "If";
            this.btnIf.UseMnemonic = false;
            this.btnIf.UseVisualStyleBackColor = false;
            this.btnIf.UseWindowsAccentColor = true;
            this.btnIf.Click += new System.EventHandler(this.BtnIf_Click);
            // 
            // btnAnd
            // 
            this.btnAnd.BorderRadius = 8;
            this.btnAnd.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnAnd.FlatAppearance.BorderSize = 0;
            this.btnAnd.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAnd.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnAnd.ForeColor = System.Drawing.Color.White;
            this.btnAnd.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnAnd.Icon = null;
            this.btnAnd.Location = new System.Drawing.Point(212, 49);
            this.btnAnd.Name = "btnAnd";
            this.btnAnd.Progress = 0;
            this.btnAnd.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnAnd.Size = new System.Drawing.Size(75, 30);
            this.btnAnd.TabIndex = 8;
            this.btnAnd.Text = "And";
            this.btnAnd.UseMnemonic = false;
            this.btnAnd.UseVisualStyleBackColor = false;
            this.btnAnd.UseWindowsAccentColor = true;
            this.btnAnd.Click += new System.EventHandler(this.BtnAnd_Click);
            // 
            // btnOr
            // 
            this.btnOr.BorderRadius = 8;
            this.btnOr.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnOr.FlatAppearance.BorderSize = 0;
            this.btnOr.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOr.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnOr.ForeColor = System.Drawing.Color.White;
            this.btnOr.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnOr.Icon = null;
            this.btnOr.Location = new System.Drawing.Point(293, 49);
            this.btnOr.Name = "btnOr";
            this.btnOr.Progress = 0;
            this.btnOr.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnOr.Size = new System.Drawing.Size(75, 30);
            this.btnOr.TabIndex = 9;
            this.btnOr.Text = "Or";
            this.btnOr.UseMnemonic = false;
            this.btnOr.UseVisualStyleBackColor = false;
            this.btnOr.UseWindowsAccentColor = true;
            this.btnOr.Click += new System.EventHandler(this.BtnOr_Click);
            // 
            // btnNot
            // 
            this.btnNot.BorderRadius = 8;
            this.btnNot.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnNot.FlatAppearance.BorderSize = 0;
            this.btnNot.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnNot.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnNot.ForeColor = System.Drawing.Color.White;
            this.btnNot.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnNot.Icon = null;
            this.btnNot.Location = new System.Drawing.Point(374, 49);
            this.btnNot.Name = "btnNot";
            this.btnNot.Progress = 0;
            this.btnNot.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnNot.Size = new System.Drawing.Size(75, 30);
            this.btnNot.TabIndex = 10;
            this.btnNot.Text = "Not";
            this.btnNot.UseMnemonic = false;
            this.btnNot.UseVisualStyleBackColor = false;
            this.btnNot.UseWindowsAccentColor = true;
            this.btnNot.Click += new System.EventHandler(this.BtnNot_Click);
            // 
            // checkTrimBlankLines
            // 
            this.checkTrimBlankLines.Location = new System.Drawing.Point(18, 356);
            this.checkTrimBlankLines.Name = "checkTrimBlankLines";
            this.checkTrimBlankLines.Size = new System.Drawing.Size(338, 27);
            this.checkTrimBlankLines.TabIndex = 12;
            this.checkTrimBlankLines.Text = "Trim blank lines";
            this.checkTrimBlankLines.UseVisualStyleBackColor = true;
            this.checkTrimBlankLines.CheckedChanged += new System.EventHandler(this.CheckTrimBlankLines_CheckedChanged);
            // 
            // TemplateEditor
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.ClientSize = new System.Drawing.Size(766, 585);
            this.Controls.Add(this.checkTrimBlankLines);
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
            this.Location = new System.Drawing.Point(0, 0);
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
            this.Controls.SetChildIndex(this.checkTrimBlankLines, 0);
            ((System.ComponentModel.ISupportInitialize)(this.template)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private FastColoredTextBox template;
        private CustomControls.ButtonPrimary btnOk;
        private System.Windows.Forms.Label lblResultLabel;
        private System.Windows.Forms.Label lblResult;
        private CustomControls.ButtonPrimary btnVariables;
        private System.Windows.Forms.LinkLabel lblTemplateEngineInfo;
        private System.Windows.Forms.ContextMenuStrip variablesContextMenu;
        private CustomControls.ButtonPrimary btnIf;
        private CustomControls.ButtonPrimary btnAnd;
        private CustomControls.ButtonPrimary btnOr;
        private CustomControls.ButtonPrimary btnNot;
        private System.Windows.Forms.CheckBox checkTrimBlankLines;
    }
}