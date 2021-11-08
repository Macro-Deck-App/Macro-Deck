
using SuchByte.MacroDeck.Interfaces;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    partial class ConditionItem
    {
        /// <summary> 
        /// Erforderliche Designervariable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

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
            this.components = new System.ComponentModel.Container();
            this.lblIf = new System.Windows.Forms.Label();
            this.typeBox = new SuchByte.MacroDeck.GUI.CustomControls.RoundedComboBox();
            this.methodBox = new SuchByte.MacroDeck.GUI.CustomControls.RoundedComboBox();
            this.valueToCompare = new SuchByte.MacroDeck.GUI.CustomControls.RoundedComboBox();
            this.actionsList = new System.Windows.Forms.FlowLayoutPanel();
            this.btnAddAction = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.source = new SuchByte.MacroDeck.GUI.CustomControls.RoundedComboBox();
            this.panelEdit = new System.Windows.Forms.FlowLayoutPanel();
            this.btnRemove = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.btnDown = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.btnUp = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.lblElse = new System.Windows.Forms.Label();
            this.elseActionsList = new System.Windows.Forms.FlowLayoutPanel();
            this.btnAddActionElse = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.flowLayoutPanel1 = new System.Windows.Forms.FlowLayoutPanel();
            this.flowLayoutPanel2 = new System.Windows.Forms.FlowLayoutPanel();
            this.addItemContextMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.menuItemAction = new System.Windows.Forms.ToolStripMenuItem();
            this.menuItemDelay = new System.Windows.Forms.ToolStripMenuItem();
            ((System.ComponentModel.ISupportInitialize)(this.btnAddAction)).BeginInit();
            this.panelEdit.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.btnRemove)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnDown)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnUp)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnAddActionElse)).BeginInit();
            this.flowLayoutPanel1.SuspendLayout();
            this.flowLayoutPanel2.SuspendLayout();
            this.addItemContextMenu.SuspendLayout();
            this.SuspendLayout();
            // 
            // lblIf
            // 
            this.lblIf.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblIf.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblIf.Location = new System.Drawing.Point(9, 2);
            this.lblIf.Name = "lblIf";
            this.lblIf.Size = new System.Drawing.Size(77, 28);
            this.lblIf.TabIndex = 4;
            this.lblIf.Text = "If";
            this.lblIf.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // typeBox
            // 
            this.typeBox.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.typeBox.Cursor = System.Windows.Forms.Cursors.Hand;
            this.typeBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.typeBox.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.typeBox.Icon = null;
            this.typeBox.Location = new System.Drawing.Point(93, 2);
            this.typeBox.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.typeBox.Name = "typeBox";
            this.typeBox.Padding = new System.Windows.Forms.Padding(8, 2, 8, 2);
            this.typeBox.SelectedIndex = -1;
            this.typeBox.SelectedItem = null;
            this.typeBox.Size = new System.Drawing.Size(115, 28);
            this.typeBox.TabIndex = 5;
            this.typeBox.SelectedIndexChanged += new System.EventHandler(this.TypeBox_SelectedIndexChanged);
            this.typeBox.Load += new System.EventHandler(this.typeBox_Load);
            // 
            // methodBox
            // 
            this.methodBox.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.methodBox.Cursor = System.Windows.Forms.Cursors.Hand;
            this.methodBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.methodBox.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.methodBox.Icon = null;
            this.methodBox.Location = new System.Drawing.Point(291, 0);
            this.methodBox.Margin = new System.Windows.Forms.Padding(3, 0, 3, 0);
            this.methodBox.Name = "methodBox";
            this.methodBox.Padding = new System.Windows.Forms.Padding(8, 2, 8, 2);
            this.methodBox.SelectedIndex = -1;
            this.methodBox.SelectedItem = null;
            this.methodBox.Size = new System.Drawing.Size(97, 28);
            this.methodBox.TabIndex = 6;
            this.methodBox.SelectedIndexChanged += new System.EventHandler(this.MethodBox_SelectedIndexChanged);
            // 
            // valueToCompare
            // 
            this.valueToCompare.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.valueToCompare.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.valueToCompare.Cursor = System.Windows.Forms.Cursors.Hand;
            this.valueToCompare.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDown;
            this.valueToCompare.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.valueToCompare.Icon = null;
            this.valueToCompare.Location = new System.Drawing.Point(394, 0);
            this.valueToCompare.Margin = new System.Windows.Forms.Padding(3, 0, 3, 0);
            this.valueToCompare.Name = "valueToCompare";
            this.valueToCompare.Padding = new System.Windows.Forms.Padding(8, 2, 8, 2);
            this.valueToCompare.SelectedIndex = -1;
            this.valueToCompare.SelectedItem = null;
            this.valueToCompare.Size = new System.Drawing.Size(122, 28);
            this.valueToCompare.TabIndex = 7;
            this.valueToCompare.SelectedIndexChanged += new System.EventHandler(this.ValueToCompare_TextChanged);
            // 
            // actionsList
            // 
            this.actionsList.AutoSize = true;
            this.actionsList.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.actionsList.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.actionsList.Location = new System.Drawing.Point(0, 0);
            this.actionsList.Margin = new System.Windows.Forms.Padding(0);
            this.actionsList.MaximumSize = new System.Drawing.Size(840, 0);
            this.actionsList.MinimumSize = new System.Drawing.Size(840, 0);
            this.actionsList.Name = "actionsList";
            this.actionsList.Size = new System.Drawing.Size(840, 0);
            this.actionsList.TabIndex = 8;
            // 
            // btnAddAction
            // 
            this.btnAddAction.BackColor = System.Drawing.Color.Transparent;
            this.btnAddAction.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Create_Normal;
            this.btnAddAction.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnAddAction.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnAddAction.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnAddAction.ForeColor = System.Drawing.Color.White;
            this.btnAddAction.HoverImage = global::SuchByte.MacroDeck.Properties.Resources.Create_Hover;
            this.btnAddAction.Location = new System.Drawing.Point(3, 3);
            this.btnAddAction.Name = "btnAddAction";
            this.btnAddAction.Size = new System.Drawing.Size(27, 27);
            this.btnAddAction.TabIndex = 9;
            this.btnAddAction.TabStop = false;
            this.btnAddAction.Text = "+ Action";
            this.btnAddAction.Click += new System.EventHandler(this.BtnAddAction_Click);
            // 
            // source
            // 
            this.source.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.source.Cursor = System.Windows.Forms.Cursors.Hand;
            this.source.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.source.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.source.Icon = null;
            this.source.Location = new System.Drawing.Point(3, 0);
            this.source.Margin = new System.Windows.Forms.Padding(3, 0, 3, 0);
            this.source.Name = "source";
            this.source.Padding = new System.Windows.Forms.Padding(8, 2, 8, 2);
            this.source.SelectedIndex = -1;
            this.source.SelectedItem = null;
            this.source.Size = new System.Drawing.Size(282, 28);
            this.source.TabIndex = 10;
            this.source.SelectedIndexChanged += new System.EventHandler(this.Source_SelectedIndexChanged);
            // 
            // panelEdit
            // 
            this.panelEdit.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.panelEdit.Controls.Add(this.btnRemove);
            this.panelEdit.Controls.Add(this.btnDown);
            this.panelEdit.Controls.Add(this.btnUp);
            this.panelEdit.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            this.panelEdit.Location = new System.Drawing.Point(758, 2);
            this.panelEdit.Name = "panelEdit";
            this.panelEdit.Size = new System.Drawing.Size(89, 26);
            this.panelEdit.TabIndex = 11;
            // 
            // btnRemove
            // 
            this.btnRemove.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnRemove.BackColor = System.Drawing.Color.Transparent;
            this.btnRemove.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Delete_Normal;
            this.btnRemove.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnRemove.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnRemove.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnRemove.ForeColor = System.Drawing.Color.White;
            this.btnRemove.HoverImage = global::SuchByte.MacroDeck.Properties.Resources.Delete_Hover;
            this.btnRemove.Location = new System.Drawing.Point(62, 0);
            this.btnRemove.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.btnRemove.Name = "btnRemove";
            this.btnRemove.Size = new System.Drawing.Size(25, 25);
            this.btnRemove.TabIndex = 2;
            this.btnRemove.TabStop = false;
            this.btnRemove.Click += new System.EventHandler(this.BtnRemove_Click);
            // 
            // btnDown
            // 
            this.btnDown.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnDown.BackColor = System.Drawing.Color.Transparent;
            this.btnDown.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Arrow_Down_Normal;
            this.btnDown.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnDown.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnDown.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnDown.ForeColor = System.Drawing.Color.White;
            this.btnDown.HoverImage = global::SuchByte.MacroDeck.Properties.Resources.Arrow_Down_Hover;
            this.btnDown.Location = new System.Drawing.Point(33, 0);
            this.btnDown.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.btnDown.Name = "btnDown";
            this.btnDown.Size = new System.Drawing.Size(25, 25);
            this.btnDown.TabIndex = 4;
            this.btnDown.TabStop = false;
            this.btnDown.Click += new System.EventHandler(this.BtnDown_Click);
            // 
            // btnUp
            // 
            this.btnUp.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnUp.BackColor = System.Drawing.Color.Transparent;
            this.btnUp.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Arrow_Up_Normal;
            this.btnUp.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnUp.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnUp.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnUp.ForeColor = System.Drawing.Color.White;
            this.btnUp.HoverImage = global::SuchByte.MacroDeck.Properties.Resources.Arrow_Up_Hover;
            this.btnUp.Location = new System.Drawing.Point(4, 0);
            this.btnUp.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.btnUp.Name = "btnUp";
            this.btnUp.Size = new System.Drawing.Size(25, 25);
            this.btnUp.TabIndex = 5;
            this.btnUp.TabStop = false;
            this.btnUp.Click += new System.EventHandler(this.BtnUp_Click);
            // 
            // lblElse
            // 
            this.lblElse.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.lblElse.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblElse.Location = new System.Drawing.Point(3, 33);
            this.lblElse.Name = "lblElse";
            this.lblElse.Size = new System.Drawing.Size(148, 33);
            this.lblElse.TabIndex = 12;
            this.lblElse.Text = "Else";
            this.lblElse.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // elseActionsList
            // 
            this.elseActionsList.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.elseActionsList.AutoSize = true;
            this.elseActionsList.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.elseActionsList.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.elseActionsList.Location = new System.Drawing.Point(0, 66);
            this.elseActionsList.Margin = new System.Windows.Forms.Padding(0);
            this.elseActionsList.MaximumSize = new System.Drawing.Size(840, 0);
            this.elseActionsList.MinimumSize = new System.Drawing.Size(840, 0);
            this.elseActionsList.Name = "elseActionsList";
            this.elseActionsList.Size = new System.Drawing.Size(840, 0);
            this.elseActionsList.TabIndex = 9;
            // 
            // btnAddActionElse
            // 
            this.btnAddActionElse.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnAddActionElse.BackColor = System.Drawing.Color.Transparent;
            this.btnAddActionElse.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Create_Normal;
            this.btnAddActionElse.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnAddActionElse.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnAddActionElse.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnAddActionElse.ForeColor = System.Drawing.Color.White;
            this.btnAddActionElse.HoverImage = global::SuchByte.MacroDeck.Properties.Resources.Create_Hover;
            this.btnAddActionElse.Location = new System.Drawing.Point(3, 69);
            this.btnAddActionElse.Name = "btnAddActionElse";
            this.btnAddActionElse.Size = new System.Drawing.Size(27, 27);
            this.btnAddActionElse.TabIndex = 13;
            this.btnAddActionElse.TabStop = false;
            this.btnAddActionElse.Click += new System.EventHandler(this.BtnAddActionElse_Click);
            // 
            // flowLayoutPanel1
            // 
            this.flowLayoutPanel1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.flowLayoutPanel1.AutoSize = true;
            this.flowLayoutPanel1.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.flowLayoutPanel1.BackColor = System.Drawing.Color.Transparent;
            this.flowLayoutPanel1.Controls.Add(this.actionsList);
            this.flowLayoutPanel1.Controls.Add(this.btnAddAction);
            this.flowLayoutPanel1.Controls.Add(this.lblElse);
            this.flowLayoutPanel1.Controls.Add(this.elseActionsList);
            this.flowLayoutPanel1.Controls.Add(this.btnAddActionElse);
            this.flowLayoutPanel1.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.flowLayoutPanel1.Location = new System.Drawing.Point(9, 37);
            this.flowLayoutPanel1.Margin = new System.Windows.Forms.Padding(0);
            this.flowLayoutPanel1.MaximumSize = new System.Drawing.Size(840, 0);
            this.flowLayoutPanel1.MinimumSize = new System.Drawing.Size(840, 0);
            this.flowLayoutPanel1.Name = "flowLayoutPanel1";
            this.flowLayoutPanel1.Padding = new System.Windows.Forms.Padding(0, 0, 0, 10);
            this.flowLayoutPanel1.Size = new System.Drawing.Size(840, 109);
            this.flowLayoutPanel1.TabIndex = 14;
            // 
            // flowLayoutPanel2
            // 
            this.flowLayoutPanel2.Controls.Add(this.source);
            this.flowLayoutPanel2.Controls.Add(this.methodBox);
            this.flowLayoutPanel2.Controls.Add(this.valueToCompare);
            this.flowLayoutPanel2.Location = new System.Drawing.Point(215, 2);
            this.flowLayoutPanel2.Name = "flowLayoutPanel2";
            this.flowLayoutPanel2.Size = new System.Drawing.Size(522, 28);
            this.flowLayoutPanel2.TabIndex = 15;
            // 
            // addItemContextMenu
            // 
            this.addItemContextMenu.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.addItemContextMenu.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.addItemContextMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menuItemAction,
            this.menuItemDelay});
            this.addItemContextMenu.LayoutStyle = System.Windows.Forms.ToolStripLayoutStyle.Table;
            this.addItemContextMenu.Name = "addItemContextMenu";
            this.addItemContextMenu.ShowImageMargin = false;
            this.addItemContextMenu.Size = new System.Drawing.Size(107, 60);
            // 
            // menuItemAction
            // 
            this.menuItemAction.ForeColor = System.Drawing.Color.White;
            this.menuItemAction.Name = "menuItemAction";
            this.menuItemAction.Size = new System.Drawing.Size(106, 28);
            this.menuItemAction.Text = "Action";
            this.menuItemAction.Click += new System.EventHandler(this.MenuItemAction_Click);
            // 
            // menuItemDelay
            // 
            this.menuItemDelay.ForeColor = System.Drawing.Color.White;
            this.menuItemDelay.Name = "menuItemDelay";
            this.menuItemDelay.Size = new System.Drawing.Size(106, 28);
            this.menuItemDelay.Text = "Delay";
            this.menuItemDelay.Click += new System.EventHandler(this.MenuItemDelay_Click);
            // 
            // ConditionItem
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSize = true;
            this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(32)))), ((int)(((byte)(32)))), ((int)(((byte)(32)))));
            this.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.Controls.Add(this.panelEdit);
            this.Controls.Add(this.typeBox);
            this.Controls.Add(this.lblIf);
            this.Controls.Add(this.flowLayoutPanel1);
            this.Controls.Add(this.flowLayoutPanel2);
            this.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.ForeColor = System.Drawing.Color.White;
            this.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            this.Name = "ConditionItem";
            this.Size = new System.Drawing.Size(850, 146);
            this.Load += new System.EventHandler(this.ConditionItem_Load);
            ((System.ComponentModel.ISupportInitialize)(this.btnAddAction)).EndInit();
            this.panelEdit.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.btnRemove)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnDown)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnUp)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnAddActionElse)).EndInit();
            this.flowLayoutPanel1.ResumeLayout(false);
            this.flowLayoutPanel1.PerformLayout();
            this.flowLayoutPanel2.ResumeLayout(false);
            this.addItemContextMenu.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        private void Source_SelectedIndexChanged1(object sender, System.EventArgs e)
        {
            throw new System.NotImplementedException();
        }

        #endregion
        private System.Windows.Forms.Label lblIf;
        private RoundedComboBox typeBox;
        private RoundedComboBox methodBox;
        private RoundedComboBox valueToCompare;
        private System.Windows.Forms.FlowLayoutPanel actionsList;
        private PictureButton btnAddAction;
        private RoundedComboBox source;
        private System.Windows.Forms.FlowLayoutPanel panelEdit;
        private PictureButton btnRemove;
        private PictureButton btnDown;
        private PictureButton btnUp;
        private System.Windows.Forms.Label lblElse;
        private System.Windows.Forms.FlowLayoutPanel elseActionsList;
        private PictureButton btnAddActionElse;
        private System.Windows.Forms.FlowLayoutPanel flowLayoutPanel1;
        private System.Windows.Forms.FlowLayoutPanel flowLayoutPanel2;
        private System.Windows.Forms.ContextMenuStrip addItemContextMenu;
        private System.Windows.Forms.ToolStripMenuItem menuItemAction;
        private System.Windows.Forms.ToolStripMenuItem menuItemDelay;
    }
}
