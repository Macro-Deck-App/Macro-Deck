using System.ComponentModel;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;

namespace SuchByte.MacroDeck.GUI
{
    partial class ButtonEditor
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
            this.radioButtonOff = new SuchByte.MacroDeck.GUI.CustomControls.ButtonRadioButton();
            this.radioButtonOn = new SuchByte.MacroDeck.GUI.CustomControls.ButtonRadioButton();
            this.panel1 = new System.Windows.Forms.Panel();
            this.panel2 = new System.Windows.Forms.Panel();
            this.label2 = new System.Windows.Forms.Label();
            this.labelAlignBottom = new SuchByte.MacroDeck.GUI.CustomControls.ButtonRadioButton();
            this.labelAlignCenter = new SuchByte.MacroDeck.GUI.CustomControls.ButtonRadioButton();
            this.labelAlignTop = new SuchByte.MacroDeck.GUI.CustomControls.ButtonRadioButton();
            this.btnEditIcon = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.btnRemoveIcon = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.btnClearLabelText = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.btnBackColor = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
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
            this.radioOnPress = new SuchByte.MacroDeck.GUI.CustomControls.ButtonRadioButton();
            this.radioOnRelease = new SuchByte.MacroDeck.GUI.CustomControls.ButtonRadioButton();
            this.radioOnLongPress = new SuchByte.MacroDeck.GUI.CustomControls.ButtonRadioButton();
            this.radioOnLongPressRelease = new SuchByte.MacroDeck.GUI.CustomControls.ButtonRadioButton();
            this.radioOnEvent = new SuchByte.MacroDeck.GUI.CustomControls.ButtonRadioButton();
            this.btnRemoveHotkey = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.hotkey = new SuchByte.MacroDeck.GUI.CustomControls.RoundedTextBox();
            this.btnEditJson = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.label1 = new System.Windows.Forms.Label();
            this.buttonGUIDLabel = new System.Windows.Forms.TextBox();
            this.lblAppearance = new System.Windows.Forms.Label();
            this.panel3 = new System.Windows.Forms.Panel();
            this.lblState = new System.Windows.Forms.Label();
            this.lblKeyBinding = new System.Windows.Forms.Label();
            this.lblActions = new System.Windows.Forms.Label();
            this.panel4 = new System.Windows.Forms.Panel();
            ((System.ComponentModel.ISupportInitialize)(this.btnPreview)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.fontSize)).BeginInit();
            this.panel1.SuspendLayout();
            this.panel2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.btnEditIcon)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnRemoveIcon)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnClearLabelText)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnOpenTemplateEditor)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnAddVariable)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnDeleteStateBinding)).BeginInit();
            this.flowLayoutPanel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.btnRemoveHotkey)).BeginInit();
            this.panel3.SuspendLayout();
            this.SuspendLayout();
            // 
            // btnApply
            // 
            this.btnApply.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnApply.BorderRadius = 8;
            this.btnApply.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnApply.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnApply.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnApply.ForeColor = System.Drawing.Color.White;
            this.btnApply.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnApply.Icon = null;
            this.btnApply.Location = new System.Drawing.Point(1104, 599);
            this.btnApply.Margin = new System.Windows.Forms.Padding(5);
            this.btnApply.Name = "btnApply";
            this.btnApply.Progress = 0;
            this.btnApply.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnApply.Size = new System.Drawing.Size(88, 30);
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
            this.btnPreview.Location = new System.Drawing.Point(6, 86);
            this.btnPreview.Margin = new System.Windows.Forms.Padding(5);
            this.btnPreview.Name = "btnPreview";
            this.btnPreview.Radius = 40;
            this.btnPreview.Row = 0;
            this.btnPreview.ShowGIFIndicator = false;
            this.btnPreview.ShowKeyboardHotkeyIndicator = false;
            this.btnPreview.Size = new System.Drawing.Size(120, 120);
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
            this.labelText.Location = new System.Drawing.Point(5, 216);
            this.labelText.Margin = new System.Windows.Forms.Padding(5);
            this.labelText.MaxCharacters = 32767;
            this.labelText.Multiline = true;
            this.labelText.Name = "labelText";
            this.labelText.Padding = new System.Windows.Forms.Padding(10, 7, 10, 7);
            this.labelText.PasswordChar = false;
            this.labelText.PlaceHolderColor = System.Drawing.Color.Gray;
            this.labelText.PlaceHolderText = "";
            this.labelText.ReadOnly = false;
            this.labelText.ScrollBars = System.Windows.Forms.ScrollBars.None;
            this.labelText.SelectionStart = 0;
            this.labelText.Size = new System.Drawing.Size(248, 98);
            this.labelText.TabIndex = 23;
            this.labelText.TabStop = false;
            this.labelText.TextAlignment = System.Windows.Forms.HorizontalAlignment.Center;
            this.labelText.TextChanged += new System.EventHandler(this.LabelChanged);
            // 
            // fontSize
            // 
            this.fontSize.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.fontSize.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.fontSize.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.fontSize.ForeColor = System.Drawing.Color.White;
            this.fontSize.Location = new System.Drawing.Point(204, 364);
            this.fontSize.Margin = new System.Windows.Forms.Padding(5);
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
            this.fontSize.Size = new System.Drawing.Size(55, 26);
            this.fontSize.TabIndex = 10;
            this.fontSize.TabStop = false;
            this.fontSize.Value = new decimal(new int[] {
            6,
            0,
            0,
            0});
            this.fontSize.ValueChanged += new System.EventHandler(this.LabelChanged);
            // 
            // lblButtonState
            // 
            this.lblButtonState.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblButtonState.Location = new System.Drawing.Point(1, 14);
            this.lblButtonState.Margin = new System.Windows.Forms.Padding(5, 0, 5, 0);
            this.lblButtonState.Name = "lblButtonState";
            this.lblButtonState.Size = new System.Drawing.Size(157, 28);
            this.lblButtonState.TabIndex = 12;
            this.lblButtonState.Text = "Button state";
            this.lblButtonState.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblButtonState.UseMnemonic = false;
            // 
            // radioButtonOff
            // 
            this.radioButtonOff.BorderRadius = 8;
            this.radioButtonOff.Checked = true;
            this.radioButtonOff.Cursor = System.Windows.Forms.Cursors.Hand;
            this.radioButtonOff.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioButtonOff.Icon = null;
            this.radioButtonOff.IconAlignment = System.Drawing.ContentAlignment.MiddleLeft;
            this.radioButtonOff.Location = new System.Drawing.Point(159, 15);
            this.radioButtonOff.Margin = new System.Windows.Forms.Padding(5);
            this.radioButtonOff.Name = "radioButtonOff";
            this.radioButtonOff.Size = new System.Drawing.Size(62, 28);
            this.radioButtonOff.TabIndex = 13;
            this.radioButtonOff.TabStop = true;
            this.radioButtonOff.Text = "Off";
            this.radioButtonOff.UseMnemonic = false;
            this.radioButtonOff.UseVisualStyleBackColor = true;
            this.radioButtonOff.CheckedChanged += new System.EventHandler(this.RadioButton_CheckedChanged);
            // 
            // radioButtonOn
            // 
            this.radioButtonOn.BorderRadius = 8;
            this.radioButtonOn.Cursor = System.Windows.Forms.Cursors.Hand;
            this.radioButtonOn.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioButtonOn.Icon = null;
            this.radioButtonOn.IconAlignment = System.Drawing.ContentAlignment.MiddleLeft;
            this.radioButtonOn.Location = new System.Drawing.Point(224, 14);
            this.radioButtonOn.Margin = new System.Windows.Forms.Padding(5);
            this.radioButtonOn.Name = "radioButtonOn";
            this.radioButtonOn.Size = new System.Drawing.Size(62, 28);
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
            this.panel1.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panel1.Location = new System.Drawing.Point(0, 28);
            this.panel1.Margin = new System.Windows.Forms.Padding(5);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(286, 42);
            this.panel1.TabIndex = 15;
            // 
            // panel2
            // 
            this.panel2.Controls.Add(this.label2);
            this.panel2.Controls.Add(this.labelAlignBottom);
            this.panel2.Controls.Add(this.labelAlignCenter);
            this.panel2.Controls.Add(this.labelAlignTop);
            this.panel2.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.panel2.Location = new System.Drawing.Point(6, 324);
            this.panel2.Margin = new System.Windows.Forms.Padding(5);
            this.panel2.Name = "panel2";
            this.panel2.Size = new System.Drawing.Size(285, 28);
            this.panel2.TabIndex = 16;
            // 
            // label2
            // 
            this.label2.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.label2.Location = new System.Drawing.Point(0, 0);
            this.label2.Margin = new System.Windows.Forms.Padding(5, 0, 5, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(174, 27);
            this.label2.TabIndex = 29;
            this.label2.Text = "Align label:";
            this.label2.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.label2.UseMnemonic = false;
            // 
            // labelAlignBottom
            // 
            this.labelAlignBottom.BorderRadius = 8;
            this.labelAlignBottom.Checked = true;
            this.labelAlignBottom.Cursor = System.Windows.Forms.Cursors.Hand;
            this.labelAlignBottom.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.labelAlignBottom.Icon = global::SuchByte.MacroDeck.Properties.Resources.AlignBottom;
            this.labelAlignBottom.IconAlignment = System.Drawing.ContentAlignment.MiddleCenter;
            this.labelAlignBottom.Location = new System.Drawing.Point(183, 0);
            this.labelAlignBottom.Margin = new System.Windows.Forms.Padding(5);
            this.labelAlignBottom.Name = "labelAlignBottom";
            this.labelAlignBottom.Size = new System.Drawing.Size(27, 27);
            this.labelAlignBottom.TabIndex = 11;
            this.labelAlignBottom.TabStop = true;
            this.labelAlignBottom.UseMnemonic = false;
            this.labelAlignBottom.UseVisualStyleBackColor = true;
            this.labelAlignBottom.CheckedChanged += new System.EventHandler(this.LabelChanged);
            // 
            // labelAlignCenter
            // 
            this.labelAlignCenter.BorderRadius = 8;
            this.labelAlignCenter.Cursor = System.Windows.Forms.Cursors.Hand;
            this.labelAlignCenter.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.labelAlignCenter.Icon = global::SuchByte.MacroDeck.Properties.Resources.AlignCenter;
            this.labelAlignCenter.IconAlignment = System.Drawing.ContentAlignment.MiddleCenter;
            this.labelAlignCenter.Location = new System.Drawing.Point(218, 0);
            this.labelAlignCenter.Margin = new System.Windows.Forms.Padding(5);
            this.labelAlignCenter.Name = "labelAlignCenter";
            this.labelAlignCenter.Size = new System.Drawing.Size(27, 27);
            this.labelAlignCenter.TabIndex = 10;
            this.labelAlignCenter.UseMnemonic = false;
            this.labelAlignCenter.UseVisualStyleBackColor = true;
            this.labelAlignCenter.CheckedChanged += new System.EventHandler(this.LabelChanged);
            // 
            // labelAlignTop
            // 
            this.labelAlignTop.BorderRadius = 8;
            this.labelAlignTop.Cursor = System.Windows.Forms.Cursors.Hand;
            this.labelAlignTop.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.labelAlignTop.Icon = global::SuchByte.MacroDeck.Properties.Resources.AlignTop;
            this.labelAlignTop.IconAlignment = System.Drawing.ContentAlignment.MiddleCenter;
            this.labelAlignTop.Location = new System.Drawing.Point(255, 0);
            this.labelAlignTop.Margin = new System.Windows.Forms.Padding(5);
            this.labelAlignTop.Name = "labelAlignTop";
            this.labelAlignTop.Size = new System.Drawing.Size(27, 27);
            this.labelAlignTop.TabIndex = 9;
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
            this.btnEditIcon.Location = new System.Drawing.Point(136, 102);
            this.btnEditIcon.Margin = new System.Windows.Forms.Padding(5);
            this.btnEditIcon.Name = "btnEditIcon";
            this.btnEditIcon.Size = new System.Drawing.Size(27, 27);
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
            this.btnRemoveIcon.Location = new System.Drawing.Point(136, 139);
            this.btnRemoveIcon.Margin = new System.Windows.Forms.Padding(5);
            this.btnRemoveIcon.Name = "btnRemoveIcon";
            this.btnRemoveIcon.Size = new System.Drawing.Size(27, 27);
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
            this.btnClearLabelText.Location = new System.Drawing.Point(264, 250);
            this.btnClearLabelText.Margin = new System.Windows.Forms.Padding(5);
            this.btnClearLabelText.Name = "btnClearLabelText";
            this.btnClearLabelText.Size = new System.Drawing.Size(27, 27);
            this.btnClearLabelText.TabIndex = 19;
            this.btnClearLabelText.TabStop = false;
            this.btnClearLabelText.Click += new System.EventHandler(this.BtnClearLabelText_Click);
            // 
            // btnBackColor
            // 
            this.btnBackColor.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnBackColor.BorderRadius = 8;
            this.btnBackColor.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnBackColor.FlatAppearance.BorderSize = 0;
            this.btnBackColor.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnBackColor.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnBackColor.ForeColor = System.Drawing.Color.White;
            this.btnBackColor.HoverColor = System.Drawing.Color.Transparent;
            this.btnBackColor.Icon = global::SuchByte.MacroDeck.Properties.Resources.Palette;
            this.btnBackColor.Location = new System.Drawing.Point(136, 176);
            this.btnBackColor.Margin = new System.Windows.Forms.Padding(5);
            this.btnBackColor.Name = "btnBackColor";
            this.btnBackColor.Progress = 0;
            this.btnBackColor.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnBackColor.Size = new System.Drawing.Size(27, 27);
            this.btnBackColor.TabIndex = 25;
            this.btnBackColor.UseMnemonic = false;
            this.btnBackColor.UseVisualStyleBackColor = false;
            this.btnBackColor.UseWindowsAccentColor = false;
            this.btnBackColor.Click += new System.EventHandler(this.BtnBackColor_Click);
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
            this.btnOpenTemplateEditor.Location = new System.Drawing.Point(263, 216);
            this.btnOpenTemplateEditor.Margin = new System.Windows.Forms.Padding(5);
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
            this.btnAddVariable.Location = new System.Drawing.Point(264, 283);
            this.btnAddVariable.Margin = new System.Windows.Forms.Padding(5);
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
            this.btnForeColor.Location = new System.Drawing.Point(265, 364);
            this.btnForeColor.Margin = new System.Windows.Forms.Padding(5);
            this.btnForeColor.Name = "btnForeColor";
            this.btnForeColor.Progress = 0;
            this.btnForeColor.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnForeColor.Size = new System.Drawing.Size(26, 26);
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
            this.fonts.Location = new System.Drawing.Point(6, 362);
            this.fonts.Margin = new System.Windows.Forms.Padding(5, 6, 5, 6);
            this.fonts.Name = "fonts";
            this.fonts.Padding = new System.Windows.Forms.Padding(10, 2, 10, 2);
            this.fonts.SelectedIndex = -1;
            this.fonts.SelectedItem = null;
            this.fonts.Size = new System.Drawing.Size(191, 28);
            this.fonts.TabIndex = 20;
            this.fonts.SelectedIndexChanged += new System.EventHandler(this.LabelChanged);
            // 
            // lblCurrentState
            // 
            this.lblCurrentState.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblCurrentState.Location = new System.Drawing.Point(220, 441);
            this.lblCurrentState.Margin = new System.Windows.Forms.Padding(5, 0, 5, 0);
            this.lblCurrentState.Name = "lblCurrentState";
            this.lblCurrentState.Size = new System.Drawing.Size(70, 28);
            this.lblCurrentState.TabIndex = 22;
            this.lblCurrentState.Text = "Off";
            this.lblCurrentState.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblCurrentState.UseMnemonic = false;
            // 
            // lblCurrentStateLabel
            // 
            this.lblCurrentStateLabel.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblCurrentStateLabel.Location = new System.Drawing.Point(6, 441);
            this.lblCurrentStateLabel.Margin = new System.Windows.Forms.Padding(5, 0, 5, 0);
            this.lblCurrentStateLabel.Name = "lblCurrentStateLabel";
            this.lblCurrentStateLabel.Size = new System.Drawing.Size(204, 28);
            this.lblCurrentStateLabel.TabIndex = 23;
            this.lblCurrentStateLabel.Text = "Current state:";
            this.lblCurrentStateLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblCurrentStateLabel.UseMnemonic = false;
            // 
            // btnOk
            // 
            this.btnOk.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOk.BorderRadius = 8;
            this.btnOk.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnOk.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOk.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnOk.ForeColor = System.Drawing.Color.White;
            this.btnOk.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnOk.Icon = null;
            this.btnOk.Location = new System.Drawing.Point(1007, 599);
            this.btnOk.Margin = new System.Windows.Forms.Padding(5);
            this.btnOk.Name = "btnOk";
            this.btnOk.Progress = 0;
            this.btnOk.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnOk.Size = new System.Drawing.Size(88, 30);
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
            this.lblStateBinding.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblStateBinding.Location = new System.Drawing.Point(6, 469);
            this.lblStateBinding.Margin = new System.Windows.Forms.Padding(5, 0, 5, 0);
            this.lblStateBinding.Name = "lblStateBinding";
            this.lblStateBinding.Size = new System.Drawing.Size(282, 17);
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
            this.listStateBinding.Location = new System.Drawing.Point(6, 491);
            this.listStateBinding.Margin = new System.Windows.Forms.Padding(5);
            this.listStateBinding.Name = "listStateBinding";
            this.listStateBinding.Padding = new System.Windows.Forms.Padding(10, 2, 10, 2);
            this.listStateBinding.SelectedIndex = -1;
            this.listStateBinding.SelectedItem = null;
            this.listStateBinding.Size = new System.Drawing.Size(253, 26);
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
            this.btnDeleteStateBinding.Location = new System.Drawing.Point(264, 491);
            this.btnDeleteStateBinding.Margin = new System.Windows.Forms.Padding(5);
            this.btnDeleteStateBinding.Name = "btnDeleteStateBinding";
            this.btnDeleteStateBinding.Size = new System.Drawing.Size(27, 27);
            this.btnDeleteStateBinding.TabIndex = 28;
            this.btnDeleteStateBinding.TabStop = false;
            this.btnDeleteStateBinding.Click += new System.EventHandler(this.BtnDeleteStateBinding_Click);
            // 
            // selectorPanel
            // 
            this.selectorPanel.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.selectorPanel.Location = new System.Drawing.Point(310, 81);
            this.selectorPanel.Margin = new System.Windows.Forms.Padding(5);
            this.selectorPanel.Name = "selectorPanel";
            this.selectorPanel.Size = new System.Drawing.Size(882, 508);
            this.selectorPanel.TabIndex = 29;
            // 
            // flowLayoutPanel1
            // 
            this.flowLayoutPanel1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.flowLayoutPanel1.AutoScroll = true;
            this.flowLayoutPanel1.AutoScrollMargin = new System.Drawing.Size(0, 30);
            this.flowLayoutPanel1.Controls.Add(this.radioOnPress);
            this.flowLayoutPanel1.Controls.Add(this.radioOnRelease);
            this.flowLayoutPanel1.Controls.Add(this.radioOnLongPress);
            this.flowLayoutPanel1.Controls.Add(this.radioOnLongPressRelease);
            this.flowLayoutPanel1.Controls.Add(this.radioOnEvent);
            this.flowLayoutPanel1.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.flowLayoutPanel1.Location = new System.Drawing.Point(309, 49);
            this.flowLayoutPanel1.Margin = new System.Windows.Forms.Padding(0);
            this.flowLayoutPanel1.Name = "flowLayoutPanel1";
            this.flowLayoutPanel1.Size = new System.Drawing.Size(877, 27);
            this.flowLayoutPanel1.TabIndex = 30;
            // 
            // radioOnPress
            // 
            this.radioOnPress.AutoSize = true;
            this.radioOnPress.BorderRadius = 8;
            this.radioOnPress.Checked = true;
            this.radioOnPress.Cursor = System.Windows.Forms.Cursors.Hand;
            this.radioOnPress.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioOnPress.Icon = null;
            this.radioOnPress.IconAlignment = System.Drawing.ContentAlignment.MiddleLeft;
            this.radioOnPress.Location = new System.Drawing.Point(0, 0);
            this.radioOnPress.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
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
            this.radioOnRelease.BorderRadius = 8;
            this.radioOnRelease.Cursor = System.Windows.Forms.Cursors.Hand;
            this.radioOnRelease.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioOnRelease.Icon = null;
            this.radioOnRelease.IconAlignment = System.Drawing.ContentAlignment.MiddleLeft;
            this.radioOnRelease.Location = new System.Drawing.Point(91, 0);
            this.radioOnRelease.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
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
            this.radioOnLongPress.BorderRadius = 8;
            this.radioOnLongPress.Cursor = System.Windows.Forms.Cursors.Hand;
            this.radioOnLongPress.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioOnLongPress.Icon = null;
            this.radioOnLongPress.IconAlignment = System.Drawing.ContentAlignment.MiddleLeft;
            this.radioOnLongPress.Location = new System.Drawing.Point(193, 0);
            this.radioOnLongPress.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
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
            this.radioOnLongPressRelease.BorderRadius = 8;
            this.radioOnLongPressRelease.Cursor = System.Windows.Forms.Cursors.Hand;
            this.radioOnLongPressRelease.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioOnLongPressRelease.Icon = null;
            this.radioOnLongPressRelease.IconAlignment = System.Drawing.ContentAlignment.MiddleLeft;
            this.radioOnLongPressRelease.Location = new System.Drawing.Point(315, 0);
            this.radioOnLongPressRelease.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
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
            this.radioOnEvent.BorderRadius = 8;
            this.radioOnEvent.Cursor = System.Windows.Forms.Cursors.Hand;
            this.radioOnEvent.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.radioOnEvent.Icon = null;
            this.radioOnEvent.IconAlignment = System.Drawing.ContentAlignment.MiddleLeft;
            this.radioOnEvent.Location = new System.Drawing.Point(488, 0);
            this.radioOnEvent.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
            this.radioOnEvent.Name = "radioOnEvent";
            this.radioOnEvent.Size = new System.Drawing.Size(87, 22);
            this.radioOnEvent.TabIndex = 1;
            this.radioOnEvent.Text = "On event";
            this.radioOnEvent.UseMnemonic = false;
            this.radioOnEvent.UseVisualStyleBackColor = true;
            this.radioOnEvent.CheckedChanged += new System.EventHandler(this.RadioOnEvent_CheckedChanged);
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
            this.btnRemoveHotkey.Location = new System.Drawing.Point(264, 577);
            this.btnRemoveHotkey.Margin = new System.Windows.Forms.Padding(5);
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
            this.hotkey.Location = new System.Drawing.Point(6, 575);
            this.hotkey.Margin = new System.Windows.Forms.Padding(5);
            this.hotkey.MaxCharacters = 32767;
            this.hotkey.Multiline = false;
            this.hotkey.Name = "hotkey";
            this.hotkey.Padding = new System.Windows.Forms.Padding(35, 7, 10, 7);
            this.hotkey.PasswordChar = false;
            this.hotkey.PlaceHolderColor = System.Drawing.Color.Gray;
            this.hotkey.PlaceHolderText = "";
            this.hotkey.ReadOnly = false;
            this.hotkey.ScrollBars = System.Windows.Forms.ScrollBars.None;
            this.hotkey.SelectionStart = 0;
            this.hotkey.Size = new System.Drawing.Size(253, 29);
            this.hotkey.TabIndex = 0;
            this.hotkey.TabStop = false;
            this.hotkey.TextAlignment = System.Windows.Forms.HorizontalAlignment.Left;
            // 
            // btnEditJson
            // 
            this.btnEditJson.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnEditJson.BorderRadius = 8;
            this.btnEditJson.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnEditJson.FlatAppearance.BorderSize = 0;
            this.btnEditJson.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnEditJson.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnEditJson.ForeColor = System.Drawing.Color.White;
            this.btnEditJson.HoverColor = System.Drawing.Color.Empty;
            this.btnEditJson.Icon = null;
            this.btnEditJson.Location = new System.Drawing.Point(310, 599);
            this.btnEditJson.Margin = new System.Windows.Forms.Padding(5);
            this.btnEditJson.Name = "btnEditJson";
            this.btnEditJson.Progress = 0;
            this.btnEditJson.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(103)))), ((int)(((byte)(225)))));
            this.btnEditJson.Size = new System.Drawing.Size(116, 30);
            this.btnEditJson.TabIndex = 33;
            this.btnEditJson.Text = "Edit JSON";
            this.btnEditJson.UseMnemonic = false;
            this.btnEditJson.UseVisualStyleBackColor = true;
            this.btnEditJson.UseWindowsAccentColor = true;
            this.btnEditJson.Click += new System.EventHandler(this.BtnEditJson_Click);
            // 
            // label1
            // 
            this.label1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.label1.Location = new System.Drawing.Point(447, 599);
            this.label1.Margin = new System.Windows.Forms.Padding(5, 0, 5, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(58, 30);
            this.label1.TabIndex = 34;
            this.label1.Text = "GUID:";
            this.label1.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // buttonGUIDLabel
            // 
            this.buttonGUIDLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.buttonGUIDLabel.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.buttonGUIDLabel.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.buttonGUIDLabel.ForeColor = System.Drawing.Color.White;
            this.buttonGUIDLabel.Location = new System.Drawing.Point(515, 605);
            this.buttonGUIDLabel.Margin = new System.Windows.Forms.Padding(5);
            this.buttonGUIDLabel.Name = "buttonGUIDLabel";
            this.buttonGUIDLabel.ReadOnly = true;
            this.buttonGUIDLabel.Size = new System.Drawing.Size(400, 16);
            this.buttonGUIDLabel.TabIndex = 35;
            // 
            // lblAppearance
            // 
            this.lblAppearance.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.lblAppearance.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblAppearance.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.lblAppearance.Location = new System.Drawing.Point(0, 0);
            this.lblAppearance.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblAppearance.Name = "lblAppearance";
            this.lblAppearance.Size = new System.Drawing.Size(286, 35);
            this.lblAppearance.TabIndex = 36;
            this.lblAppearance.Text = "Appearance";
            this.lblAppearance.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // panel3
            // 
            this.panel3.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.panel3.Controls.Add(this.lblAppearance);
            this.panel3.Controls.Add(this.panel1);
            this.panel3.Location = new System.Drawing.Point(5, 5);
            this.panel3.Margin = new System.Windows.Forms.Padding(4);
            this.panel3.Name = "panel3";
            this.panel3.Size = new System.Drawing.Size(286, 70);
            this.panel3.TabIndex = 37;
            // 
            // lblState
            // 
            this.lblState.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.lblState.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.lblState.Location = new System.Drawing.Point(5, 406);
            this.lblState.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblState.Name = "lblState";
            this.lblState.Size = new System.Drawing.Size(286, 35);
            this.lblState.TabIndex = 37;
            this.lblState.Text = "Button state";
            this.lblState.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblState.Click += new System.EventHandler(this.label4_Click);
            // 
            // lblKeyBinding
            // 
            this.lblKeyBinding.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.lblKeyBinding.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.lblKeyBinding.Location = new System.Drawing.Point(6, 535);
            this.lblKeyBinding.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblKeyBinding.Name = "lblKeyBinding";
            this.lblKeyBinding.Size = new System.Drawing.Size(285, 35);
            this.lblKeyBinding.TabIndex = 38;
            this.lblKeyBinding.Text = "Key binding";
            this.lblKeyBinding.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblActions
            // 
            this.lblActions.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.lblActions.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.lblActions.Location = new System.Drawing.Point(309, 5);
            this.lblActions.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblActions.Name = "lblActions";
            this.lblActions.Size = new System.Drawing.Size(350, 35);
            this.lblActions.TabIndex = 39;
            this.lblActions.Text = "Actions";
            this.lblActions.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // panel4
            // 
            this.panel4.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.panel4.BackColor = System.Drawing.Color.Silver;
            this.panel4.Location = new System.Drawing.Point(300, 2);
            this.panel4.Margin = new System.Windows.Forms.Padding(4);
            this.panel4.Name = "panel4";
            this.panel4.Size = new System.Drawing.Size(1, 630);
            this.panel4.TabIndex = 40;
            // 
            // ButtonEditor
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.ClientSize = new System.Drawing.Size(1200, 635);
            this.Controls.Add(this.panel4);
            this.Controls.Add(this.lblActions);
            this.Controls.Add(this.lblKeyBinding);
            this.Controls.Add(this.btnRemoveHotkey);
            this.Controls.Add(this.lblStateBinding);
            this.Controls.Add(this.hotkey);
            this.Controls.Add(this.lblState);
            this.Controls.Add(this.listStateBinding);
            this.Controls.Add(this.btnDeleteStateBinding);
            this.Controls.Add(this.panel3);
            this.Controls.Add(this.btnBackColor);
            this.Controls.Add(this.lblCurrentState);
            this.Controls.Add(this.btnOpenTemplateEditor);
            this.Controls.Add(this.lblCurrentStateLabel);
            this.Controls.Add(this.buttonGUIDLabel);
            this.Controls.Add(this.btnAddVariable);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.btnForeColor);
            this.Controls.Add(this.btnEditJson);
            this.Controls.Add(this.fonts);
            this.Controls.Add(this.btnClearLabelText);
            this.Controls.Add(this.panel2);
            this.Controls.Add(this.flowLayoutPanel1);
            this.Controls.Add(this.labelText);
            this.Controls.Add(this.selectorPanel);
            this.Controls.Add(this.btnEditIcon);
            this.Controls.Add(this.btnOk);
            this.Controls.Add(this.btnRemoveIcon);
            this.Controls.Add(this.btnApply);
            this.Controls.Add(this.fontSize);
            this.Controls.Add(this.btnPreview);
            this.Location = new System.Drawing.Point(0, 0);
            this.Margin = new System.Windows.Forms.Padding(5, 6, 5, 6);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ButtonEditor";
            this.Text = "Macro Deck :: Edit button";
            this.Shown += new System.EventHandler(this.ButtonEditor_Shown);
            this.Controls.SetChildIndex(this.btnPreview, 0);
            this.Controls.SetChildIndex(this.fontSize, 0);
            this.Controls.SetChildIndex(this.btnApply, 0);
            this.Controls.SetChildIndex(this.btnRemoveIcon, 0);
            this.Controls.SetChildIndex(this.btnOk, 0);
            this.Controls.SetChildIndex(this.btnEditIcon, 0);
            this.Controls.SetChildIndex(this.selectorPanel, 0);
            this.Controls.SetChildIndex(this.labelText, 0);
            this.Controls.SetChildIndex(this.flowLayoutPanel1, 0);
            this.Controls.SetChildIndex(this.panel2, 0);
            this.Controls.SetChildIndex(this.btnClearLabelText, 0);
            this.Controls.SetChildIndex(this.fonts, 0);
            this.Controls.SetChildIndex(this.btnEditJson, 0);
            this.Controls.SetChildIndex(this.btnForeColor, 0);
            this.Controls.SetChildIndex(this.label1, 0);
            this.Controls.SetChildIndex(this.btnAddVariable, 0);
            this.Controls.SetChildIndex(this.buttonGUIDLabel, 0);
            this.Controls.SetChildIndex(this.lblCurrentStateLabel, 0);
            this.Controls.SetChildIndex(this.btnOpenTemplateEditor, 0);
            this.Controls.SetChildIndex(this.lblCurrentState, 0);
            this.Controls.SetChildIndex(this.btnBackColor, 0);
            this.Controls.SetChildIndex(this.panel3, 0);
            this.Controls.SetChildIndex(this.btnDeleteStateBinding, 0);
            this.Controls.SetChildIndex(this.listStateBinding, 0);
            this.Controls.SetChildIndex(this.lblState, 0);
            this.Controls.SetChildIndex(this.hotkey, 0);
            this.Controls.SetChildIndex(this.lblStateBinding, 0);
            this.Controls.SetChildIndex(this.btnRemoveHotkey, 0);
            this.Controls.SetChildIndex(this.lblKeyBinding, 0);
            this.Controls.SetChildIndex(this.lblActions, 0);
            this.Controls.SetChildIndex(this.panel4, 0);
            ((System.ComponentModel.ISupportInitialize)(this.btnPreview)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.fontSize)).EndInit();
            this.panel1.ResumeLayout(false);
            this.panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.btnEditIcon)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnRemoveIcon)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnClearLabelText)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnOpenTemplateEditor)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnAddVariable)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnDeleteStateBinding)).EndInit();
            this.flowLayoutPanel1.ResumeLayout(false);
            this.flowLayoutPanel1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.btnRemoveHotkey)).EndInit();
            this.panel3.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }


        #endregion
        private ButtonPrimary btnApply;
        private RoundedButton btnPreview;
        private RoundedTextBox labelText;
        private NumericUpDown fontSize;
        private Label lblButtonState;
        private ButtonRadioButton radioButtonOff;
        private ButtonRadioButton radioButtonOn;
        private Panel panel1;
        private Panel panel2;
        private ButtonRadioButton labelAlignBottom;
        private ButtonRadioButton labelAlignCenter;
        private ButtonRadioButton labelAlignTop;
        private PictureButton btnEditIcon;
        private PictureButton btnRemoveIcon;
        private PictureButton btnClearLabelText;
        private RoundedComboBox fonts;
        private ButtonPrimary btnForeColor;
        private Label lblCurrentState;
        private Label lblCurrentStateLabel;
        private ButtonPrimary btnOk;
        private PictureButton btnAddVariable;
        protected ContextMenuStrip variablesContextMenu;
        private Label lblStateBinding;
        private RoundedComboBox listStateBinding;
        private PictureButton btnDeleteStateBinding;
        private Panel selectorPanel;
        private FlowLayoutPanel flowLayoutPanel1;
        private ButtonRadioButton radioOnPress;
        private ButtonRadioButton radioOnEvent;
        private PictureButton btnOpenTemplateEditor;
        private PictureButton btnRemoveHotkey;
        private RoundedTextBox hotkey;
        private ButtonRadioButton radioOnRelease;
        private ButtonRadioButton radioOnLongPress;
        private ButtonRadioButton radioOnLongPressRelease;
        private ButtonPrimary btnEditJson;
        private Label label1;
        private TextBox buttonGUIDLabel;
        private Label label2;
        private ButtonPrimary btnBackColor;
        private Label lblAppearance;
        private Panel panel3;
        private Label lblState;
        private Label lblKeyBinding;
        private Label lblActions;
        private Panel panel4;
    }
}