
namespace SuchByte.MacroDeck.GUI.Dialogs
{
    partial class DebugConsole
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
            this.logOutput = new System.Windows.Forms.RichTextBox();
            this.btnClear = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.btnRestartMacroDeck = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.btnExit = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.btnOpenUser = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.label1 = new System.Windows.Forms.Label();
            this.logLevel = new SuchByte.MacroDeck.GUI.CustomControls.RoundedComboBox();
            this.btnExportOutput = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.label3 = new System.Windows.Forms.Label();
            this.filter = new SuchByte.MacroDeck.GUI.CustomControls.RoundedTextBox();
            this.btnAddFilter = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.filtersList = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.btnRemoveFilters = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.SuspendLayout();
            // 
            // logOutput
            // 
            this.logOutput.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.logOutput.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.logOutput.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.logOutput.ForeColor = System.Drawing.Color.White;
            this.logOutput.Location = new System.Drawing.Point(2, 69);
            this.logOutput.Name = "logOutput";
            this.logOutput.ReadOnly = true;
            this.logOutput.Size = new System.Drawing.Size(796, 343);
            this.logOutput.TabIndex = 1;
            this.logOutput.Text = "";
            // 
            // btnClear
            // 
            this.btnClear.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnClear.BorderRadius = 8;
            this.btnClear.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnClear.FlatAppearance.BorderSize = 0;
            this.btnClear.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnClear.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnClear.ForeColor = System.Drawing.Color.White;
            this.btnClear.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnClear.Icon = null;
            this.btnClear.Location = new System.Drawing.Point(673, 416);
            this.btnClear.Name = "btnClear";
            this.btnClear.Progress = 0;
            this.btnClear.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnClear.Size = new System.Drawing.Size(122, 27);
            this.btnClear.TabIndex = 2;
            this.btnClear.Text = "Clear";
            this.btnClear.UseVisualStyleBackColor = false;
            this.btnClear.UseWindowsAccentColor = true;
            this.btnClear.Click += new System.EventHandler(this.BtnClear_Click);
            // 
            // btnRestartMacroDeck
            // 
            this.btnRestartMacroDeck.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnRestartMacroDeck.BorderRadius = 8;
            this.btnRestartMacroDeck.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnRestartMacroDeck.FlatAppearance.BorderSize = 0;
            this.btnRestartMacroDeck.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRestartMacroDeck.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnRestartMacroDeck.ForeColor = System.Drawing.Color.White;
            this.btnRestartMacroDeck.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnRestartMacroDeck.Icon = null;
            this.btnRestartMacroDeck.Location = new System.Drawing.Point(133, 416);
            this.btnRestartMacroDeck.Name = "btnRestartMacroDeck";
            this.btnRestartMacroDeck.Progress = 0;
            this.btnRestartMacroDeck.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnRestartMacroDeck.Size = new System.Drawing.Size(122, 27);
            this.btnRestartMacroDeck.TabIndex = 3;
            this.btnRestartMacroDeck.Text = "Restart Macro Deck";
            this.btnRestartMacroDeck.UseVisualStyleBackColor = false;
            this.btnRestartMacroDeck.UseWindowsAccentColor = true;
            this.btnRestartMacroDeck.Click += new System.EventHandler(this.BtnRestartMacroDeck_Click);
            // 
            // btnExit
            // 
            this.btnExit.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnExit.BorderRadius = 8;
            this.btnExit.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnExit.FlatAppearance.BorderSize = 0;
            this.btnExit.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnExit.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnExit.ForeColor = System.Drawing.Color.White;
            this.btnExit.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnExit.Icon = null;
            this.btnExit.Location = new System.Drawing.Point(5, 416);
            this.btnExit.Name = "btnExit";
            this.btnExit.Progress = 0;
            this.btnExit.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnExit.Size = new System.Drawing.Size(122, 27);
            this.btnExit.TabIndex = 4;
            this.btnExit.Text = "Exit Macro Deck";
            this.btnExit.UseVisualStyleBackColor = false;
            this.btnExit.UseWindowsAccentColor = true;
            this.btnExit.Click += new System.EventHandler(this.BtnExit_Click);
            // 
            // btnOpenUser
            // 
            this.btnOpenUser.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnOpenUser.BorderRadius = 8;
            this.btnOpenUser.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnOpenUser.FlatAppearance.BorderSize = 0;
            this.btnOpenUser.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOpenUser.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnOpenUser.ForeColor = System.Drawing.Color.White;
            this.btnOpenUser.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnOpenUser.Icon = null;
            this.btnOpenUser.Location = new System.Drawing.Point(261, 416);
            this.btnOpenUser.Name = "btnOpenUser";
            this.btnOpenUser.Progress = 0;
            this.btnOpenUser.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnOpenUser.Size = new System.Drawing.Size(122, 27);
            this.btnOpenUser.TabIndex = 5;
            this.btnOpenUser.Text = "Open user directory";
            this.btnOpenUser.UseVisualStyleBackColor = false;
            this.btnOpenUser.UseWindowsAccentColor = true;
            this.btnOpenUser.Click += new System.EventHandler(this.BtnOpenUser_Click);
            // 
            // label1
            // 
            this.label1.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.label1.Location = new System.Drawing.Point(7, 37);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(95, 26);
            this.label1.TabIndex = 6;
            this.label1.Text = "Log level:";
            this.label1.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // logLevel
            // 
            this.logLevel.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.logLevel.Cursor = System.Windows.Forms.Cursors.Hand;
            this.logLevel.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.logLevel.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.logLevel.Icon = null;
            this.logLevel.Location = new System.Drawing.Point(108, 37);
            this.logLevel.Name = "logLevel";
            this.logLevel.Padding = new System.Windows.Forms.Padding(8, 2, 8, 2);
            this.logLevel.SelectedIndex = -1;
            this.logLevel.SelectedItem = null;
            this.logLevel.Size = new System.Drawing.Size(169, 26);
            this.logLevel.TabIndex = 7;
            this.logLevel.SelectedIndexChanged += new System.EventHandler(this.LogLevel_SelectedIndexChanged);
            // 
            // btnExportOutput
            // 
            this.btnExportOutput.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnExportOutput.BorderRadius = 8;
            this.btnExportOutput.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnExportOutput.FlatAppearance.BorderSize = 0;
            this.btnExportOutput.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnExportOutput.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnExportOutput.ForeColor = System.Drawing.Color.White;
            this.btnExportOutput.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnExportOutput.Icon = null;
            this.btnExportOutput.Location = new System.Drawing.Point(545, 416);
            this.btnExportOutput.Name = "btnExportOutput";
            this.btnExportOutput.Progress = 0;
            this.btnExportOutput.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnExportOutput.Size = new System.Drawing.Size(122, 27);
            this.btnExportOutput.TabIndex = 8;
            this.btnExportOutput.Text = "Export output";
            this.btnExportOutput.UseVisualStyleBackColor = false;
            this.btnExportOutput.UseWindowsAccentColor = true;
            this.btnExportOutput.Click += new System.EventHandler(this.BtnExportOutput_Click);
            // 
            // label3
            // 
            this.label3.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.label3.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.label3.Location = new System.Drawing.Point(368, 37);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(52, 26);
            this.label3.TabIndex = 9;
            this.label3.Text = "Filter:";
            this.label3.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // filter
            // 
            this.filter.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.filter.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.filter.Cursor = System.Windows.Forms.Cursors.Hand;
            this.filter.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.filter.Icon = null;
            this.filter.Location = new System.Drawing.Point(424, 38);
            this.filter.MaxCharacters = 32767;
            this.filter.Multiline = false;
            this.filter.Name = "filter";
            this.filter.Padding = new System.Windows.Forms.Padding(8, 5, 8, 5);
            this.filter.PasswordChar = false;
            this.filter.PlaceHolderColor = System.Drawing.Color.Gray;
            this.filter.PlaceHolderText = "";
            this.filter.ReadOnly = false;
            this.filter.ScrollBars = System.Windows.Forms.ScrollBars.None;
            this.filter.SelectionStart = 0;
            this.filter.Size = new System.Drawing.Size(315, 25);
            this.filter.TabIndex = 10;
            this.filter.TextAlignment = System.Windows.Forms.HorizontalAlignment.Left;
            // 
            // btnAddFilter
            // 
            this.btnAddFilter.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnAddFilter.BorderRadius = 8;
            this.btnAddFilter.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnAddFilter.FlatAppearance.BorderSize = 0;
            this.btnAddFilter.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAddFilter.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnAddFilter.ForeColor = System.Drawing.Color.White;
            this.btnAddFilter.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnAddFilter.Icon = global::SuchByte.MacroDeck.Properties.Resources.Create_Hover;
            this.btnAddFilter.Location = new System.Drawing.Point(741, 38);
            this.btnAddFilter.Name = "btnAddFilter";
            this.btnAddFilter.Progress = 0;
            this.btnAddFilter.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnAddFilter.Size = new System.Drawing.Size(25, 25);
            this.btnAddFilter.TabIndex = 11;
            this.btnAddFilter.UseVisualStyleBackColor = false;
            this.btnAddFilter.UseWindowsAccentColor = true;
            this.btnAddFilter.Click += new System.EventHandler(this.BtnAddFilter_Click);
            // 
            // filtersList
            // 
            this.filtersList.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.filtersList.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.filtersList.Name = "filtersList";
            this.filtersList.ShowImageMargin = false;
            this.filtersList.Size = new System.Drawing.Size(36, 4);
            // 
            // btnRemoveFilters
            // 
            this.btnRemoveFilters.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnRemoveFilters.BorderRadius = 8;
            this.btnRemoveFilters.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnRemoveFilters.FlatAppearance.BorderSize = 0;
            this.btnRemoveFilters.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRemoveFilters.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnRemoveFilters.ForeColor = System.Drawing.Color.White;
            this.btnRemoveFilters.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(152)))), ((int)(((byte)(0)))), ((int)(((byte)(0)))));
            this.btnRemoveFilters.Icon = global::SuchByte.MacroDeck.Properties.Resources.Delete_Hover;
            this.btnRemoveFilters.Location = new System.Drawing.Point(770, 38);
            this.btnRemoveFilters.Name = "btnRemoveFilters";
            this.btnRemoveFilters.Progress = 0;
            this.btnRemoveFilters.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnRemoveFilters.Size = new System.Drawing.Size(25, 25);
            this.btnRemoveFilters.TabIndex = 12;
            this.btnRemoveFilters.UseVisualStyleBackColor = false;
            this.btnRemoveFilters.UseWindowsAccentColor = false;
            this.btnRemoveFilters.Click += new System.EventHandler(this.btnRemoveFilters_Click);
            // 
            // DebugConsole
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this.btnRemoveFilters);
            this.Controls.Add(this.btnAddFilter);
            this.Controls.Add(this.filter);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.btnExportOutput);
            this.Controls.Add(this.logLevel);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.btnOpenUser);
            this.Controls.Add(this.btnExit);
            this.Controls.Add(this.btnRestartMacroDeck);
            this.Controls.Add(this.btnClear);
            this.Controls.Add(this.logOutput);
            this.Location = new System.Drawing.Point(0, 0);
            this.MinimumSize = new System.Drawing.Size(800, 450);
            this.Name = "DebugConsole";
            this.Padding = new System.Windows.Forms.Padding(2);
            this.StartPosition = System.Windows.Forms.FormStartPosition.WindowsDefaultLocation;
            this.Load += new System.EventHandler(this.DebugConsole_Load);
            this.Controls.SetChildIndex(this.logOutput, 0);
            this.Controls.SetChildIndex(this.btnClear, 0);
            this.Controls.SetChildIndex(this.btnRestartMacroDeck, 0);
            this.Controls.SetChildIndex(this.btnExit, 0);
            this.Controls.SetChildIndex(this.btnOpenUser, 0);
            this.Controls.SetChildIndex(this.label1, 0);
            this.Controls.SetChildIndex(this.logLevel, 0);
            this.Controls.SetChildIndex(this.btnExportOutput, 0);
            this.Controls.SetChildIndex(this.label3, 0);
            this.Controls.SetChildIndex(this.filter, 0);
            this.Controls.SetChildIndex(this.btnAddFilter, 0);
            this.Controls.SetChildIndex(this.btnRemoveFilters, 0);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.RichTextBox logOutput;
        private CustomControls.ButtonPrimary btnClear;
        private CustomControls.ButtonPrimary btnRestartMacroDeck;
        private CustomControls.ButtonPrimary btnExit;
        private CustomControls.ButtonPrimary btnOpenUser;
        private System.Windows.Forms.Label label1;
        private CustomControls.RoundedComboBox logLevel;
        private CustomControls.ButtonPrimary btnExportOutput;
        private System.Windows.Forms.Label label3;
        private CustomControls.RoundedTextBox filter;
        private CustomControls.ButtonPrimary btnAddFilter;
        private System.Windows.Forms.ContextMenuStrip filtersList;
        private CustomControls.ButtonPrimary btnRemoveFilters;
    }
}