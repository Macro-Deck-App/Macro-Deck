using System.ComponentModel;
using SuchByte.MacroDeck.GUI.CustomControls;

namespace SuchByte.MacroDeck.GUI.Dialogs
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
            components = new Container();
            btnApply = new ButtonPrimary();
            btnPreview = new RoundedButton();
            labelText = new RoundedTextBox();
            fontSize = new NumericUpDown();
            lblButtonState = new Label();
            radioButtonOff = new ButtonRadioButton();
            radioButtonOn = new ButtonRadioButton();
            panel1 = new Panel();
            panel2 = new Panel();
            label2 = new Label();
            labelAlignBottom = new ButtonRadioButton();
            labelAlignCenter = new ButtonRadioButton();
            labelAlignTop = new ButtonRadioButton();
            btnEditIcon = new PictureButton();
            btnRemoveIcon = new PictureButton();
            btnClearLabelText = new PictureButton();
            btnBackColor = new ButtonPrimary();
            btnOpenTemplateEditor = new PictureButton();
            btnAddVariable = new PictureButton();
            btnForeColor = new ButtonPrimary();
            fonts = new RoundedComboBox();
            lblCurrentState = new Label();
            lblCurrentStateLabel = new Label();
            btnOk = new ButtonPrimary();
            variablesContextMenu = new ContextMenuStrip(components);
            lblStateBinding = new Label();
            listStateBinding = new RoundedComboBox();
            btnDeleteStateBinding = new PictureButton();
            selectorPanel = new Panel();
            flowLayoutPanel1 = new FlowLayoutPanel();
            radioOnPress = new ButtonRadioButton();
            radioOnRelease = new ButtonRadioButton();
            radioOnLongPress = new ButtonRadioButton();
            radioOnLongPressRelease = new ButtonRadioButton();
            radioOnEvent = new ButtonRadioButton();
            btnRemoveHotkey = new PictureButton();
            hotkey = new RoundedTextBox();
            btnEditJson = new ButtonPrimary();
            label1 = new Label();
            buttonGUIDLabel = new TextBox();
            lblAppearance = new Label();
            panel3 = new Panel();
            lblState = new Label();
            lblKeyBinding = new Label();
            lblActions = new Label();
            panel4 = new Panel();
            ((ISupportInitialize)btnPreview).BeginInit();
            ((ISupportInitialize)fontSize).BeginInit();
            panel1.SuspendLayout();
            panel2.SuspendLayout();
            ((ISupportInitialize)btnEditIcon).BeginInit();
            ((ISupportInitialize)btnRemoveIcon).BeginInit();
            ((ISupportInitialize)btnClearLabelText).BeginInit();
            ((ISupportInitialize)btnOpenTemplateEditor).BeginInit();
            ((ISupportInitialize)btnAddVariable).BeginInit();
            ((ISupportInitialize)btnDeleteStateBinding).BeginInit();
            flowLayoutPanel1.SuspendLayout();
            ((ISupportInitialize)btnRemoveHotkey).BeginInit();
            panel3.SuspendLayout();
            SuspendLayout();
            // 
            // btnApply
            // 
            btnApply.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnApply.BorderRadius = 8;
            btnApply.Cursor = Cursors.Hand;
            btnApply.FlatStyle = FlatStyle.Flat;
            btnApply.Font = new Font("Tahoma", 9.75F);
            btnApply.ForeColor = Color.White;
            btnApply.HoverColor = Color.FromArgb(0, 89, 184);
            btnApply.Icon = null;
            btnApply.Location = new Point(1104, 599);
            btnApply.Margin = new Padding(5);
            btnApply.Name = "btnApply";
            btnApply.Progress = 0;
            btnApply.ProgressColor = Color.FromArgb(0, 46, 94);
            btnApply.Size = new Size(88, 30);
            btnApply.TabIndex = 1;
            btnApply.Text = "Apply";
            btnApply.UseMnemonic = false;
            btnApply.UseVisualStyleBackColor = true;
            btnApply.UseWindowsAccentColor = true;
            btnApply.WriteProgress = true;
            btnApply.Click += BtnSave_Click;
            // 
            // btnPreview
            // 
            btnPreview.BackColor = Color.FromArgb(65, 65, 65);
            btnPreview.BackgroundImageLayout = ImageLayout.Stretch;
            btnPreview.Column = 0;
            btnPreview.Cursor = Cursors.Hand;
            btnPreview.Font = new Font("Tahoma", 15.75F, FontStyle.Bold);
            btnPreview.ForeColor = Color.White;
            btnPreview.ForegroundImage = null;
            btnPreview.Location = new Point(6, 86);
            btnPreview.Margin = new Padding(5);
            btnPreview.Name = "btnPreview";
            btnPreview.Radius = 40;
            btnPreview.Row = 0;
            btnPreview.ShowGIFIndicator = false;
            btnPreview.ShowKeyboardHotkeyIndicator = false;
            btnPreview.Size = new Size(120, 120);
            btnPreview.SizeMode = PictureBoxSizeMode.StretchImage;
            btnPreview.TabIndex = 3;
            btnPreview.TabStop = false;
            btnPreview.Click += BtnPreview_Click;
            // 
            // labelText
            // 
            labelText.AutoScroll = true;
            labelText.BackColor = Color.FromArgb(55, 55, 55);
            labelText.Cursor = Cursors.Hand;
            labelText.Font = new Font("Tahoma", 11.25F);
            labelText.Icon = null;
            labelText.Location = new Point(5, 216);
            labelText.Margin = new Padding(5);
            labelText.MaxCharacters = 32767;
            labelText.Multiline = true;
            labelText.Name = "labelText";
            labelText.Padding = new Padding(10, 7, 10, 7);
            labelText.PasswordChar = false;
            labelText.PlaceHolderColor = Color.Gray;
            labelText.PlaceHolderText = "";
            labelText.ReadOnly = false;
            labelText.ScrollBars = ScrollBars.None;
            labelText.SelectionStart = 0;
            labelText.Size = new Size(248, 98);
            labelText.TabIndex = 23;
            labelText.TabStop = false;
            labelText.TextAlignment = HorizontalAlignment.Center;
            labelText.TextChanged += LabelChanged;
            // 
            // fontSize
            // 
            fontSize.BackColor = Color.FromArgb(55, 55, 55);
            fontSize.BorderStyle = BorderStyle.FixedSingle;
            fontSize.Font = new Font("Tahoma", 11.25F);
            fontSize.ForeColor = Color.White;
            fontSize.Location = new Point(204, 364);
            fontSize.Margin = new Padding(5);
            fontSize.Maximum = new decimal(new int[] { 18, 0, 0, 0 });
            fontSize.Minimum = new decimal(new int[] { 4, 0, 0, 0 });
            fontSize.Name = "fontSize";
            fontSize.Size = new Size(55, 26);
            fontSize.TabIndex = 10;
            fontSize.TabStop = false;
            fontSize.Value = new decimal(new int[] { 6, 0, 0, 0 });
            fontSize.ValueChanged += LabelChanged;
            // 
            // lblButtonState
            // 
            lblButtonState.Font = new Font("Tahoma", 9.75F);
            lblButtonState.Location = new Point(1, 14);
            lblButtonState.Margin = new Padding(5, 0, 5, 0);
            lblButtonState.Name = "lblButtonState";
            lblButtonState.Size = new Size(157, 28);
            lblButtonState.TabIndex = 12;
            lblButtonState.Text = "Button state";
            lblButtonState.TextAlign = ContentAlignment.MiddleLeft;
            lblButtonState.UseMnemonic = false;
            // 
            // radioButtonOff
            // 
            radioButtonOff.BorderRadius = 8;
            radioButtonOff.Checked = true;
            radioButtonOff.Cursor = Cursors.Hand;
            radioButtonOff.Font = new Font("Tahoma", 9.75F);
            radioButtonOff.Icon = null;
            radioButtonOff.IconAlignment = ContentAlignment.MiddleLeft;
            radioButtonOff.Location = new Point(159, 15);
            radioButtonOff.Margin = new Padding(5);
            radioButtonOff.Name = "radioButtonOff";
            radioButtonOff.Size = new Size(62, 28);
            radioButtonOff.TabIndex = 13;
            radioButtonOff.TabStop = true;
            radioButtonOff.Text = "Off";
            radioButtonOff.UseMnemonic = false;
            radioButtonOff.UseVisualStyleBackColor = true;
            radioButtonOff.CheckedChanged += RadioButton_CheckedChanged;
            // 
            // radioButtonOn
            // 
            radioButtonOn.BorderRadius = 8;
            radioButtonOn.Cursor = Cursors.Hand;
            radioButtonOn.Font = new Font("Tahoma", 9.75F);
            radioButtonOn.Icon = null;
            radioButtonOn.IconAlignment = ContentAlignment.MiddleLeft;
            radioButtonOn.Location = new Point(224, 14);
            radioButtonOn.Margin = new Padding(5);
            radioButtonOn.Name = "radioButtonOn";
            radioButtonOn.Size = new Size(62, 28);
            radioButtonOn.TabIndex = 14;
            radioButtonOn.Text = "On";
            radioButtonOn.UseMnemonic = false;
            radioButtonOn.UseVisualStyleBackColor = true;
            radioButtonOn.CheckedChanged += RadioButton_CheckedChanged;
            // 
            // panel1
            // 
            panel1.Controls.Add(lblButtonState);
            panel1.Controls.Add(radioButtonOn);
            panel1.Controls.Add(radioButtonOff);
            panel1.Dock = DockStyle.Bottom;
            panel1.Location = new Point(0, 28);
            panel1.Margin = new Padding(5);
            panel1.Name = "panel1";
            panel1.Size = new Size(286, 42);
            panel1.TabIndex = 15;
            // 
            // panel2
            // 
            panel2.Controls.Add(label2);
            panel2.Controls.Add(labelAlignBottom);
            panel2.Controls.Add(labelAlignCenter);
            panel2.Controls.Add(labelAlignTop);
            panel2.Font = new Font("Tahoma", 9.75F);
            panel2.Location = new Point(6, 324);
            panel2.Margin = new Padding(5);
            panel2.Name = "panel2";
            panel2.Size = new Size(285, 28);
            panel2.TabIndex = 16;
            // 
            // label2
            // 
            label2.Font = new Font("Tahoma", 9.75F);
            label2.Location = new Point(0, 0);
            label2.Margin = new Padding(5, 0, 5, 0);
            label2.Name = "label2";
            label2.Size = new Size(174, 27);
            label2.TabIndex = 29;
            label2.Text = "Align label:";
            label2.TextAlign = ContentAlignment.MiddleLeft;
            label2.UseMnemonic = false;
            // 
            // labelAlignBottom
            // 
            labelAlignBottom.BorderRadius = 8;
            labelAlignBottom.Checked = true;
            labelAlignBottom.Cursor = Cursors.Hand;
            labelAlignBottom.Font = new Font("Tahoma", 11.25F);
            labelAlignBottom.Icon = Properties.Resources.AlignBottom;
            labelAlignBottom.IconAlignment = ContentAlignment.MiddleCenter;
            labelAlignBottom.Location = new Point(183, 0);
            labelAlignBottom.Margin = new Padding(5);
            labelAlignBottom.Name = "labelAlignBottom";
            labelAlignBottom.Size = new Size(27, 27);
            labelAlignBottom.TabIndex = 11;
            labelAlignBottom.TabStop = true;
            labelAlignBottom.UseMnemonic = false;
            labelAlignBottom.UseVisualStyleBackColor = true;
            labelAlignBottom.CheckedChanged += LabelChanged;
            // 
            // labelAlignCenter
            // 
            labelAlignCenter.BorderRadius = 8;
            labelAlignCenter.Cursor = Cursors.Hand;
            labelAlignCenter.Font = new Font("Tahoma", 11.25F);
            labelAlignCenter.Icon = Properties.Resources.AlignCenter;
            labelAlignCenter.IconAlignment = ContentAlignment.MiddleCenter;
            labelAlignCenter.Location = new Point(218, 0);
            labelAlignCenter.Margin = new Padding(5);
            labelAlignCenter.Name = "labelAlignCenter";
            labelAlignCenter.Size = new Size(27, 27);
            labelAlignCenter.TabIndex = 10;
            labelAlignCenter.UseMnemonic = false;
            labelAlignCenter.UseVisualStyleBackColor = true;
            labelAlignCenter.CheckedChanged += LabelChanged;
            // 
            // labelAlignTop
            // 
            labelAlignTop.BorderRadius = 8;
            labelAlignTop.Cursor = Cursors.Hand;
            labelAlignTop.Font = new Font("Tahoma", 11.25F);
            labelAlignTop.Icon = Properties.Resources.AlignTop;
            labelAlignTop.IconAlignment = ContentAlignment.MiddleCenter;
            labelAlignTop.Location = new Point(255, 0);
            labelAlignTop.Margin = new Padding(5);
            labelAlignTop.Name = "labelAlignTop";
            labelAlignTop.Size = new Size(27, 27);
            labelAlignTop.TabIndex = 9;
            labelAlignTop.UseMnemonic = false;
            labelAlignTop.UseVisualStyleBackColor = true;
            labelAlignTop.CheckedChanged += LabelChanged;
            // 
            // btnEditIcon
            // 
            btnEditIcon.BackColor = Color.Transparent;
            btnEditIcon.BackgroundImage = Properties.Resources.Edit_Normal;
            btnEditIcon.BackgroundImageLayout = ImageLayout.Stretch;
            btnEditIcon.Cursor = Cursors.Hand;
            btnEditIcon.Font = new Font("Tahoma", 9.75F);
            btnEditIcon.ForeColor = Color.White;
            btnEditIcon.HoverImage = Properties.Resources.Edit_Hover;
            btnEditIcon.Location = new Point(136, 102);
            btnEditIcon.Margin = new Padding(5);
            btnEditIcon.Name = "btnEditIcon";
            btnEditIcon.Size = new Size(27, 27);
            btnEditIcon.TabIndex = 17;
            btnEditIcon.TabStop = false;
            btnEditIcon.Click += BtnEditIcon_Click;
            // 
            // btnRemoveIcon
            // 
            btnRemoveIcon.BackColor = Color.Transparent;
            btnRemoveIcon.BackgroundImage = Properties.Resources.Delete_Normal;
            btnRemoveIcon.BackgroundImageLayout = ImageLayout.Stretch;
            btnRemoveIcon.Cursor = Cursors.Hand;
            btnRemoveIcon.Font = new Font("Tahoma", 9.75F);
            btnRemoveIcon.ForeColor = Color.White;
            btnRemoveIcon.HoverImage = Properties.Resources.Delete_Hover;
            btnRemoveIcon.Location = new Point(136, 139);
            btnRemoveIcon.Margin = new Padding(5);
            btnRemoveIcon.Name = "btnRemoveIcon";
            btnRemoveIcon.Size = new Size(27, 27);
            btnRemoveIcon.TabIndex = 18;
            btnRemoveIcon.TabStop = false;
            btnRemoveIcon.Click += BtnRemoveIcon_Click;
            // 
            // btnClearLabelText
            // 
            btnClearLabelText.BackColor = Color.Transparent;
            btnClearLabelText.BackgroundImage = Properties.Resources.Delete_Normal;
            btnClearLabelText.BackgroundImageLayout = ImageLayout.Stretch;
            btnClearLabelText.Cursor = Cursors.Hand;
            btnClearLabelText.Font = new Font("Tahoma", 9.75F);
            btnClearLabelText.ForeColor = Color.White;
            btnClearLabelText.HoverImage = Properties.Resources.Delete_Hover;
            btnClearLabelText.Location = new Point(264, 250);
            btnClearLabelText.Margin = new Padding(5);
            btnClearLabelText.Name = "btnClearLabelText";
            btnClearLabelText.Size = new Size(27, 27);
            btnClearLabelText.TabIndex = 19;
            btnClearLabelText.TabStop = false;
            btnClearLabelText.Click += BtnClearLabelText_Click;
            // 
            // btnBackColor
            // 
            btnBackColor.BackgroundImageLayout = ImageLayout.Stretch;
            btnBackColor.BorderRadius = 8;
            btnBackColor.Cursor = Cursors.Hand;
            btnBackColor.FlatAppearance.BorderSize = 0;
            btnBackColor.FlatStyle = FlatStyle.Flat;
            btnBackColor.Font = new Font("Tahoma", 9.75F);
            btnBackColor.ForeColor = Color.White;
            btnBackColor.HoverColor = Color.Transparent;
            btnBackColor.Icon = Properties.Resources.Palette;
            btnBackColor.Location = new Point(136, 176);
            btnBackColor.Margin = new Padding(5);
            btnBackColor.Name = "btnBackColor";
            btnBackColor.Progress = 0;
            btnBackColor.ProgressColor = Color.FromArgb(0, 46, 94);
            btnBackColor.Size = new Size(27, 27);
            btnBackColor.TabIndex = 25;
            btnBackColor.UseMnemonic = false;
            btnBackColor.UseVisualStyleBackColor = false;
            btnBackColor.UseWindowsAccentColor = false;
            btnBackColor.WriteProgress = true;
            btnBackColor.Click += BtnBackColor_Click;
            // 
            // btnOpenTemplateEditor
            // 
            btnOpenTemplateEditor.BackColor = Color.Transparent;
            btnOpenTemplateEditor.BackgroundImage = Properties.Resources.Arrow_Top_Right_Normal;
            btnOpenTemplateEditor.BackgroundImageLayout = ImageLayout.Stretch;
            btnOpenTemplateEditor.Cursor = Cursors.Hand;
            btnOpenTemplateEditor.Font = new Font("Tahoma", 9.75F);
            btnOpenTemplateEditor.ForeColor = Color.White;
            btnOpenTemplateEditor.HoverImage = Properties.Resources.Arrow_Top_Right_Hover;
            btnOpenTemplateEditor.Location = new Point(263, 216);
            btnOpenTemplateEditor.Margin = new Padding(5);
            btnOpenTemplateEditor.Name = "btnOpenTemplateEditor";
            btnOpenTemplateEditor.Size = new Size(27, 27);
            btnOpenTemplateEditor.TabIndex = 24;
            btnOpenTemplateEditor.TabStop = false;
            btnOpenTemplateEditor.Click += BtnOpenTemplateEditor_Click;
            // 
            // btnAddVariable
            // 
            btnAddVariable.BackColor = Color.Transparent;
            btnAddVariable.BackgroundImage = Properties.Resources.Variable_Normal;
            btnAddVariable.BackgroundImageLayout = ImageLayout.Stretch;
            btnAddVariable.Cursor = Cursors.Hand;
            btnAddVariable.Font = new Font("Tahoma", 8.25F);
            btnAddVariable.ForeColor = Color.White;
            btnAddVariable.HoverImage = Properties.Resources.Variable_Hover;
            btnAddVariable.Location = new Point(264, 283);
            btnAddVariable.Margin = new Padding(5);
            btnAddVariable.Name = "btnAddVariable";
            btnAddVariable.Size = new Size(27, 27);
            btnAddVariable.TabIndex = 22;
            btnAddVariable.TabStop = false;
            btnAddVariable.Click += BtnAddVariable_Click;
            // 
            // btnForeColor
            // 
            btnForeColor.BackgroundImageLayout = ImageLayout.Stretch;
            btnForeColor.BorderRadius = 8;
            btnForeColor.Cursor = Cursors.Hand;
            btnForeColor.FlatAppearance.BorderSize = 0;
            btnForeColor.FlatStyle = FlatStyle.Flat;
            btnForeColor.Font = new Font("Tahoma", 9.75F);
            btnForeColor.ForeColor = Color.White;
            btnForeColor.HoverColor = Color.Transparent;
            btnForeColor.Icon = Properties.Resources.Palette;
            btnForeColor.Location = new Point(265, 364);
            btnForeColor.Margin = new Padding(5);
            btnForeColor.Name = "btnForeColor";
            btnForeColor.Progress = 0;
            btnForeColor.ProgressColor = Color.FromArgb(0, 46, 94);
            btnForeColor.Size = new Size(26, 26);
            btnForeColor.TabIndex = 21;
            btnForeColor.UseMnemonic = false;
            btnForeColor.UseVisualStyleBackColor = false;
            btnForeColor.UseWindowsAccentColor = false;
            btnForeColor.WriteProgress = true;
            btnForeColor.Click += BtnForeColor_Click;
            // 
            // fonts
            // 
            fonts.BackColor = Color.FromArgb(65, 65, 65);
            fonts.Cursor = Cursors.Hand;
            fonts.DropDownStyle = ComboBoxStyle.DropDownList;
            fonts.Font = new Font("Tahoma", 9.75F);
            fonts.ForeColor = Color.White;
            fonts.Icon = null;
            fonts.Location = new Point(6, 362);
            fonts.Margin = new Padding(5, 6, 5, 6);
            fonts.Name = "fonts";
            fonts.Padding = new Padding(10, 2, 10, 2);
            fonts.SelectedIndex = -1;
            fonts.SelectedItem = null;
            fonts.Size = new Size(191, 28);
            fonts.TabIndex = 20;
            fonts.SelectedIndexChanged += LabelChanged;
            // 
            // lblCurrentState
            // 
            lblCurrentState.Font = new Font("Tahoma", 9.75F);
            lblCurrentState.Location = new Point(220, 441);
            lblCurrentState.Margin = new Padding(5, 0, 5, 0);
            lblCurrentState.Name = "lblCurrentState";
            lblCurrentState.Size = new Size(70, 28);
            lblCurrentState.TabIndex = 22;
            lblCurrentState.Text = "Off";
            lblCurrentState.TextAlign = ContentAlignment.MiddleLeft;
            lblCurrentState.UseMnemonic = false;
            // 
            // lblCurrentStateLabel
            // 
            lblCurrentStateLabel.Font = new Font("Tahoma", 9.75F);
            lblCurrentStateLabel.Location = new Point(6, 441);
            lblCurrentStateLabel.Margin = new Padding(5, 0, 5, 0);
            lblCurrentStateLabel.Name = "lblCurrentStateLabel";
            lblCurrentStateLabel.Size = new Size(204, 28);
            lblCurrentStateLabel.TabIndex = 23;
            lblCurrentStateLabel.Text = "Current state:";
            lblCurrentStateLabel.TextAlign = ContentAlignment.MiddleLeft;
            lblCurrentStateLabel.UseMnemonic = false;
            // 
            // btnOk
            // 
            btnOk.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnOk.BorderRadius = 8;
            btnOk.Cursor = Cursors.Hand;
            btnOk.FlatStyle = FlatStyle.Flat;
            btnOk.Font = new Font("Tahoma", 9.75F);
            btnOk.ForeColor = Color.White;
            btnOk.HoverColor = Color.FromArgb(0, 89, 184);
            btnOk.Icon = null;
            btnOk.Location = new Point(1007, 599);
            btnOk.Margin = new Padding(5);
            btnOk.Name = "btnOk";
            btnOk.Progress = 0;
            btnOk.ProgressColor = Color.FromArgb(0, 46, 94);
            btnOk.Size = new Size(88, 30);
            btnOk.TabIndex = 25;
            btnOk.Text = "Ok";
            btnOk.UseMnemonic = false;
            btnOk.UseVisualStyleBackColor = true;
            btnOk.UseWindowsAccentColor = true;
            btnOk.WriteProgress = true;
            btnOk.Click += BtnOk_Click;
            // 
            // variablesContextMenu
            // 
            variablesContextMenu.BackColor = Color.FromArgb(45, 45, 45);
            variablesContextMenu.Font = new Font("Tahoma", 9.75F);
            variablesContextMenu.Name = "variablesContextMenu";
            variablesContextMenu.ShowImageMargin = false;
            variablesContextMenu.ShowItemToolTips = false;
            variablesContextMenu.Size = new Size(36, 4);
            // 
            // lblStateBinding
            // 
            lblStateBinding.Font = new Font("Tahoma", 9.75F);
            lblStateBinding.Location = new Point(6, 469);
            lblStateBinding.Margin = new Padding(5, 0, 5, 0);
            lblStateBinding.Name = "lblStateBinding";
            lblStateBinding.Size = new Size(282, 17);
            lblStateBinding.TabIndex = 26;
            lblStateBinding.Text = "State binding:";
            lblStateBinding.TextAlign = ContentAlignment.MiddleLeft;
            lblStateBinding.UseMnemonic = false;
            // 
            // listStateBinding
            // 
            listStateBinding.BackColor = Color.FromArgb(65, 65, 65);
            listStateBinding.Cursor = Cursors.Hand;
            listStateBinding.DropDownStyle = ComboBoxStyle.DropDownList;
            listStateBinding.Font = new Font("Tahoma", 9F);
            listStateBinding.ForeColor = Color.White;
            listStateBinding.Icon = null;
            listStateBinding.Location = new Point(6, 491);
            listStateBinding.Margin = new Padding(5);
            listStateBinding.Name = "listStateBinding";
            listStateBinding.Padding = new Padding(10, 2, 10, 2);
            listStateBinding.SelectedIndex = -1;
            listStateBinding.SelectedItem = null;
            listStateBinding.Size = new Size(253, 26);
            listStateBinding.TabIndex = 27;
            listStateBinding.SelectedIndexChanged += ListStateBinding_SelectedIndexChanged;
            // 
            // btnDeleteStateBinding
            // 
            btnDeleteStateBinding.BackColor = Color.Transparent;
            btnDeleteStateBinding.BackgroundImage = Properties.Resources.Delete_Normal;
            btnDeleteStateBinding.BackgroundImageLayout = ImageLayout.Stretch;
            btnDeleteStateBinding.Cursor = Cursors.Hand;
            btnDeleteStateBinding.Font = new Font("Tahoma", 9.75F);
            btnDeleteStateBinding.ForeColor = Color.White;
            btnDeleteStateBinding.HoverImage = Properties.Resources.Delete_Hover;
            btnDeleteStateBinding.Location = new Point(264, 491);
            btnDeleteStateBinding.Margin = new Padding(5);
            btnDeleteStateBinding.Name = "btnDeleteStateBinding";
            btnDeleteStateBinding.Size = new Size(27, 27);
            btnDeleteStateBinding.TabIndex = 28;
            btnDeleteStateBinding.TabStop = false;
            btnDeleteStateBinding.Click += BtnDeleteStateBinding_Click;
            // 
            // selectorPanel
            // 
            selectorPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            selectorPanel.Location = new Point(310, 81);
            selectorPanel.Margin = new Padding(5);
            selectorPanel.Name = "selectorPanel";
            selectorPanel.Size = new Size(882, 508);
            selectorPanel.TabIndex = 29;
            // 
            // flowLayoutPanel1
            // 
            flowLayoutPanel1.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            flowLayoutPanel1.AutoScroll = true;
            flowLayoutPanel1.AutoScrollMargin = new Size(0, 30);
            flowLayoutPanel1.Controls.Add(radioOnPress);
            flowLayoutPanel1.Controls.Add(radioOnRelease);
            flowLayoutPanel1.Controls.Add(radioOnLongPress);
            flowLayoutPanel1.Controls.Add(radioOnLongPressRelease);
            flowLayoutPanel1.Controls.Add(radioOnEvent);
            flowLayoutPanel1.Font = new Font("Tahoma", 12F);
            flowLayoutPanel1.Location = new Point(309, 49);
            flowLayoutPanel1.Margin = new Padding(0);
            flowLayoutPanel1.Name = "flowLayoutPanel1";
            flowLayoutPanel1.Size = new Size(877, 27);
            flowLayoutPanel1.TabIndex = 30;
            // 
            // radioOnPress
            // 
            radioOnPress.AutoSize = true;
            radioOnPress.BorderRadius = 8;
            radioOnPress.Checked = true;
            radioOnPress.Cursor = Cursors.Hand;
            radioOnPress.Font = new Font("Tahoma", 11.25F);
            radioOnPress.Icon = null;
            radioOnPress.IconAlignment = ContentAlignment.MiddleLeft;
            radioOnPress.Location = new Point(0, 0);
            radioOnPress.Margin = new Padding(0, 0, 6, 0);
            radioOnPress.Name = "radioOnPress";
            radioOnPress.Size = new Size(85, 22);
            radioOnPress.TabIndex = 0;
            radioOnPress.TabStop = true;
            radioOnPress.Text = "On press";
            radioOnPress.UseMnemonic = false;
            radioOnPress.UseVisualStyleBackColor = true;
            radioOnPress.CheckedChanged += RadioOnPress_CheckedChanged;
            // 
            // radioOnRelease
            // 
            radioOnRelease.AutoSize = true;
            radioOnRelease.BorderRadius = 8;
            radioOnRelease.Cursor = Cursors.Hand;
            radioOnRelease.Font = new Font("Tahoma", 11.25F);
            radioOnRelease.Icon = null;
            radioOnRelease.IconAlignment = ContentAlignment.MiddleLeft;
            radioOnRelease.Location = new Point(91, 0);
            radioOnRelease.Margin = new Padding(0, 0, 6, 0);
            radioOnRelease.Name = "radioOnRelease";
            radioOnRelease.Size = new Size(96, 22);
            radioOnRelease.TabIndex = 2;
            radioOnRelease.Text = "On release";
            radioOnRelease.UseMnemonic = false;
            radioOnRelease.UseVisualStyleBackColor = true;
            radioOnRelease.CheckedChanged += RadioOnRelease_CheckedChanged;
            // 
            // radioOnLongPress
            // 
            radioOnLongPress.AutoSize = true;
            radioOnLongPress.BorderRadius = 8;
            radioOnLongPress.Cursor = Cursors.Hand;
            radioOnLongPress.Font = new Font("Tahoma", 11.25F);
            radioOnLongPress.Icon = null;
            radioOnLongPress.IconAlignment = ContentAlignment.MiddleLeft;
            radioOnLongPress.Location = new Point(193, 0);
            radioOnLongPress.Margin = new Padding(0, 0, 6, 0);
            radioOnLongPress.Name = "radioOnLongPress";
            radioOnLongPress.Size = new Size(116, 22);
            radioOnLongPress.TabIndex = 3;
            radioOnLongPress.Text = "On long press";
            radioOnLongPress.UseMnemonic = false;
            radioOnLongPress.UseVisualStyleBackColor = true;
            radioOnLongPress.CheckedChanged += RadioOnLongPress_CheckedChanged;
            // 
            // radioOnLongPressRelease
            // 
            radioOnLongPressRelease.AutoSize = true;
            radioOnLongPressRelease.BorderRadius = 8;
            radioOnLongPressRelease.Cursor = Cursors.Hand;
            radioOnLongPressRelease.Font = new Font("Tahoma", 11.25F);
            radioOnLongPressRelease.Icon = null;
            radioOnLongPressRelease.IconAlignment = ContentAlignment.MiddleLeft;
            radioOnLongPressRelease.Location = new Point(315, 0);
            radioOnLongPressRelease.Margin = new Padding(0, 0, 6, 0);
            radioOnLongPressRelease.Name = "radioOnLongPressRelease";
            radioOnLongPressRelease.Size = new Size(167, 22);
            radioOnLongPressRelease.TabIndex = 4;
            radioOnLongPressRelease.Text = "On long press release";
            radioOnLongPressRelease.UseMnemonic = false;
            radioOnLongPressRelease.UseVisualStyleBackColor = true;
            radioOnLongPressRelease.CheckedChanged += RadioOnLongPressRelease_CheckedChanged;
            // 
            // radioOnEvent
            // 
            radioOnEvent.AutoSize = true;
            radioOnEvent.BorderRadius = 8;
            radioOnEvent.Cursor = Cursors.Hand;
            radioOnEvent.Font = new Font("Tahoma", 11.25F);
            radioOnEvent.Icon = null;
            radioOnEvent.IconAlignment = ContentAlignment.MiddleLeft;
            radioOnEvent.Location = new Point(488, 0);
            radioOnEvent.Margin = new Padding(0, 0, 6, 0);
            radioOnEvent.Name = "radioOnEvent";
            radioOnEvent.Size = new Size(87, 22);
            radioOnEvent.TabIndex = 1;
            radioOnEvent.Text = "On event";
            radioOnEvent.UseMnemonic = false;
            radioOnEvent.UseVisualStyleBackColor = true;
            radioOnEvent.CheckedChanged += RadioOnEvent_CheckedChanged;
            // 
            // btnRemoveHotkey
            // 
            btnRemoveHotkey.BackColor = Color.Transparent;
            btnRemoveHotkey.BackgroundImage = Properties.Resources.Delete_Normal;
            btnRemoveHotkey.BackgroundImageLayout = ImageLayout.Stretch;
            btnRemoveHotkey.Cursor = Cursors.Hand;
            btnRemoveHotkey.Font = new Font("Tahoma", 9.75F);
            btnRemoveHotkey.ForeColor = Color.White;
            btnRemoveHotkey.HoverImage = Properties.Resources.Delete_Hover;
            btnRemoveHotkey.Location = new Point(264, 577);
            btnRemoveHotkey.Margin = new Padding(5);
            btnRemoveHotkey.Name = "btnRemoveHotkey";
            btnRemoveHotkey.Size = new Size(27, 27);
            btnRemoveHotkey.TabIndex = 20;
            btnRemoveHotkey.TabStop = false;
            btnRemoveHotkey.Click += BtnRemoveHotkey_Click;
            // 
            // hotkey
            // 
            hotkey.BackColor = Color.FromArgb(65, 65, 65);
            hotkey.Cursor = Cursors.Hand;
            hotkey.Font = new Font("Tahoma", 9F);
            hotkey.Icon = Properties.Resources.Keyboard;
            hotkey.Location = new Point(6, 575);
            hotkey.Margin = new Padding(5);
            hotkey.MaxCharacters = 32767;
            hotkey.Multiline = false;
            hotkey.Name = "hotkey";
            hotkey.Padding = new Padding(35, 7, 10, 7);
            hotkey.PasswordChar = false;
            hotkey.PlaceHolderColor = Color.Gray;
            hotkey.PlaceHolderText = "";
            hotkey.ReadOnly = false;
            hotkey.ScrollBars = ScrollBars.None;
            hotkey.SelectionStart = 0;
            hotkey.Size = new Size(253, 29);
            hotkey.TabIndex = 0;
            hotkey.TabStop = false;
            hotkey.TextAlignment = HorizontalAlignment.Left;
            // 
            // btnEditJson
            // 
            btnEditJson.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            btnEditJson.BorderRadius = 8;
            btnEditJson.Cursor = Cursors.Hand;
            btnEditJson.FlatAppearance.BorderSize = 0;
            btnEditJson.FlatStyle = FlatStyle.Flat;
            btnEditJson.Font = new Font("Tahoma", 9.75F);
            btnEditJson.ForeColor = Color.White;
            btnEditJson.HoverColor = Color.Empty;
            btnEditJson.Icon = null;
            btnEditJson.Location = new Point(310, 599);
            btnEditJson.Margin = new Padding(5);
            btnEditJson.Name = "btnEditJson";
            btnEditJson.Progress = 0;
            btnEditJson.ProgressColor = Color.FromArgb(0, 103, 225);
            btnEditJson.Size = new Size(116, 30);
            btnEditJson.TabIndex = 33;
            btnEditJson.Text = "Edit JSON";
            btnEditJson.UseMnemonic = false;
            btnEditJson.UseVisualStyleBackColor = true;
            btnEditJson.UseWindowsAccentColor = true;
            btnEditJson.WriteProgress = true;
            btnEditJson.Click += BtnEditJson_Click;
            // 
            // label1
            // 
            label1.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            label1.Location = new Point(447, 599);
            label1.Margin = new Padding(5, 0, 5, 0);
            label1.Name = "label1";
            label1.Size = new Size(58, 30);
            label1.TabIndex = 34;
            label1.Text = "GUID:";
            label1.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // buttonGUIDLabel
            // 
            buttonGUIDLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            buttonGUIDLabel.BackColor = Color.FromArgb(45, 45, 45);
            buttonGUIDLabel.BorderStyle = BorderStyle.None;
            buttonGUIDLabel.ForeColor = Color.White;
            buttonGUIDLabel.Location = new Point(515, 605);
            buttonGUIDLabel.Margin = new Padding(5);
            buttonGUIDLabel.Name = "buttonGUIDLabel";
            buttonGUIDLabel.ReadOnly = true;
            buttonGUIDLabel.Size = new Size(400, 16);
            buttonGUIDLabel.TabIndex = 35;
            // 
            // lblAppearance
            // 
            lblAppearance.BackColor = Color.FromArgb(35, 35, 35);
            lblAppearance.Dock = DockStyle.Top;
            lblAppearance.Font = new Font("Tahoma", 11.25F, FontStyle.Bold);
            lblAppearance.Location = new Point(0, 0);
            lblAppearance.Margin = new Padding(4, 0, 4, 0);
            lblAppearance.Name = "lblAppearance";
            lblAppearance.Size = new Size(286, 35);
            lblAppearance.TabIndex = 36;
            lblAppearance.Text = "Appearance";
            lblAppearance.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // panel3
            // 
            panel3.BackColor = Color.FromArgb(35, 35, 35);
            panel3.Controls.Add(lblAppearance);
            panel3.Controls.Add(panel1);
            panel3.Location = new Point(5, 5);
            panel3.Margin = new Padding(4);
            panel3.Name = "panel3";
            panel3.Size = new Size(286, 70);
            panel3.TabIndex = 37;
            // 
            // lblState
            // 
            lblState.BackColor = Color.FromArgb(35, 35, 35);
            lblState.Font = new Font("Tahoma", 11.25F, FontStyle.Bold);
            lblState.Location = new Point(5, 406);
            lblState.Margin = new Padding(4, 0, 4, 0);
            lblState.Name = "lblState";
            lblState.Size = new Size(286, 35);
            lblState.TabIndex = 37;
            lblState.Text = "Button state";
            lblState.TextAlign = ContentAlignment.MiddleLeft;
            lblState.Click += label4_Click;
            // 
            // lblKeyBinding
            // 
            lblKeyBinding.BackColor = Color.FromArgb(35, 35, 35);
            lblKeyBinding.Font = new Font("Tahoma", 11.25F, FontStyle.Bold);
            lblKeyBinding.Location = new Point(6, 535);
            lblKeyBinding.Margin = new Padding(4, 0, 4, 0);
            lblKeyBinding.Name = "lblKeyBinding";
            lblKeyBinding.Size = new Size(285, 35);
            lblKeyBinding.TabIndex = 38;
            lblKeyBinding.Text = "Key binding";
            lblKeyBinding.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // lblActions
            // 
            lblActions.BackColor = Color.FromArgb(35, 35, 35);
            lblActions.Font = new Font("Tahoma", 11.25F, FontStyle.Bold);
            lblActions.Location = new Point(309, 5);
            lblActions.Margin = new Padding(4, 0, 4, 0);
            lblActions.Name = "lblActions";
            lblActions.Size = new Size(350, 35);
            lblActions.TabIndex = 39;
            lblActions.Text = "Actions";
            lblActions.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // panel4
            // 
            panel4.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            panel4.BackColor = Color.Silver;
            panel4.Location = new Point(300, 2);
            panel4.Margin = new Padding(4);
            panel4.Name = "panel4";
            panel4.Size = new Size(1, 630);
            panel4.TabIndex = 40;
            // 
            // ButtonEditor
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(45, 45, 45);
            ClientSize = new Size(1200, 635);
            Controls.Add(panel4);
            Controls.Add(lblActions);
            Controls.Add(lblKeyBinding);
            Controls.Add(btnRemoveHotkey);
            Controls.Add(lblStateBinding);
            Controls.Add(hotkey);
            Controls.Add(lblState);
            Controls.Add(listStateBinding);
            Controls.Add(btnDeleteStateBinding);
            Controls.Add(panel3);
            Controls.Add(btnBackColor);
            Controls.Add(lblCurrentState);
            Controls.Add(btnOpenTemplateEditor);
            Controls.Add(lblCurrentStateLabel);
            Controls.Add(buttonGUIDLabel);
            Controls.Add(btnAddVariable);
            Controls.Add(label1);
            Controls.Add(btnForeColor);
            Controls.Add(btnEditJson);
            Controls.Add(fonts);
            Controls.Add(btnClearLabelText);
            Controls.Add(panel2);
            Controls.Add(flowLayoutPanel1);
            Controls.Add(labelText);
            Controls.Add(selectorPanel);
            Controls.Add(btnEditIcon);
            Controls.Add(btnOk);
            Controls.Add(btnRemoveIcon);
            Controls.Add(btnApply);
            Controls.Add(fontSize);
            Controls.Add(btnPreview);
            Margin = new Padding(5, 6, 5, 6);
            Name = "ButtonEditor";
            Text = "Macro Deck :: Edit button";
            Shown += ButtonEditor_Shown;
            ((ISupportInitialize)btnPreview).EndInit();
            ((ISupportInitialize)fontSize).EndInit();
            panel1.ResumeLayout(false);
            panel2.ResumeLayout(false);
            ((ISupportInitialize)btnEditIcon).EndInit();
            ((ISupportInitialize)btnRemoveIcon).EndInit();
            ((ISupportInitialize)btnClearLabelText).EndInit();
            ((ISupportInitialize)btnOpenTemplateEditor).EndInit();
            ((ISupportInitialize)btnAddVariable).EndInit();
            ((ISupportInitialize)btnDeleteStateBinding).EndInit();
            flowLayoutPanel1.ResumeLayout(false);
            flowLayoutPanel1.PerformLayout();
            ((ISupportInitialize)btnRemoveHotkey).EndInit();
            panel3.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();

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