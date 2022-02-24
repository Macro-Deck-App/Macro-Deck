using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Interfaces;
using System.Windows.Controls;
using ComboBox = SuchByte.MacroDeck.GUI.CustomControls.ComboBox;

namespace SuchByte.MacroDeck.GUI
{
    partial class ButtonEditor
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
            if (this.actionButton != null)
            {
                this.actionButton.StateChanged -= this.OnStateChanged;
            }
            if (this.actionButtonEdited != null)
            {
                this.actionButtonEdited.StateChanged -= this.OnStateChanged;
            }
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
            this.btnApply = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.btnPreview = new SuchByte.MacroDeck.GUI.CustomControls.RoundedButton();
            this.labelText = new SuchByte.MacroDeck.GUI.CustomControls.RoundedTextBox();
            this.fontSize = new System.Windows.Forms.NumericUpDown();
            this.lblButtonState = new System.Windows.Forms.Label();
            this.radioButtonOff = new SuchByte.MacroDeck.GUI.CustomControls.TabRadioButton();
            this.radioButtonOn = new SuchByte.MacroDeck.GUI.CustomControls.TabRadioButton();
            this.panel1 = new System.Windows.Forms.Panel();
            this.panel2 = new System.Windows.Forms.Panel();
            this.labelAlignBottom = new SuchByte.MacroDeck.GUI.CustomControls.TabRadioButton();
            this.labelAlignCenter = new SuchByte.MacroDeck.GUI.CustomControls.TabRadioButton();
            this.labelAlignTop = new SuchByte.MacroDeck.GUI.CustomControls.TabRadioButton();
            this.btnEditIcon = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.btnRemoveIcon = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.btnClearLabelText = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.groupAppearance = new System.Windows.Forms.GroupBox();
            this.btnOpenTemplateEditor = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.btnAddVariable = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.btnForeColor = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.fonts = new SuchByte.MacroDeck.GUI.CustomControls.RoundedComboBox();
            this.lblCurrentState = new System.Windows.Forms.Label();
            this.lblCurrentStateLabel = new System.Windows.Forms.Label();
            this.btnOk = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.variablesContextMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.lblStateBinding = new System.Windows.Forms.Label();
            this.listStateBinding = new SuchByte.MacroDeck.GUI.CustomControls.RoundedComboBox();
            this.btnDeleteStateBinding = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.selectorPanel = new System.Windows.Forms.Panel();
            this.flowLayoutPanel1 = new System.Windows.Forms.FlowLayoutPanel();
            this.radioOnPress = new SuchByte.MacroDeck.GUI.CustomControls.TabRadioButton();
            this.radioOnRelease = new SuchByte.MacroDeck.GUI.CustomControls.TabRadioButton();
            this.radioOnLongPress = new SuchByte.MacroDeck.GUI.CustomControls.TabRadioButton();
            this.radioOnLongPressRelease = new SuchByte.MacroDeck.GUI.CustomControls.TabRadioButton();
            this.radioOnEvent = new SuchByte.MacroDeck.GUI.CustomControls.TabRadioButton();
            this.groupButtonState = new System.Windows.Forms.GroupBox();
            this.groupHotkey = new System.Windows.Forms.GroupBox();
            this.lblHotkeyInfo = new System.Windows.Forms.Label();
            this.btnRemoveHotkey = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.hotkey = new SuchByte.MacroDeck.GUI.CustomControls.RoundedTextBox();
            this.btnEditJson = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            ((System.ComponentModel.ISupportInitialize)(this.btnPreview)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.fontSize)).BeginInit();
            this.panel1.SuspendLayout();
            this.panel2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.btnEditIcon)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnRemoveIcon)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnClearLabelText)).BeginInit();
            this.groupAppearance.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.btnOpenTemplateEditor)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnAddVariable)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnDeleteStateBinding)).BeginInit();
            this.flowLayoutPanel1.SuspendLayout();
            this.groupButtonState.SuspendLayout();
            this.groupHotkey.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.btnRemoveHotkey)).BeginInit();
            this.SuspendLayout();
            // 
            // btnApply
            // 
            this.btnApply.BorderRadius = 8;
            this.btnApply.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnApply.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnApply.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnApply.ForeColor = System.Drawing.Color.White;
            this.btnApply.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnApply.Icon = null;
            this.btnApply.Location = new System.Drawing.Point(1117, 601);
            this.btnApply.Name = "btnApply";
            this.btnApply.Progress = 0;
            this.btnApply.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnApply.Size = new System.Drawing.Size(75, 25);
            this.btnApply.TabIndex = 1;
            this.btnApply.Text = "Apply";
            this.btnApply.UseMnemonic = false;
            this.btnApply.UseVisualStyleBackColor = true;
            this.btnApply.UseWindowsAccentColor = true;
            this.btnApply.Click += new System.EventHandler(this.BtnSave_Click);
            // 
            // btnPreview
            // 
            this.btnPreview.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.btnPreview.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnPreview.Column = 0;
            this.btnPreview.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnPreview.Font = new System.Drawing.Font("Tahoma", 15.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.btnPreview.ForeColor = System.Drawing.Color.White;
            this.btnPreview.ForegroundImage = null;
            this.btnPreview.Location = new System.Drawing.Point(94, 62);
            this.btnPreview.Name = "btnPreview";
            this.btnPreview.Radius = 40;
            this.btnPreview.Row = 0;
            this.btnPreview.ShowGIFIndicator = false;
            this.btnPreview.ShowKeyboardHotkeyIndicator = false;
            this.btnPreview.Size = new System.Drawing.Size(118, 118);
            this.btnPreview.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.btnPreview.TabIndex = 3;
            this.btnPreview.TabStop = false;
            this.btnPreview.Click += new System.EventHandler(this.BtnPreview_Click);
            // 
            // labelText
            // 
            this.labelText.AutoScroll = true;
            this.labelText.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.labelText.Cursor = System.Windows.Forms.Cursors.Hand;
            this.labelText.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.labelText.Icon = null;
            this.labelText.Location = new System.Drawing.Point(9, 217);
            this.labelText.MaxCharacters = 32767;
            this.labelText.Multiline = true;
            this.labelText.Name = "labelText";
            this.labelText.Padding = new System.Windows.Forms.Padding(8, 5, 8, 5);
            this.labelText.PasswordChar = false;
            this.labelText.PlaceHolderColor = System.Drawing.Color.Gray;
            this.labelText.PlaceHolderText = "";
            this.labelText.ReadOnly = false;
            this.labelText.ScrollBars = System.Windows.Forms.ScrollBars.None;
            this.labelText.SelectionStart = 0;
            this.labelText.Size = new System.Drawing.Size(258, 115);
            this.labelText.TabIndex = 23;
            this.labelText.TabStop = false;
            this.labelText.TextAlignment = System.Windows.Forms.HorizontalAlignment.Center;
            this.labelText.TextChanged += new System.EventHandler(this.LabelChanged);
            // 
            // fontSize
            // 
            this.fontSize.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.fontSize.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.fontSize.ForeColor = System.Drawing.Color.White;
            this.fontSize.Location = new System.Drawing.Point(206, 370);
            this.fontSize.Maximum = new decimal(new int[] {
            18,
            0,
            0,
            0});
            this.fontSize.Minimum = new decimal(new int[] {
            4,
            0,
            0,
            0});
            this.fontSize.Name = "fontSize";
            this.fontSize.Size = new System.Drawing.Size(54, 27);
            this.fontSize.TabIndex = 10;
            this.fontSize.Value = new decimal(new int[] {
            6,
            0,
            0,
            0});
            this.fontSize.ValueChanged += new System.EventHandler(this.LabelChanged);
            // 
            // lblButtonState
            // 
            this.lblButtonState.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblButtonState.Location = new System.Drawing.Point(3, 5);
            this.lblButtonState.Name = "lblButtonState";
            this.lblButtonState.Size = new System.Drawing.Size(130, 19);
            this.lblButtonState.TabIndex = 12;
            this.lblButtonState.Text = "Button state";
            this.lblButtonState.UseMnemonic = false;
            // 
            // radioButtonOff
            // 
            this.radioButtonOff.Checked = true;
            this.radioButtonOff.Cursor = System.Windows.Forms.Cursors.Hand;
            this.radioButtonOff.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioButtonOff.Location = new System.Drawing.Point(139, 3);
            this.radioButtonOff.Name = "radioButtonOff";
            this.radioButtonOff.Size = new System.Drawing.Size(70, 23);
            this.radioButtonOff.TabIndex = 13;
            this.radioButtonOff.TabStop = true;
            this.radioButtonOff.Text = "Off";
            this.radioButtonOff.UseMnemonic = false;
            this.radioButtonOff.UseVisualStyleBackColor = true;
            this.radioButtonOff.CheckedChanged += new System.EventHandler(this.RadioButton_CheckedChanged);
            // 
            // radioButtonOn
            // 
            this.radioButtonOn.Cursor = System.Windows.Forms.Cursors.Hand;
            this.radioButtonOn.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioButtonOn.Location = new System.Drawing.Point(215, 3);
            this.radioButtonOn.Name = "radioButtonOn";
            this.radioButtonOn.Size = new System.Drawing.Size(70, 23);
            this.radioButtonOn.TabIndex = 14;
            this.radioButtonOn.Text = "On";
            this.radioButtonOn.UseMnemonic = false;
            this.radioButtonOn.UseVisualStyleBackColor = true;
            this.radioButtonOn.CheckedChanged += new System.EventHandler(this.RadioButton_CheckedChanged);
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.lblButtonState);
            this.panel1.Controls.Add(this.radioButtonOn);
            this.panel1.Controls.Add(this.radioButtonOff);
            this.panel1.Location = new System.Drawing.Point(8, 23);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(291, 28);
            this.panel1.TabIndex = 15;
            // 
            // panel2
            // 
            this.panel2.Controls.Add(this.labelAlignBottom);
            this.panel2.Controls.Add(this.labelAlignCenter);
            this.panel2.Controls.Add(this.labelAlignTop);
            this.panel2.Location = new System.Drawing.Point(8, 338);
            this.panel2.Name = "panel2";
            this.panel2.Size = new System.Drawing.Size(285, 28);
            this.panel2.TabIndex = 16;
            // 
            // labelAlignBottom
            // 
            this.labelAlignBottom.AutoSize = true;
            this.labelAlignBottom.Checked = true;
            this.labelAlignBottom.Cursor = System.Windows.Forms.Cursors.Hand;
            this.labelAlignBottom.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.labelAlignBottom.Location = new System.Drawing.Point(186, 3);
            this.labelAlignBottom.Name = "labelAlignBottom";
            this.labelAlignBottom.Size = new System.Drawing.Size(74, 22);
            this.labelAlignBottom.TabIndex = 11;
            this.labelAlignBottom.TabStop = true;
            this.labelAlignBottom.Text = "Bottom";
            this.labelAlignBottom.UseMnemonic = false;
            this.labelAlignBottom.UseVisualStyleBackColor = true;
            this.labelAlignBottom.CheckedChanged += new System.EventHandler(this.LabelChanged);
            // 
            // labelAlignCenter
            // 
            this.labelAlignCenter.AutoSize = true;
            this.labelAlignCenter.Cursor = System.Windows.Forms.Cursors.Hand;
            this.labelAlignCenter.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.labelAlignCenter.Location = new System.Drawing.Point(91, 3);
            this.labelAlignCenter.Name = "labelAlignCenter";
            this.labelAlignCenter.Size = new System.Drawing.Size(69, 22);
            this.labelAlignCenter.TabIndex = 10;
            this.labelAlignCenter.Text = "Center";
            this.labelAlignCenter.UseMnemonic = false;
            this.labelAlignCenter.UseVisualStyleBackColor = true;
            this.labelAlignCenter.CheckedChanged += new System.EventHandler(this.LabelChanged);
            // 
            // labelAlignTop
            // 
            this.labelAlignTop.AutoSize = true;
            this.labelAlignTop.Cursor = System.Windows.Forms.Cursors.Hand;
            this.labelAlignTop.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.labelAlignTop.Location = new System.Drawing.Point(3, 3);
            this.labelAlignTop.Name = "labelAlignTop";
            this.labelAlignTop.Size = new System.Drawing.Size(52, 22);
            this.labelAlignTop.TabIndex = 9;
            this.labelAlignTop.Text = "Top";
            this.labelAlignTop.UseMnemonic = false;
            this.labelAlignTop.UseVisualStyleBackColor = true;
            this.labelAlignTop.CheckedChanged += new System.EventHandler(this.LabelChanged);
            // 
            // btnEditIcon
            // 
            this.btnEditIcon.BackColor = System.Drawing.Color.Transparent;
            this.btnEditIcon.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Edit_Normal;
            this.btnEditIcon.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnEditIcon.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnEditIcon.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnEditIcon.ForeColor = System.Drawing.Color.White;
            this.btnEditIcon.HoverImage = global::SuchByte.MacroDeck.Properties.Resources.Edit_Hover;
            this.btnEditIcon.Location = new System.Drawing.Point(126, 184);
            this.btnEditIcon.Name = "btnEditIcon";
            this.btnEditIcon.Size = new System.Drawing.Size(25, 25);
            this.btnEditIcon.TabIndex = 17;
            this.btnEditIcon.TabStop = false;
            this.btnEditIcon.Click += new System.EventHandler(this.BtnEditIcon_Click);
            // 
            // btnRemoveIcon
            // 
            this.btnRemoveIcon.BackColor = System.Drawing.Color.Transparent;
            this.btnRemoveIcon.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Delete_Normal;
            this.btnRemoveIcon.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnRemoveIcon.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnRemoveIcon.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnRemoveIcon.ForeColor = System.Drawing.Color.White;
            this.btnRemoveIcon.HoverImage = global::SuchByte.MacroDeck.Properties.Resources.Delete_Hover;
            this.btnRemoveIcon.Location = new System.Drawing.Point(156, 184);
            this.btnRemoveIcon.Name = "btnRemoveIcon";
            this.btnRemoveIcon.Size = new System.Drawing.Size(25, 25);
            this.btnRemoveIcon.TabIndex = 18;
            this.btnRemoveIcon.TabStop = false;
            this.btnRemoveIcon.Click += new System.EventHandler(this.BtnRemoveIcon_Click);
            // 
            // btnClearLabelText
            // 
            this.btnClearLabelText.BackColor = System.Drawing.Color.Transparent;
            this.btnClearLabelText.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Delete_Normal;
            this.btnClearLabelText.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnClearLabelText.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnClearLabelText.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnClearLabelText.ForeColor = System.Drawing.Color.White;
            this.btnClearLabelText.HoverImage = global::SuchByte.MacroDeck.Properties.Resources.Delete_Hover;
            this.btnClearLabelText.Location = new System.Drawing.Point(271, 254);
            this.btnClearLabelText.Name = "btnClearLabelText";
            this.btnClearLabelText.Size = new System.Drawing.Size(27, 27);
            this.btnClearLabelText.TabIndex = 19;
            this.btnClearLabelText.TabStop = false;
            this.btnClearLabelText.Click += new System.EventHandler(this.BtnClearLabelText_Click);
            // 
            // groupAppearance
            // 
            this.groupAppearance.Controls.Add(this.btnOpenTemplateEditor);
            this.groupAppearance.Controls.Add(this.btnAddVariable);
            this.groupAppearance.Controls.Add(this.btnForeColor);
            this.groupAppearance.Controls.Add(this.fonts);
            this.groupAppearance.Controls.Add(this.btnClearLabelText);
            this.groupAppearance.Controls.Add(this.panel2);
            this.groupAppearance.Controls.Add(this.labelText);
            this.groupAppearance.Controls.Add(this.panel1);
            this.groupAppearance.Controls.Add(this.btnEditIcon);
            this.groupAppearance.Controls.Add(this.btnRemoveIcon);
            this.groupAppearance.Controls.Add(this.fontSize);
            this.groupAppearance.Controls.Add(this.btnPreview);
            this.groupAppearance.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.groupAppearance.ForeColor = System.Drawing.Color.White;
            this.groupAppearance.Location = new System.Drawing.Point(4, 4);
            this.groupAppearance.Name = "groupAppearance";
            this.groupAppearance.Size = new System.Drawing.Size(307, 407);
            this.groupAppearance.TabIndex = 20;
            this.groupAppearance.TabStop = false;
            this.groupAppearance.Text = "Appearance";
            // 
            // btnOpenTemplateEditor
            // 
            this.btnOpenTemplateEditor.BackColor = System.Drawing.Color.Transparent;
            this.btnOpenTemplateEditor.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Arrow_Top_Right_Normal;
            this.btnOpenTemplateEditor.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnOpenTemplateEditor.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnOpenTemplateEditor.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnOpenTemplateEditor.ForeColor = System.Drawing.Color.White;
            this.btnOpenTemplateEditor.HoverImage = global::SuchByte.MacroDeck.Properties.Resources.Arrow_Top_Right_Hover;
            this.btnOpenTemplateEditor.Location = new System.Drawing.Point(271, 221);
            this.btnOpenTemplateEditor.Name = "btnOpenTemplateEditor";
            this.btnOpenTemplateEditor.Size = new System.Drawing.Size(27, 27);
            this.btnOpenTemplateEditor.TabIndex = 24;
            this.btnOpenTemplateEditor.TabStop = false;
            this.btnOpenTemplateEditor.Click += new System.EventHandler(this.BtnOpenTemplateEditor_Click);
            // 
            // btnAddVariable
            // 
            this.btnAddVariable.BackColor = System.Drawing.Color.Transparent;
            this.btnAddVariable.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Variable_Normal;
            this.btnAddVariable.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnAddVariable.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnAddVariable.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnAddVariable.ForeColor = System.Drawing.Color.White;
            this.btnAddVariable.HoverImage = global::SuchByte.MacroDeck.Properties.Resources.Variable_Hover;
            this.btnAddVariable.Location = new System.Drawing.Point(271, 287);
            this.btnAddVariable.Name = "btnAddVariable";
            this.btnAddVariable.Size = new System.Drawing.Size(27, 27);
            this.btnAddVariable.TabIndex = 22;
            this.btnAddVariable.TabStop = false;
            this.btnAddVariable.Click += new System.EventHandler(this.BtnAddVariable_Click);
            // 
            // btnForeColor
            // 
            this.btnForeColor.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnForeColor.BorderRadius = 8;
            this.btnForeColor.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnForeColor.FlatAppearance.BorderSize = 0;
            this.btnForeColor.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnForeColor.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnForeColor.ForeColor = System.Drawing.Color.White;
            this.btnForeColor.HoverColor = System.Drawing.Color.Transparent;
            this.btnForeColor.Icon = global::SuchByte.MacroDeck.Properties.Resources.Palette;
            this.btnForeColor.Location = new System.Drawing.Point(266, 370);
            this.btnForeColor.Name = "btnForeColor";
            this.btnForeColor.Progress = 0;
            this.btnForeColor.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnForeColor.Size = new System.Drawing.Size(27, 27);
            this.btnForeColor.TabIndex = 21;
            this.btnForeColor.UseMnemonic = false;
            this.btnForeColor.UseVisualStyleBackColor = false;
            this.btnForeColor.UseWindowsAccentColor = false;
            this.btnForeColor.Click += new System.EventHandler(this.BtnForeColor_Click);
            // 
            // fonts
            // 
            this.fonts.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.fonts.Cursor = System.Windows.Forms.Cursors.Hand;
            this.fonts.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.fonts.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.fonts.ForeColor = System.Drawing.Color.White;
            this.fonts.Icon = null;
            this.fonts.Location = new System.Drawing.Point(8, 369);
            this.fonts.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.fonts.Name = "fonts";
            this.fonts.Padding = new System.Windows.Forms.Padding(8, 2, 8, 2);
            this.fonts.SelectedIndex = -1;
            this.fonts.SelectedItem = null;
            this.fonts.Size = new System.Drawing.Size(192, 28);
            this.fonts.TabIndex = 20;
            this.fonts.SelectedIndexChanged += new System.EventHandler(this.LabelChanged);
            // 
            // lblCurrentState
            // 
            this.lblCurrentState.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblCurrentState.Location = new System.Drawing.Point(162, 26);
            this.lblCurrentState.Name = "lblCurrentState";
            this.lblCurrentState.Size = new System.Drawing.Size(66, 16);
            this.lblCurrentState.TabIndex = 22;
            this.lblCurrentState.Text = "Off";
            this.lblCurrentState.UseMnemonic = false;
            // 
            // lblCurrentStateLabel
            // 
            this.lblCurrentStateLabel.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblCurrentStateLabel.Location = new System.Drawing.Point(6, 23);
            this.lblCurrentStateLabel.Name = "lblCurrentStateLabel";
            this.lblCurrentStateLabel.Size = new System.Drawing.Size(150, 19);
            this.lblCurrentStateLabel.TabIndex = 23;
            this.lblCurrentStateLabel.Text = "Current state:";
            this.lblCurrentStateLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblCurrentStateLabel.UseMnemonic = false;
            // 
            // btnOk
            // 
            this.btnOk.BorderRadius = 8;
            this.btnOk.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnOk.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOk.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnOk.ForeColor = System.Drawing.Color.White;
            this.btnOk.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnOk.Icon = null;
            this.btnOk.Location = new System.Drawing.Point(1036, 601);
            this.btnOk.Name = "btnOk";
            this.btnOk.Progress = 0;
            this.btnOk.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnOk.Size = new System.Drawing.Size(75, 25);
            this.btnOk.TabIndex = 25;
            this.btnOk.Text = "Ok";
            this.btnOk.UseMnemonic = false;
            this.btnOk.UseVisualStyleBackColor = true;
            this.btnOk.UseWindowsAccentColor = true;
            this.btnOk.Click += new System.EventHandler(this.BtnOk_Click);
            // 
            // variablesContextMenu
            // 
            this.variablesContextMenu.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.variablesContextMenu.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.variablesContextMenu.Name = "variablesContextMenu";
            this.variablesContextMenu.ShowImageMargin = false;
            this.variablesContextMenu.ShowItemToolTips = false;
            this.variablesContextMenu.Size = new System.Drawing.Size(36, 4);
            // 
            // lblStateBinding
            // 
            this.lblStateBinding.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblStateBinding.Location = new System.Drawing.Point(6, 51);
            this.lblStateBinding.Name = "lblStateBinding";
            this.lblStateBinding.Size = new System.Drawing.Size(272, 19);
            this.lblStateBinding.TabIndex = 26;
            this.lblStateBinding.Text = "State binding:";
            this.lblStateBinding.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblStateBinding.UseMnemonic = false;
            // 
            // listStateBinding
            // 
            this.listStateBinding.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.listStateBinding.Cursor = System.Windows.Forms.Cursors.Hand;
            this.listStateBinding.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.listStateBinding.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.listStateBinding.ForeColor = System.Drawing.Color.White;
            this.listStateBinding.Icon = null;
            this.listStateBinding.Location = new System.Drawing.Point(6, 73);
            this.listStateBinding.Name = "listStateBinding";
            this.listStateBinding.Padding = new System.Windows.Forms.Padding(8, 2, 8, 2);
            this.listStateBinding.SelectedIndex = -1;
            this.listStateBinding.SelectedItem = null;
            this.listStateBinding.Size = new System.Drawing.Size(262, 26);
            this.listStateBinding.TabIndex = 27;
            this.listStateBinding.SelectedIndexChanged += new System.EventHandler(this.ListStateBinding_SelectedIndexChanged);
            // 
            // btnDeleteStateBinding
            // 
            this.btnDeleteStateBinding.BackColor = System.Drawing.Color.Transparent;
            this.btnDeleteStateBinding.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Delete_Normal;
            this.btnDeleteStateBinding.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnDeleteStateBinding.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnDeleteStateBinding.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnDeleteStateBinding.ForeColor = System.Drawing.Color.White;
            this.btnDeleteStateBinding.HoverImage = global::SuchByte.MacroDeck.Properties.Resources.Delete_Hover;
            this.btnDeleteStateBinding.Location = new System.Drawing.Point(271, 73);
            this.btnDeleteStateBinding.Name = "btnDeleteStateBinding";
            this.btnDeleteStateBinding.Size = new System.Drawing.Size(27, 27);
            this.btnDeleteStateBinding.TabIndex = 28;
            this.btnDeleteStateBinding.TabStop = false;
            this.btnDeleteStateBinding.Click += new System.EventHandler(this.BtnDeleteStateBinding_Click);
            // 
            // selectorPanel
            // 
            this.selectorPanel.Location = new System.Drawing.Point(317, 54);
            this.selectorPanel.Name = "selectorPanel";
            this.selectorPanel.Size = new System.Drawing.Size(879, 541);
            this.selectorPanel.TabIndex = 29;
            // 
            // flowLayoutPanel1
            // 
            this.flowLayoutPanel1.Controls.Add(this.radioOnPress);
            this.flowLayoutPanel1.Controls.Add(this.radioOnRelease);
            this.flowLayoutPanel1.Controls.Add(this.radioOnLongPress);
            this.flowLayoutPanel1.Controls.Add(this.radioOnLongPressRelease);
            this.flowLayoutPanel1.Controls.Add(this.radioOnEvent);
            this.flowLayoutPanel1.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.flowLayoutPanel1.Location = new System.Drawing.Point(317, 17);
            this.flowLayoutPanel1.Name = "flowLayoutPanel1";
            this.flowLayoutPanel1.Size = new System.Drawing.Size(838, 31);
            this.flowLayoutPanel1.TabIndex = 30;
            // 
            // radioOnPress
            // 
            this.radioOnPress.AutoSize = true;
            this.radioOnPress.Checked = true;
            this.radioOnPress.Cursor = System.Windows.Forms.Cursors.Hand;
            this.radioOnPress.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioOnPress.Location = new System.Drawing.Point(3, 3);
            this.radioOnPress.Name = "radioOnPress";
            this.radioOnPress.Size = new System.Drawing.Size(85, 22);
            this.radioOnPress.TabIndex = 0;
            this.radioOnPress.TabStop = true;
            this.radioOnPress.Text = "On press";
            this.radioOnPress.UseMnemonic = false;
            this.radioOnPress.UseVisualStyleBackColor = true;
            this.radioOnPress.CheckedChanged += new System.EventHandler(this.RadioOnPress_CheckedChanged);
            // 
            // radioOnRelease
            // 
            this.radioOnRelease.AutoSize = true;
            this.radioOnRelease.Cursor = System.Windows.Forms.Cursors.Hand;
            this.radioOnRelease.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioOnRelease.Location = new System.Drawing.Point(94, 3);
            this.radioOnRelease.Name = "radioOnRelease";
            this.radioOnRelease.Size = new System.Drawing.Size(96, 22);
            this.radioOnRelease.TabIndex = 2;
            this.radioOnRelease.Text = "On release";
            this.radioOnRelease.UseMnemonic = false;
            this.radioOnRelease.UseVisualStyleBackColor = true;
            this.radioOnRelease.CheckedChanged += new System.EventHandler(this.RadioOnRelease_CheckedChanged);
            // 
            // radioOnLongPress
            // 
            this.radioOnLongPress.AutoSize = true;
            this.radioOnLongPress.Cursor = System.Windows.Forms.Cursors.Hand;
            this.radioOnLongPress.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioOnLongPress.Location = new System.Drawing.Point(196, 3);
            this.radioOnLongPress.Name = "radioOnLongPress";
            this.radioOnLongPress.Size = new System.Drawing.Size(116, 22);
            this.radioOnLongPress.TabIndex = 3;
            this.radioOnLongPress.Text = "On long press";
            this.radioOnLongPress.UseMnemonic = false;
            this.radioOnLongPress.UseVisualStyleBackColor = true;
            this.radioOnLongPress.CheckedChanged += new System.EventHandler(this.RadioOnLongPress_CheckedChanged);
            // 
            // radioOnLongPressRelease
            // 
            this.radioOnLongPressRelease.AutoSize = true;
            this.radioOnLongPressRelease.Cursor = System.Windows.Forms.Cursors.Hand;
            this.radioOnLongPressRelease.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioOnLongPressRelease.Location = new System.Drawing.Point(318, 3);
            this.radioOnLongPressRelease.Name = "radioOnLongPressRelease";
            this.radioOnLongPressRelease.Size = new System.Drawing.Size(167, 22);
            this.radioOnLongPressRelease.TabIndex = 4;
            this.radioOnLongPressRelease.Text = "On long press release";
            this.radioOnLongPressRelease.UseMnemonic = false;
            this.radioOnLongPressRelease.UseVisualStyleBackColor = true;
            this.radioOnLongPressRelease.CheckedChanged += new System.EventHandler(this.RadioOnLongPressRelease_CheckedChanged);
            // 
            // radioOnEvent
            // 
            this.radioOnEvent.AutoSize = true;
            this.radioOnEvent.Cursor = System.Windows.Forms.Cursors.Hand;
            this.radioOnEvent.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioOnEvent.Location = new System.Drawing.Point(491, 3);
            this.radioOnEvent.Name = "radioOnEvent";
            this.radioOnEvent.Size = new System.Drawing.Size(87, 22);
            this.radioOnEvent.TabIndex = 1;
            this.radioOnEvent.Text = "On event";
            this.radioOnEvent.UseMnemonic = false;
            this.radioOnEvent.UseVisualStyleBackColor = true;
            this.radioOnEvent.CheckedChanged += new System.EventHandler(this.RadioOnEvent_CheckedChanged);
            // 
            // groupButtonState
            // 
            this.groupButtonState.Controls.Add(this.lblStateBinding);
            this.groupButtonState.Controls.Add(this.listStateBinding);
            this.groupButtonState.Controls.Add(this.btnDeleteStateBinding);
            this.groupButtonState.Controls.Add(this.lblCurrentState);
            this.groupButtonState.Controls.Add(this.lblCurrentStateLabel);
            this.groupButtonState.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.groupButtonState.ForeColor = System.Drawing.Color.White;
            this.groupButtonState.Location = new System.Drawing.Point(4, 414);
            this.groupButtonState.Name = "groupButtonState";
            this.groupButtonState.Size = new System.Drawing.Size(307, 110);
            this.groupButtonState.TabIndex = 31;
            this.groupButtonState.TabStop = false;
            this.groupButtonState.Text = "Button state";
            // 
            // groupHotkey
            // 
            this.groupHotkey.Controls.Add(this.lblHotkeyInfo);
            this.groupHotkey.Controls.Add(this.btnRemoveHotkey);
            this.groupHotkey.Controls.Add(this.hotkey);
            this.groupHotkey.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.groupHotkey.ForeColor = System.Drawing.Color.White;
            this.groupHotkey.Location = new System.Drawing.Point(4, 527);
            this.groupHotkey.Name = "groupHotkey";
            this.groupHotkey.Size = new System.Drawing.Size(307, 87);
            this.groupHotkey.TabIndex = 32;
            this.groupHotkey.TabStop = false;
            this.groupHotkey.Text = "Hotkey";
            // 
            // lblHotkeyInfo
            // 
            this.lblHotkeyInfo.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblHotkeyInfo.Location = new System.Drawing.Point(6, 53);
            this.lblHotkeyInfo.Name = "lblHotkeyInfo";
            this.lblHotkeyInfo.Size = new System.Drawing.Size(295, 29);
            this.lblHotkeyInfo.TabIndex = 21;
            this.lblHotkeyInfo.Text = "The hotkey executes the \"On press\" actions when pressed";
            this.lblHotkeyInfo.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblHotkeyInfo.UseMnemonic = false;
            // 
            // btnRemoveHotkey
            // 
            this.btnRemoveHotkey.BackColor = System.Drawing.Color.Transparent;
            this.btnRemoveHotkey.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Delete_Normal;
            this.btnRemoveHotkey.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnRemoveHotkey.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnRemoveHotkey.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnRemoveHotkey.ForeColor = System.Drawing.Color.White;
            this.btnRemoveHotkey.HoverImage = global::SuchByte.MacroDeck.Properties.Resources.Delete_Hover;
            this.btnRemoveHotkey.Location = new System.Drawing.Point(271, 23);
            this.btnRemoveHotkey.Name = "btnRemoveHotkey";
            this.btnRemoveHotkey.Size = new System.Drawing.Size(27, 27);
            this.btnRemoveHotkey.TabIndex = 20;
            this.btnRemoveHotkey.TabStop = false;
            this.btnRemoveHotkey.Click += new System.EventHandler(this.BtnRemoveHotkey_Click);
            // 
            // hotkey
            // 
            this.hotkey.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.hotkey.Cursor = System.Windows.Forms.Cursors.Hand;
            this.hotkey.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.hotkey.Icon = global::SuchByte.MacroDeck.Properties.Resources.Keyboard;
            this.hotkey.Location = new System.Drawing.Point(8, 25);
            this.hotkey.MaxCharacters = 32767;
            this.hotkey.Multiline = false;
            this.hotkey.Name = "hotkey";
            this.hotkey.Padding = new System.Windows.Forms.Padding(26, 5, 8, 5);
            this.hotkey.PasswordChar = false;
            this.hotkey.PlaceHolderColor = System.Drawing.Color.Gray;
            this.hotkey.PlaceHolderText = "";
            this.hotkey.ReadOnly = false;
            this.hotkey.ScrollBars = System.Windows.Forms.ScrollBars.None;
            this.hotkey.SelectionStart = 0;
            this.hotkey.Size = new System.Drawing.Size(260, 25);
            this.hotkey.TabIndex = 0;
            this.hotkey.TextAlignment = System.Windows.Forms.HorizontalAlignment.Left;
            // 
            // btnEditJson
            // 
            this.btnEditJson.BorderRadius = 8;
            this.btnEditJson.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnEditJson.FlatAppearance.BorderSize = 0;
            this.btnEditJson.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnEditJson.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnEditJson.ForeColor = System.Drawing.Color.White;
            this.btnEditJson.HoverColor = System.Drawing.Color.Empty;
            this.btnEditJson.Icon = null;
            this.btnEditJson.Location = new System.Drawing.Point(320, 601);
            this.btnEditJson.Name = "btnEditJson";
            this.btnEditJson.Progress = 0;
            this.btnEditJson.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(103)))), ((int)(((byte)(225)))));
            this.btnEditJson.Size = new System.Drawing.Size(150, 25);
            this.btnEditJson.TabIndex = 33;
            this.btnEditJson.Text = "Edit JSON";
            this.btnEditJson.UseMnemonic = false;
            this.btnEditJson.UseVisualStyleBackColor = true;
            this.btnEditJson.UseWindowsAccentColor = true;
            this.btnEditJson.Click += new System.EventHandler(this.BtnEditJson_Click);
            // 
            // ButtonEditor
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.ClientSize = new System.Drawing.Size(1200, 635);
            this.Controls.Add(this.btnEditJson);
            this.Controls.Add(this.groupHotkey);
            this.Controls.Add(this.groupButtonState);
            this.Controls.Add(this.flowLayoutPanel1);
            this.Controls.Add(this.selectorPanel);
            this.Controls.Add(this.btnOk);
            this.Controls.Add(this.btnApply);
            this.Controls.Add(this.groupAppearance);
            this.Location = new System.Drawing.Point(0, 0);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ButtonEditor";
            this.Text = "Macro Deck :: Edit button";
            this.Load += new System.EventHandler(this.ButtonEditor_Load);
            this.Controls.SetChildIndex(this.groupAppearance, 0);
            this.Controls.SetChildIndex(this.btnApply, 0);
            this.Controls.SetChildIndex(this.btnOk, 0);
            this.Controls.SetChildIndex(this.selectorPanel, 0);
            this.Controls.SetChildIndex(this.flowLayoutPanel1, 0);
            this.Controls.SetChildIndex(this.groupButtonState, 0);
            this.Controls.SetChildIndex(this.groupHotkey, 0);
            this.Controls.SetChildIndex(this.btnEditJson, 0);
            ((System.ComponentModel.ISupportInitialize)(this.btnPreview)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.fontSize)).EndInit();
            this.panel1.ResumeLayout(false);
            this.panel2.ResumeLayout(false);
            this.panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.btnEditIcon)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnRemoveIcon)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnClearLabelText)).EndInit();
            this.groupAppearance.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.btnOpenTemplateEditor)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnAddVariable)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnDeleteStateBinding)).EndInit();
            this.flowLayoutPanel1.ResumeLayout(false);
            this.flowLayoutPanel1.PerformLayout();
            this.groupButtonState.ResumeLayout(false);
            this.groupHotkey.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.btnRemoveHotkey)).EndInit();
            this.ResumeLayout(false);

        }


        #endregion
        private CustomControls.ButtonPrimary btnApply;
        private CustomControls.RoundedButton btnPreview;
        private RoundedTextBox labelText;
        private System.Windows.Forms.NumericUpDown fontSize;
        private System.Windows.Forms.Label lblButtonState;
        private CustomControls.TabRadioButton radioButtonOff;
        private CustomControls.TabRadioButton radioButtonOn;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Panel panel2;
        private CustomControls.TabRadioButton labelAlignBottom;
        private CustomControls.TabRadioButton labelAlignCenter;
        private CustomControls.TabRadioButton labelAlignTop;
        private PictureButton btnEditIcon;
        private PictureButton btnRemoveIcon;
        private PictureButton btnClearLabelText;
        private System.Windows.Forms.GroupBox groupAppearance;
        private RoundedComboBox fonts;
        private ButtonPrimary btnForeColor;
        private System.Windows.Forms.Label lblCurrentState;
        private System.Windows.Forms.Label lblCurrentStateLabel;
        private ButtonPrimary btnOk;
        private PictureButton btnAddVariable;
        protected System.Windows.Forms.ContextMenuStrip variablesContextMenu;
        private System.Windows.Forms.Label lblStateBinding;
        private RoundedComboBox listStateBinding;
        private PictureButton btnDeleteStateBinding;
        private System.Windows.Forms.Panel selectorPanel;
        private System.Windows.Forms.FlowLayoutPanel flowLayoutPanel1;
        private TabRadioButton radioOnPress;
        private TabRadioButton radioOnEvent;
        private System.Windows.Forms.GroupBox groupButtonState;
        private PictureButton btnOpenTemplateEditor;
        private System.Windows.Forms.GroupBox groupHotkey;
        private PictureButton btnRemoveHotkey;
        private RoundedTextBox hotkey;
        private System.Windows.Forms.Label lblHotkeyInfo;
        private TabRadioButton radioOnRelease;
        private TabRadioButton radioOnLongPress;
        private TabRadioButton radioOnLongPressRelease;
        private ButtonPrimary btnEditJson;
    }
}