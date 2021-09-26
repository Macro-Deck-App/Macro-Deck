using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Interfaces;
using System.Windows.Controls;

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
            this.lblPath = new System.Windows.Forms.Label();
            this.buttonPath = new System.Windows.Forms.Label();
            this.btnApply = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.btnPreview = new SuchByte.MacroDeck.GUI.CustomControls.RoundedButton();
            this.labelText = new SuchByte.MacroDeck.GUI.CustomControls.PlaceHolderTextBox();
            this.fontSize = new System.Windows.Forms.NumericUpDown();
            this.lblButtonState = new System.Windows.Forms.Label();
            this.radioButtonOff = new System.Windows.Forms.RadioButton();
            this.radioButtonOn = new System.Windows.Forms.RadioButton();
            this.panel1 = new System.Windows.Forms.Panel();
            this.panel2 = new System.Windows.Forms.Panel();
            this.labelAlignBottom = new System.Windows.Forms.RadioButton();
            this.labelAlignCenter = new System.Windows.Forms.RadioButton();
            this.labelAlignTop = new System.Windows.Forms.RadioButton();
            this.btnEditIcon = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.btnRemoveIcon = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.btnClearLabelText = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.groupAppearance = new System.Windows.Forms.GroupBox();
            this.btnAddVariable = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.btnForeColor = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.fonts = new System.Windows.Forms.ComboBox();
            this.horizontalTabControl1 = new SuchByte.MacroDeck.GUI.CustomControls.HorizontalTabControl();
            this.tabOnPress = new System.Windows.Forms.TabPage();
            this.tabOnEvent = new System.Windows.Forms.TabPage();
            this.lblCurrentState = new System.Windows.Forms.Label();
            this.lblCurrentStateLabel = new System.Windows.Forms.Label();
            this.btnOk = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.variablesContextMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            ((System.ComponentModel.ISupportInitialize)(this.btnPreview)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.fontSize)).BeginInit();
            this.panel1.SuspendLayout();
            this.panel2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.btnEditIcon)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnRemoveIcon)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnClearLabelText)).BeginInit();
            this.groupAppearance.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.btnAddVariable)).BeginInit();
            this.horizontalTabControl1.SuspendLayout();
            this.SuspendLayout();
            // 
            // lblPath
            // 
            this.lblPath.AutoSize = true;
            this.lblPath.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblPath.Location = new System.Drawing.Point(460, 29);
            this.lblPath.Name = "lblPath";
            this.lblPath.Size = new System.Drawing.Size(46, 19);
            this.lblPath.TabIndex = 0;
            this.lblPath.Text = "Path:";
            // 
            // buttonPath
            // 
            this.buttonPath.Location = new System.Drawing.Point(584, 32);
            this.buttonPath.Name = "buttonPath";
            this.buttonPath.Size = new System.Drawing.Size(355, 16);
            this.buttonPath.TabIndex = 0;
            this.buttonPath.Text = ".";
            // 
            // btnApply
            // 
            this.btnApply.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnApply.BorderRadius = 8;
            this.btnApply.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnApply.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnApply.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnApply.ForeColor = System.Drawing.Color.White;
            this.btnApply.Location = new System.Drawing.Point(864, 708);
            this.btnApply.Name = "btnApply";
            this.btnApply.Size = new System.Drawing.Size(75, 25);
            this.btnApply.TabIndex = 1;
            this.btnApply.Text = "Apply";
            this.btnApply.UseVisualStyleBackColor = true;
            this.btnApply.Click += new System.EventHandler(this.BtnSave_Click);
            // 
            // btnPreview
            // 
            this.btnPreview.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(108)))), ((int)(((byte)(117)))), ((int)(((byte)(125)))));
            this.btnPreview.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnPreview.Column = 0;
            this.btnPreview.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnPreview.Font = new System.Drawing.Font("Tahoma", 15.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.btnPreview.ForeColor = System.Drawing.Color.White;
            this.btnPreview.ForegroundImage = null;
            this.btnPreview.Location = new System.Drawing.Point(5, 51);
            this.btnPreview.Name = "btnPreview";
            this.btnPreview.Radius = 40;
            this.btnPreview.Row = 0;
            this.btnPreview.ShowGIFIndicator = false;
            this.btnPreview.Size = new System.Drawing.Size(118, 118);
            this.btnPreview.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.btnPreview.TabIndex = 3;
            this.btnPreview.TabStop = false;
            this.btnPreview.Click += new System.EventHandler(this.BtnPreview_Click);
            // 
            // labelText
            // 
            this.labelText.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.labelText.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.labelText.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Italic, System.Drawing.GraphicsUnit.Point);
            this.labelText.ForeColor = System.Drawing.Color.Gray;
            this.labelText.Location = new System.Drawing.Point(155, 51);
            this.labelText.Multiline = true;
            this.labelText.Name = "labelText";
            this.labelText.PlaceHolderText = "Add label";
            this.labelText.Size = new System.Drawing.Size(236, 85);
            this.labelText.TabIndex = 5;
            this.labelText.Text = "Add label";
            this.labelText.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.labelText.TextChanged += new System.EventHandler(this.LabelChanged);
            // 
            // fontSize
            // 
            this.fontSize.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.fontSize.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.fontSize.ForeColor = System.Drawing.Color.White;
            this.fontSize.Location = new System.Drawing.Point(337, 173);
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
            // 
            // radioButtonOff
            // 
            this.radioButtonOff.Checked = true;
            this.radioButtonOff.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioButtonOff.Location = new System.Drawing.Point(139, 3);
            this.radioButtonOff.Name = "radioButtonOff";
            this.radioButtonOff.Size = new System.Drawing.Size(70, 23);
            this.radioButtonOff.TabIndex = 13;
            this.radioButtonOff.TabStop = true;
            this.radioButtonOff.Text = "Off";
            this.radioButtonOff.UseVisualStyleBackColor = true;
            this.radioButtonOff.CheckedChanged += new System.EventHandler(this.RadioButton_CheckedChanged);
            // 
            // radioButtonOn
            // 
            this.radioButtonOn.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioButtonOn.Location = new System.Drawing.Point(215, 3);
            this.radioButtonOn.Name = "radioButtonOn";
            this.radioButtonOn.Size = new System.Drawing.Size(70, 23);
            this.radioButtonOn.TabIndex = 14;
            this.radioButtonOn.Text = "On";
            this.radioButtonOn.UseVisualStyleBackColor = true;
            this.radioButtonOn.CheckedChanged += new System.EventHandler(this.RadioButton_CheckedChanged);
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.lblButtonState);
            this.panel1.Controls.Add(this.radioButtonOn);
            this.panel1.Controls.Add(this.radioButtonOff);
            this.panel1.Location = new System.Drawing.Point(75, 16);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(293, 28);
            this.panel1.TabIndex = 15;
            // 
            // panel2
            // 
            this.panel2.Controls.Add(this.labelAlignBottom);
            this.panel2.Controls.Add(this.labelAlignCenter);
            this.panel2.Controls.Add(this.labelAlignTop);
            this.panel2.Location = new System.Drawing.Point(155, 141);
            this.panel2.Name = "panel2";
            this.panel2.Size = new System.Drawing.Size(280, 28);
            this.panel2.TabIndex = 16;
            // 
            // labelAlignBottom
            // 
            this.labelAlignBottom.AutoSize = true;
            this.labelAlignBottom.Checked = true;
            this.labelAlignBottom.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.labelAlignBottom.Location = new System.Drawing.Point(186, 3);
            this.labelAlignBottom.Name = "labelAlignBottom";
            this.labelAlignBottom.Size = new System.Drawing.Size(74, 22);
            this.labelAlignBottom.TabIndex = 11;
            this.labelAlignBottom.TabStop = true;
            this.labelAlignBottom.Text = "Bottom";
            this.labelAlignBottom.UseVisualStyleBackColor = true;
            this.labelAlignBottom.CheckedChanged += new System.EventHandler(this.LabelChanged);
            // 
            // labelAlignCenter
            // 
            this.labelAlignCenter.AutoSize = true;
            this.labelAlignCenter.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.labelAlignCenter.Location = new System.Drawing.Point(91, 3);
            this.labelAlignCenter.Name = "labelAlignCenter";
            this.labelAlignCenter.Size = new System.Drawing.Size(69, 22);
            this.labelAlignCenter.TabIndex = 10;
            this.labelAlignCenter.Text = "Center";
            this.labelAlignCenter.UseVisualStyleBackColor = true;
            this.labelAlignCenter.CheckedChanged += new System.EventHandler(this.LabelChanged);
            // 
            // labelAlignTop
            // 
            this.labelAlignTop.AutoSize = true;
            this.labelAlignTop.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.labelAlignTop.Location = new System.Drawing.Point(3, 3);
            this.labelAlignTop.Name = "labelAlignTop";
            this.labelAlignTop.Size = new System.Drawing.Size(52, 22);
            this.labelAlignTop.TabIndex = 9;
            this.labelAlignTop.Text = "Top";
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
            this.btnEditIcon.Location = new System.Drawing.Point(37, 173);
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
            this.btnRemoveIcon.Location = new System.Drawing.Point(67, 173);
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
            this.btnClearLabelText.Location = new System.Drawing.Point(397, 51);
            this.btnClearLabelText.Name = "btnClearLabelText";
            this.btnClearLabelText.Size = new System.Drawing.Size(27, 27);
            this.btnClearLabelText.TabIndex = 19;
            this.btnClearLabelText.TabStop = false;
            this.btnClearLabelText.Click += new System.EventHandler(this.BtnClearLabelText_Click);
            // 
            // groupAppearance
            // 
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
            this.groupAppearance.Location = new System.Drawing.Point(12, 7);
            this.groupAppearance.Name = "groupAppearance";
            this.groupAppearance.Size = new System.Drawing.Size(442, 207);
            this.groupAppearance.TabIndex = 20;
            this.groupAppearance.TabStop = false;
            this.groupAppearance.Text = "Appearance";
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
            this.btnAddVariable.Location = new System.Drawing.Point(397, 84);
            this.btnAddVariable.Name = "btnAddVariable";
            this.btnAddVariable.Size = new System.Drawing.Size(27, 27);
            this.btnAddVariable.TabIndex = 22;
            this.btnAddVariable.TabStop = false;
            this.btnAddVariable.Click += new System.EventHandler(this.BtnAddVariable_Click);
            // 
            // btnForeColor
            // 
            this.btnForeColor.BackColor = System.Drawing.Color.Transparent;
            this.btnForeColor.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.icon_color_picker;
            this.btnForeColor.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnForeColor.BorderRadius = 8;
            this.btnForeColor.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnForeColor.FlatAppearance.BorderSize = 0;
            this.btnForeColor.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnForeColor.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnForeColor.ForeColor = System.Drawing.Color.White;
            this.btnForeColor.Location = new System.Drawing.Point(405, 173);
            this.btnForeColor.Name = "btnForeColor";
            this.btnForeColor.Size = new System.Drawing.Size(27, 27);
            this.btnForeColor.TabIndex = 21;
            this.btnForeColor.UseVisualStyleBackColor = false;
            this.btnForeColor.Click += new System.EventHandler(this.BtnForeColor_Click);
            // 
            // fonts
            // 
            this.fonts.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.fonts.FormattingEnabled = true;
            this.fonts.Location = new System.Drawing.Point(155, 173);
            this.fonts.Name = "fonts";
            this.fonts.Size = new System.Drawing.Size(176, 27);
            this.fonts.TabIndex = 20;
            this.fonts.SelectedIndexChanged += new System.EventHandler(this.LabelChanged);
            // 
            // horizontalTabControl1
            // 
            this.horizontalTabControl1.BackgroundColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.horizontalTabControl1.Controls.Add(this.tabOnPress);
            this.horizontalTabControl1.Controls.Add(this.tabOnEvent);
            this.horizontalTabControl1.ItemSize = new System.Drawing.Size(136, 32);
            this.horizontalTabControl1.Location = new System.Drawing.Point(7, 221);
            this.horizontalTabControl1.Name = "horizontalTabControl1";
            this.horizontalTabControl1.SelectedIndex = 0;
            this.horizontalTabControl1.Size = new System.Drawing.Size(939, 485);
            this.horizontalTabControl1.SizeMode = System.Windows.Forms.TabSizeMode.Fixed;
            this.horizontalTabControl1.TabIndex = 21;
            // 
            // tabOnPress
            // 
            this.tabOnPress.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.tabOnPress.ForeColor = System.Drawing.Color.White;
            this.tabOnPress.Location = new System.Drawing.Point(4, 36);
            this.tabOnPress.Name = "tabOnPress";
            this.tabOnPress.Padding = new System.Windows.Forms.Padding(3);
            this.tabOnPress.Size = new System.Drawing.Size(931, 445);
            this.tabOnPress.TabIndex = 0;
            this.tabOnPress.Text = "On Press";
            // 
            // tabOnEvent
            // 
            this.tabOnEvent.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.tabOnEvent.Location = new System.Drawing.Point(4, 36);
            this.tabOnEvent.Name = "tabOnEvent";
            this.tabOnEvent.Size = new System.Drawing.Size(931, 445);
            this.tabOnEvent.TabIndex = 1;
            this.tabOnEvent.Text = "On Event";
            // 
            // lblCurrentState
            // 
            this.lblCurrentState.Location = new System.Drawing.Point(584, 60);
            this.lblCurrentState.Name = "lblCurrentState";
            this.lblCurrentState.Size = new System.Drawing.Size(29, 16);
            this.lblCurrentState.TabIndex = 22;
            this.lblCurrentState.Text = "Off";
            // 
            // lblCurrentStateLabel
            // 
            this.lblCurrentStateLabel.AutoSize = true;
            this.lblCurrentStateLabel.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblCurrentStateLabel.Location = new System.Drawing.Point(460, 57);
            this.lblCurrentStateLabel.Name = "lblCurrentStateLabel";
            this.lblCurrentStateLabel.Size = new System.Drawing.Size(106, 19);
            this.lblCurrentStateLabel.TabIndex = 23;
            this.lblCurrentStateLabel.Text = "Current state:";
            // 
            // btnOk
            // 
            this.btnOk.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnOk.BorderRadius = 8;
            this.btnOk.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnOk.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOk.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnOk.ForeColor = System.Drawing.Color.White;
            this.btnOk.Location = new System.Drawing.Point(783, 708);
            this.btnOk.Name = "btnOk";
            this.btnOk.Size = new System.Drawing.Size(75, 25);
            this.btnOk.TabIndex = 25;
            this.btnOk.Text = "Ok";
            this.btnOk.UseVisualStyleBackColor = true;
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
            // ButtonEditor
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.ClientSize = new System.Drawing.Size(951, 744);
            this.Controls.Add(this.btnOk);
            this.Controls.Add(this.lblCurrentState);
            this.Controls.Add(this.lblCurrentStateLabel);
            this.Controls.Add(this.horizontalTabControl1);
            this.Controls.Add(this.btnApply);
            this.Controls.Add(this.buttonPath);
            this.Controls.Add(this.lblPath);
            this.Controls.Add(this.groupAppearance);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ButtonEditor";
            this.Text = "Macro Deck :: Edit button";
            this.Load += new System.EventHandler(this.ButtonEditor_Load);
            this.Controls.SetChildIndex(this.groupAppearance, 0);
            this.Controls.SetChildIndex(this.lblPath, 0);
            this.Controls.SetChildIndex(this.buttonPath, 0);
            this.Controls.SetChildIndex(this.btnApply, 0);
            this.Controls.SetChildIndex(this.horizontalTabControl1, 0);
            this.Controls.SetChildIndex(this.lblCurrentStateLabel, 0);
            this.Controls.SetChildIndex(this.lblCurrentState, 0);
            this.Controls.SetChildIndex(this.btnOk, 0);
            ((System.ComponentModel.ISupportInitialize)(this.btnPreview)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.fontSize)).EndInit();
            this.panel1.ResumeLayout(false);
            this.panel2.ResumeLayout(false);
            this.panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.btnEditIcon)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnRemoveIcon)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnClearLabelText)).EndInit();
            this.groupAppearance.ResumeLayout(false);
            this.groupAppearance.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.btnAddVariable)).EndInit();
            this.horizontalTabControl1.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblPath;
        private System.Windows.Forms.Label buttonPath;
        private CustomControls.ButtonPrimary btnApply;
        private CustomControls.RoundedButton btnPreview;
        private PlaceHolderTextBox labelText;
        private System.Windows.Forms.NumericUpDown fontSize;
        private System.Windows.Forms.Label lblButtonState;
        private System.Windows.Forms.RadioButton radioButtonOff;
        private System.Windows.Forms.RadioButton radioButtonOn;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Panel panel2;
        private System.Windows.Forms.RadioButton labelAlignBottom;
        private System.Windows.Forms.RadioButton labelAlignCenter;
        private System.Windows.Forms.RadioButton labelAlignTop;
        private PictureButton btnEditIcon;
        private PictureButton btnRemoveIcon;
        private PictureButton btnClearLabelText;
        private System.Windows.Forms.GroupBox groupAppearance;
        private System.Windows.Forms.ComboBox fonts;
        private ButtonPrimary btnForeColor;
        private HorizontalTabControl horizontalTabControl1;
        private System.Windows.Forms.TabPage tabOnPress;
        private System.Windows.Forms.Label lblCurrentState;
        private System.Windows.Forms.Label lblCurrentStateLabel;
        private ButtonPrimary btnOk;
        private System.Windows.Forms.TabPage tabOnEvent;
        private PictureButton btnAddVariable;
        protected System.Windows.Forms.ContextMenuStrip variablesContextMenu;
    }
}