
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.Interfaces;
using SuchByte.MacroDeck.Properties;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    partial class ConditionItem
    {
        /// <summary> 
        /// Erforderliche Designervariable.
        /// </summary>
        private IContainer components = null;

        /// <summary> 
        /// Verwendete Ressourcen bereinigen.
        /// </summary>
        /// <param name="disposing">True, wenn verwaltete Ressourcen gelöscht werden sollen; andernfalls False.</param>
        protected override void Dispose(bool disposing)
        {
            foreach (IActionConditionItem actionItem in this.actionsList.Controls)
            {
                actionItem.OnRemoveClick -= this.RemoveClicked;
                actionItem.OnEditClick -= this.EditClicked;
                actionItem.OnMoveUpClick -= this.MoveUpClicked;
                actionItem.OnMoveDownClick -= this.MoveDownClicked;

            }
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Vom Komponenten-Designer generierter Code

        /// <summary> 
        /// Erforderliche Methode für die Designerunterstützung. 
        /// Der Inhalt der Methode darf nicht mit dem Code-Editor geändert werden.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new Container();
            this.lblIf = new Label();
            this.typeBox = new RoundedComboBox();
            this.methodBox = new RoundedComboBox();
            this.valueToCompare = new RoundedComboBox();
            this.actionsList = new FlowLayoutPanel();
            this.btnAddAction = new PictureButton();
            this.source = new RoundedComboBox();
            this.panelEdit = new FlowLayoutPanel();
            this.btnRemove = new PictureButton();
            this.btnDown = new PictureButton();
            this.btnUp = new PictureButton();
            this.lblElse = new Label();
            this.elseActionsList = new FlowLayoutPanel();
            this.btnAddActionElse = new PictureButton();
            this.flowLayoutPanel1 = new FlowLayoutPanel();
            this.flowLayoutPanel2 = new FlowLayoutPanel();
            this.addItemContextMenu = new ContextMenuStrip(this.components);
            this.menuItemAction = new ToolStripMenuItem();
            this.menuItemDelay = new ToolStripMenuItem();
            this.template = new RoundedTextBox();
            this.btnOpenTemplateEditor = new ButtonPrimary();
            ((ISupportInitialize)(this.btnAddAction)).BeginInit();
            this.panelEdit.SuspendLayout();
            ((ISupportInitialize)(this.btnRemove)).BeginInit();
            ((ISupportInitialize)(this.btnDown)).BeginInit();
            ((ISupportInitialize)(this.btnUp)).BeginInit();
            ((ISupportInitialize)(this.btnAddActionElse)).BeginInit();
            this.flowLayoutPanel1.SuspendLayout();
            this.flowLayoutPanel2.SuspendLayout();
            this.addItemContextMenu.SuspendLayout();
            this.SuspendLayout();
            // 
            // lblIf
            // 
            this.lblIf.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblIf.ImageAlign = ContentAlignment.MiddleLeft;
            this.lblIf.Location = new Point(9, 2);
            this.lblIf.Name = "lblIf";
            this.lblIf.Size = new Size(77, 28);
            this.lblIf.TabIndex = 4;
            this.lblIf.Text = "If";
            this.lblIf.UseMnemonic = false;
            this.lblIf.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // typeBox
            // 
            this.typeBox.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.typeBox.Cursor = Cursors.Hand;
            this.typeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            this.typeBox.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.typeBox.Icon = null;
            this.typeBox.Location = new Point(93, 2);
            this.typeBox.Margin = new Padding(4, 5, 4, 5);
            this.typeBox.Name = "typeBox";
            this.typeBox.Padding = new Padding(8, 2, 8, 2);
            this.typeBox.SelectedIndex = -1;
            this.typeBox.SelectedItem = null;
            this.typeBox.Size = new Size(115, 28);
            this.typeBox.TabIndex = 5;
            this.typeBox.SelectedIndexChanged += new EventHandler(this.TypeBox_SelectedIndexChanged);
            this.typeBox.Load += new EventHandler(this.typeBox_Load);
            // 
            // methodBox
            // 
            this.methodBox.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.methodBox.Cursor = Cursors.Hand;
            this.methodBox.DropDownStyle = ComboBoxStyle.DropDownList;
            this.methodBox.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.methodBox.Icon = null;
            this.methodBox.Location = new Point(245, 0);
            this.methodBox.Margin = new Padding(3, 0, 3, 0);
            this.methodBox.Name = "methodBox";
            this.methodBox.Padding = new Padding(8, 2, 8, 2);
            this.methodBox.SelectedIndex = -1;
            this.methodBox.SelectedItem = null;
            this.methodBox.Size = new Size(97, 28);
            this.methodBox.TabIndex = 6;
            this.methodBox.SelectedIndexChanged += new EventHandler(this.MethodBox_SelectedIndexChanged);
            // 
            // valueToCompare
            // 
            this.valueToCompare.Anchor = ((AnchorStyles)(((AnchorStyles.Top | AnchorStyles.Left) 
                                                          | AnchorStyles.Right)));
            this.valueToCompare.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.valueToCompare.Cursor = Cursors.Hand;
            this.valueToCompare.DropDownStyle = ComboBoxStyle.DropDown;
            this.valueToCompare.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.valueToCompare.Icon = null;
            this.valueToCompare.Location = new Point(348, 0);
            this.valueToCompare.Margin = new Padding(3, 0, 3, 0);
            this.valueToCompare.Name = "valueToCompare";
            this.valueToCompare.Padding = new Padding(8, 2, 8, 2);
            this.valueToCompare.SelectedIndex = -1;
            this.valueToCompare.SelectedItem = null;
            this.valueToCompare.Size = new Size(166, 28);
            this.valueToCompare.TabIndex = 7;
            this.valueToCompare.SelectedIndexChanged += new EventHandler(this.ValueToCompare_TextChanged);
            // 
            // actionsList
            // 
            this.actionsList.AutoSize = true;
            this.actionsList.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.actionsList.FlowDirection = FlowDirection.TopDown;
            this.actionsList.Location = new Point(0, 0);
            this.actionsList.Margin = new Padding(0);
            this.actionsList.MaximumSize = new Size(840, 0);
            this.actionsList.MinimumSize = new Size(840, 0);
            this.actionsList.Name = "actionsList";
            this.actionsList.Size = new Size(840, 0);
            this.actionsList.TabIndex = 8;
            // 
            // btnAddAction
            // 
            this.btnAddAction.BackColor = Color.Transparent;
            this.btnAddAction.BackgroundImage = Resources.Create_Normal;
            this.btnAddAction.BackgroundImageLayout = ImageLayout.Stretch;
            this.btnAddAction.Cursor = Cursors.Hand;
            this.btnAddAction.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnAddAction.ForeColor = Color.White;
            this.btnAddAction.HoverImage = Resources.Create_Hover;
            this.btnAddAction.Location = new Point(3, 3);
            this.btnAddAction.Name = "btnAddAction";
            this.btnAddAction.Size = new Size(27, 27);
            this.btnAddAction.TabIndex = 9;
            this.btnAddAction.TabStop = false;
            this.btnAddAction.Text = "+ Action";
            this.btnAddAction.Click += new EventHandler(this.BtnAddAction_Click);
            // 
            // source
            // 
            this.source.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.source.Cursor = Cursors.Hand;
            this.source.DropDownStyle = ComboBoxStyle.DropDownList;
            this.source.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.source.Icon = null;
            this.source.Location = new Point(3, 0);
            this.source.Margin = new Padding(3, 0, 3, 0);
            this.source.Name = "source";
            this.source.Padding = new Padding(8, 2, 8, 2);
            this.source.SelectedIndex = -1;
            this.source.SelectedItem = null;
            this.source.Size = new Size(236, 28);
            this.source.TabIndex = 10;
            this.source.SelectedIndexChanged += new EventHandler(this.Source_SelectedIndexChanged);
            // 
            // panelEdit
            // 
            this.panelEdit.Anchor = ((AnchorStyles)((AnchorStyles.Top | AnchorStyles.Right)));
            this.panelEdit.Controls.Add(this.btnRemove);
            this.panelEdit.Controls.Add(this.btnDown);
            this.panelEdit.Controls.Add(this.btnUp);
            this.panelEdit.FlowDirection = FlowDirection.RightToLeft;
            this.panelEdit.Location = new Point(758, 2);
            this.panelEdit.Name = "panelEdit";
            this.panelEdit.Size = new Size(89, 26);
            this.panelEdit.TabIndex = 11;
            // 
            // btnRemove
            // 
            this.btnRemove.Anchor = ((AnchorStyles)((AnchorStyles.Top | AnchorStyles.Right)));
            this.btnRemove.BackColor = Color.Transparent;
            this.btnRemove.BackgroundImage = Resources.Delete_Normal;
            this.btnRemove.BackgroundImageLayout = ImageLayout.Stretch;
            this.btnRemove.Cursor = Cursors.Hand;
            this.btnRemove.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnRemove.ForeColor = Color.White;
            this.btnRemove.HoverImage = Resources.Delete_Hover;
            this.btnRemove.Location = new Point(62, 0);
            this.btnRemove.Margin = new Padding(2, 0, 2, 0);
            this.btnRemove.Name = "btnRemove";
            this.btnRemove.Size = new Size(25, 25);
            this.btnRemove.TabIndex = 2;
            this.btnRemove.TabStop = false;
            this.btnRemove.Click += new EventHandler(this.BtnRemove_Click);
            // 
            // btnDown
            // 
            this.btnDown.Anchor = ((AnchorStyles)((AnchorStyles.Top | AnchorStyles.Right)));
            this.btnDown.BackColor = Color.Transparent;
            this.btnDown.BackgroundImage = Resources.Arrow_Down_Normal;
            this.btnDown.BackgroundImageLayout = ImageLayout.Stretch;
            this.btnDown.Cursor = Cursors.Hand;
            this.btnDown.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnDown.ForeColor = Color.White;
            this.btnDown.HoverImage = Resources.Arrow_Down_Hover;
            this.btnDown.Location = new Point(33, 0);
            this.btnDown.Margin = new Padding(2, 0, 2, 0);
            this.btnDown.Name = "btnDown";
            this.btnDown.Size = new Size(25, 25);
            this.btnDown.TabIndex = 4;
            this.btnDown.TabStop = false;
            this.btnDown.Click += new EventHandler(this.BtnDown_Click);
            // 
            // btnUp
            // 
            this.btnUp.Anchor = ((AnchorStyles)((AnchorStyles.Top | AnchorStyles.Right)));
            this.btnUp.BackColor = Color.Transparent;
            this.btnUp.BackgroundImage = Resources.Arrow_Up_Normal;
            this.btnUp.BackgroundImageLayout = ImageLayout.Stretch;
            this.btnUp.Cursor = Cursors.Hand;
            this.btnUp.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnUp.ForeColor = Color.White;
            this.btnUp.HoverImage = Resources.Arrow_Up_Hover;
            this.btnUp.Location = new Point(4, 0);
            this.btnUp.Margin = new Padding(2, 0, 2, 0);
            this.btnUp.Name = "btnUp";
            this.btnUp.Size = new Size(25, 25);
            this.btnUp.TabIndex = 5;
            this.btnUp.TabStop = false;
            this.btnUp.Click += new EventHandler(this.BtnUp_Click);
            // 
            // lblElse
            // 
            this.lblElse.Anchor = ((AnchorStyles)((AnchorStyles.Bottom | AnchorStyles.Left)));
            this.lblElse.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblElse.Location = new Point(3, 33);
            this.lblElse.Name = "lblElse";
            this.lblElse.Size = new Size(148, 33);
            this.lblElse.TabIndex = 12;
            this.lblElse.Text = "Else";
            this.lblElse.TextAlign = ContentAlignment.MiddleLeft;
            this.lblElse.UseMnemonic = false;
            // 
            // elseActionsList
            // 
            this.elseActionsList.Anchor = ((AnchorStyles)((AnchorStyles.Bottom | AnchorStyles.Left)));
            this.elseActionsList.AutoSize = true;
            this.elseActionsList.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.elseActionsList.FlowDirection = FlowDirection.TopDown;
            this.elseActionsList.Location = new Point(0, 66);
            this.elseActionsList.Margin = new Padding(0);
            this.elseActionsList.MaximumSize = new Size(840, 0);
            this.elseActionsList.MinimumSize = new Size(840, 0);
            this.elseActionsList.Name = "elseActionsList";
            this.elseActionsList.Size = new Size(840, 0);
            this.elseActionsList.TabIndex = 9;
            // 
            // btnAddActionElse
            // 
            this.btnAddActionElse.Anchor = ((AnchorStyles)((AnchorStyles.Bottom | AnchorStyles.Left)));
            this.btnAddActionElse.BackColor = Color.Transparent;
            this.btnAddActionElse.BackgroundImage = Resources.Create_Normal;
            this.btnAddActionElse.BackgroundImageLayout = ImageLayout.Stretch;
            this.btnAddActionElse.Cursor = Cursors.Hand;
            this.btnAddActionElse.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnAddActionElse.ForeColor = Color.White;
            this.btnAddActionElse.HoverImage = Resources.Create_Hover;
            this.btnAddActionElse.Location = new Point(3, 69);
            this.btnAddActionElse.Name = "btnAddActionElse";
            this.btnAddActionElse.Size = new Size(27, 27);
            this.btnAddActionElse.TabIndex = 13;
            this.btnAddActionElse.TabStop = false;
            this.btnAddActionElse.Click += new EventHandler(this.BtnAddActionElse_Click);
            // 
            // flowLayoutPanel1
            // 
            this.flowLayoutPanel1.Anchor = ((AnchorStyles)(((AnchorStyles.Top | AnchorStyles.Left) 
                                                            | AnchorStyles.Right)));
            this.flowLayoutPanel1.AutoSize = true;
            this.flowLayoutPanel1.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.flowLayoutPanel1.BackColor = Color.Transparent;
            this.flowLayoutPanel1.Controls.Add(this.actionsList);
            this.flowLayoutPanel1.Controls.Add(this.btnAddAction);
            this.flowLayoutPanel1.Controls.Add(this.lblElse);
            this.flowLayoutPanel1.Controls.Add(this.elseActionsList);
            this.flowLayoutPanel1.Controls.Add(this.btnAddActionElse);
            this.flowLayoutPanel1.FlowDirection = FlowDirection.TopDown;
            this.flowLayoutPanel1.Location = new Point(9, 37);
            this.flowLayoutPanel1.Margin = new Padding(0);
            this.flowLayoutPanel1.MaximumSize = new Size(840, 0);
            this.flowLayoutPanel1.MinimumSize = new Size(840, 0);
            this.flowLayoutPanel1.Name = "flowLayoutPanel1";
            this.flowLayoutPanel1.Padding = new Padding(0, 0, 0, 10);
            this.flowLayoutPanel1.Size = new Size(840, 109);
            this.flowLayoutPanel1.TabIndex = 14;
            // 
            // flowLayoutPanel2
            // 
            this.flowLayoutPanel2.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.flowLayoutPanel2.Controls.Add(this.source);
            this.flowLayoutPanel2.Controls.Add(this.methodBox);
            this.flowLayoutPanel2.Controls.Add(this.valueToCompare);
            this.flowLayoutPanel2.Controls.Add(this.template);
            this.flowLayoutPanel2.Controls.Add(this.btnOpenTemplateEditor);
            this.flowLayoutPanel2.Location = new Point(215, 2);
            this.flowLayoutPanel2.Margin = new Padding(0);
            this.flowLayoutPanel2.MaximumSize = new Size(522, 0);
            this.flowLayoutPanel2.MinimumSize = new Size(522, 28);
            this.flowLayoutPanel2.Name = "flowLayoutPanel2";
            this.flowLayoutPanel2.Size = new Size(522, 28);
            this.flowLayoutPanel2.TabIndex = 15;
            // 
            // addItemContextMenu
            // 
            this.addItemContextMenu.BackColor = Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.addItemContextMenu.Font = new Font("Tahoma", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.addItemContextMenu.Items.AddRange(new ToolStripItem[] {
            this.menuItemAction,
            this.menuItemDelay});
            this.addItemContextMenu.LayoutStyle = ToolStripLayoutStyle.Table;
            this.addItemContextMenu.Name = "addItemContextMenu";
            this.addItemContextMenu.ShowImageMargin = false;
            this.addItemContextMenu.Size = new Size(107, 60);
            // 
            // menuItemAction
            // 
            this.menuItemAction.ForeColor = Color.White;
            this.menuItemAction.Name = "menuItemAction";
            this.menuItemAction.Size = new Size(106, 28);
            this.menuItemAction.Text = "Action";
            this.menuItemAction.Click += new EventHandler(this.MenuItemAction_Click);
            // 
            // menuItemDelay
            // 
            this.menuItemDelay.ForeColor = Color.White;
            this.menuItemDelay.Name = "menuItemDelay";
            this.menuItemDelay.Size = new Size(106, 28);
            this.menuItemDelay.Text = "Delay";
            this.menuItemDelay.Click += new EventHandler(this.MenuItemDelay_Click);
            // 
            // template
            // 
            this.template.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.template.Cursor = Cursors.Hand;
            this.template.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.template.Icon = null;
            this.template.Location = new Point(3, 31);
            this.template.MaxCharacters = 32767;
            this.template.Multiline = false;
            this.template.Name = "template";
            this.template.Padding = new Padding(8, 5, 8, 5);
            this.template.PasswordChar = false;
            this.template.PlaceHolderColor = Color.Gray;
            this.template.PlaceHolderText = "";
            this.template.ReadOnly = false;
            this.template.ScrollBars = ScrollBars.None;
            this.template.SelectionStart = 0;
            this.template.Size = new Size(467, 25);
            this.template.TabIndex = 11;
            this.template.TextAlignment = HorizontalAlignment.Left;
            this.template.TextChanged += Template_TextChanged;
            // 
            // btnOpenTemplateEditor
            // 
            this.btnOpenTemplateEditor.BackColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnOpenTemplateEditor.BorderRadius = 8;
            this.btnOpenTemplateEditor.Cursor = Cursors.Hand;
            this.btnOpenTemplateEditor.FlatAppearance.BorderSize = 0;
            this.btnOpenTemplateEditor.FlatStyle = FlatStyle.Flat;
            this.btnOpenTemplateEditor.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnOpenTemplateEditor.ForeColor = Color.White;
            this.btnOpenTemplateEditor.HoverColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnOpenTemplateEditor.Icon = Resources.Arrow_Top_Right_Hover;
            this.btnOpenTemplateEditor.Location = new Point(476, 31);
            this.btnOpenTemplateEditor.Name = "btnOpenTemplateEditor";
            this.btnOpenTemplateEditor.Progress = 0;
            this.btnOpenTemplateEditor.ProgressColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnOpenTemplateEditor.Size = new Size(28, 28);
            this.btnOpenTemplateEditor.TabIndex = 12;
            this.btnOpenTemplateEditor.UseVisualStyleBackColor = false;
            this.btnOpenTemplateEditor.UseMnemonic = false;
            this.btnOpenTemplateEditor.Click += new EventHandler(this.BtnOpenTemplateEditor_Click);
            // 
            // ConditionItem
            // 
            
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoSize = true;
            this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.BackColor = Color.FromArgb(((int)(((byte)(32)))), ((int)(((byte)(32)))), ((int)(((byte)(32)))));
            this.Controls.Add(this.panelEdit);
            this.Controls.Add(this.typeBox);
            this.Controls.Add(this.lblIf);
            this.Controls.Add(this.flowLayoutPanel1);
            this.Controls.Add(this.flowLayoutPanel2);
            this.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.ForeColor = Color.White;
            this.Margin = new Padding(0, 3, 0, 3);
            this.MaximumSize = new Size(850, 0);
            this.MinimumSize = new Size(850, 0);
            this.Name = "ConditionItem";
            this.Size = new Size(850, 146);
            this.Load += new EventHandler(this.ConditionItem_Load);
            ((ISupportInitialize)(this.btnAddAction)).EndInit();
            this.panelEdit.ResumeLayout(false);
            ((ISupportInitialize)(this.btnRemove)).EndInit();
            ((ISupportInitialize)(this.btnDown)).EndInit();
            ((ISupportInitialize)(this.btnUp)).EndInit();
            ((ISupportInitialize)(this.btnAddActionElse)).EndInit();
            this.flowLayoutPanel1.ResumeLayout(false);
            this.flowLayoutPanel1.PerformLayout();
            this.flowLayoutPanel2.ResumeLayout(false);
            this.addItemContextMenu.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        


        #endregion
        private Label lblIf;
        private RoundedComboBox typeBox;
        private RoundedComboBox methodBox;
        private RoundedComboBox valueToCompare;
        private FlowLayoutPanel actionsList;
        private PictureButton btnAddAction;
        private RoundedComboBox source;
        private FlowLayoutPanel panelEdit;
        private PictureButton btnRemove;
        private PictureButton btnDown;
        private PictureButton btnUp;
        private Label lblElse;
        private FlowLayoutPanel elseActionsList;
        private PictureButton btnAddActionElse;
        private FlowLayoutPanel flowLayoutPanel1;
        private FlowLayoutPanel flowLayoutPanel2;
        private ContextMenuStrip addItemContextMenu;
        private ToolStripMenuItem menuItemAction;
        private ToolStripMenuItem menuItemDelay;
        private RoundedTextBox template;
        private ButtonPrimary btnOpenTemplateEditor;
    }
}
