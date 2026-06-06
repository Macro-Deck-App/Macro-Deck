
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.Interfaces;
using SuchByte.MacroDeck.Properties;

namespace SuchByte.MacroDeck.GUI.CustomControls.ButtonEditor
{
    partial class EventItem
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
            this.eventBox = new RoundedComboBox();
            this.lblTrigger = new Label();
            this.panelEdit = new FlowLayoutPanel();
            this.btnRemove = new PictureButton();
            this.addItemContextMenu = new ContextMenuStrip(this.components);
            this.menuItemAction = new ToolStripMenuItem();
            this.menuItemCondition = new ToolStripMenuItem();
            this.menuItemDelay = new ToolStripMenuItem();
            this.btnAdd = new PictureButton();
            this.actionsList = new FlowLayoutPanel();
            this.flowLayoutPanel1 = new FlowLayoutPanel();
            this.eventParameter = new RoundedComboBox();
            this.panelEdit.SuspendLayout();
            ((ISupportInitialize)(this.btnRemove)).BeginInit();
            this.addItemContextMenu.SuspendLayout();
            ((ISupportInitialize)(this.btnAdd)).BeginInit();
            this.flowLayoutPanel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // eventBox
            // 
            this.eventBox.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.eventBox.Cursor = Cursors.Hand;
            this.eventBox.DropDownStyle = ComboBoxStyle.DropDownList;
            this.eventBox.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.eventBox.Icon = null;
            this.eventBox.Location = new Point(110, 2);
            this.eventBox.Margin = new Padding(4, 5, 4, 5);
            this.eventBox.Name = "eventBox";
            this.eventBox.Padding = new Padding(8, 2, 8, 2);
            this.eventBox.SelectedIndex = -1;
            this.eventBox.SelectedItem = null;
            this.eventBox.Size = new Size(309, 28);
            this.eventBox.TabIndex = 7;
            this.eventBox.SelectedIndexChanged += new EventHandler(this.EventBox_SelectedIndexChanged);
            // 
            // lblTrigger
            // 
            this.lblTrigger.Font = new Font("Tahoma", 12F, FontStyle.Regular, GraphicsUnit.Point);
            this.lblTrigger.Location = new Point(9, 2);
            this.lblTrigger.Name = "lblTrigger";
            this.lblTrigger.Size = new Size(91, 28);
            this.lblTrigger.TabIndex = 6;
            this.lblTrigger.Text = "Trigger";
            this.lblTrigger.TextAlign = ContentAlignment.MiddleLeft;
            this.lblTrigger.UseMnemonic = false;
            // 
            // panelEdit
            // 
            this.panelEdit.Controls.Add(this.btnRemove);
            this.panelEdit.FlowDirection = FlowDirection.RightToLeft;
            this.panelEdit.Location = new Point(815, 2);
            this.panelEdit.Name = "panelEdit";
            this.panelEdit.Size = new Size(30, 26);
            this.panelEdit.TabIndex = 12;
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
            this.btnRemove.Location = new Point(3, 0);
            this.btnRemove.Margin = new Padding(2, 0, 2, 0);
            this.btnRemove.Name = "btnRemove";
            this.btnRemove.Size = new Size(25, 25);
            this.btnRemove.TabIndex = 2;
            this.btnRemove.TabStop = false;
            this.btnRemove.Click += new EventHandler(this.BtnRemove_Click);
            // 
            // addItemContextMenu
            // 
            this.addItemContextMenu.BackColor = Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.addItemContextMenu.Font = new Font("Tahoma", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.addItemContextMenu.Items.AddRange(new ToolStripItem[] {
            this.menuItemAction,
            this.menuItemCondition,
            this.menuItemDelay});
            this.addItemContextMenu.LayoutStyle = ToolStripLayoutStyle.Table;
            this.addItemContextMenu.Name = "addItemContextMenu";
            this.addItemContextMenu.ShowImageMargin = false;
            this.addItemContextMenu.Size = new Size(134, 88);
            // 
            // menuItemAction
            // 
            this.menuItemAction.ForeColor = Color.White;
            this.menuItemAction.Name = "menuItemAction";
            this.menuItemAction.Size = new Size(133, 28);
            this.menuItemAction.Text = "Action";
            this.menuItemAction.Click += new EventHandler(this.MenuItemAction_Click);
            // 
            // menuItemCondition
            // 
            this.menuItemCondition.ForeColor = Color.White;
            this.menuItemCondition.Name = "menuItemCondition";
            this.menuItemCondition.Size = new Size(133, 28);
            this.menuItemCondition.Text = "Condition";
            this.menuItemCondition.Click += new EventHandler(this.MenuItemCondition_Click);
            // 
            // menuItemDelay
            // 
            this.menuItemDelay.ForeColor = Color.White;
            this.menuItemDelay.Name = "menuItemDelay";
            this.menuItemDelay.Size = new Size(133, 28);
            this.menuItemDelay.Text = "Delay";
            this.menuItemDelay.Click += new EventHandler(this.MenuItemDelay_Click);
            // 
            // btnAdd
            // 
            this.btnAdd.Anchor = AnchorStyles.Left;
            this.btnAdd.BackColor = Color.Transparent;
            this.btnAdd.BackgroundImage = Resources.Create_Normal;
            this.btnAdd.BackgroundImageLayout = ImageLayout.Stretch;
            this.btnAdd.Cursor = Cursors.Hand;
            this.btnAdd.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnAdd.ForeColor = Color.White;
            this.btnAdd.HoverImage = Resources.Create_Hover;
            this.btnAdd.Location = new Point(3, 13);
            this.btnAdd.Name = "btnAdd";
            this.btnAdd.Size = new Size(27, 27);
            this.btnAdd.TabIndex = 9;
            this.btnAdd.TabStop = false;
            this.btnAdd.Click += new EventHandler(this.BtnAdd_Click);
            // 
            // actionsList
            // 
            this.actionsList.AutoSize = true;
            this.actionsList.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.actionsList.Location = new Point(0, 0);
            this.actionsList.Margin = new Padding(0);
            this.actionsList.MaximumSize = new Size(840, 0);
            this.actionsList.MinimumSize = new Size(840, 1);
            this.actionsList.Name = "actionsList";
            this.actionsList.Padding = new Padding(0, 0, 0, 10);
            this.actionsList.Size = new Size(840, 10);
            this.actionsList.TabIndex = 8;
            // 
            // flowLayoutPanel1
            // 
            this.flowLayoutPanel1.AutoSize = true;
            this.flowLayoutPanel1.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.flowLayoutPanel1.Controls.Add(this.actionsList);
            this.flowLayoutPanel1.Controls.Add(this.btnAdd);
            this.flowLayoutPanel1.FlowDirection = FlowDirection.TopDown;
            this.flowLayoutPanel1.Location = new Point(10, 37);
            this.flowLayoutPanel1.Margin = new Padding(0);
            this.flowLayoutPanel1.Name = "flowLayoutPanel1";
            this.flowLayoutPanel1.Size = new Size(840, 43);
            this.flowLayoutPanel1.TabIndex = 13;
            // 
            // eventParameter
            // 
            this.eventParameter.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.eventParameter.Cursor = Cursors.Hand;
            this.eventParameter.DropDownStyle = ComboBoxStyle.DropDownList;
            this.eventParameter.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.eventParameter.Icon = null;
            this.eventParameter.Location = new Point(427, 2);
            this.eventParameter.Margin = new Padding(4, 5, 4, 5);
            this.eventParameter.Name = "eventParameter";
            this.eventParameter.Padding = new Padding(8, 2, 8, 2);
            this.eventParameter.SelectedIndex = -1;
            this.eventParameter.SelectedItem = null;
            this.eventParameter.Size = new Size(309, 28);
            this.eventParameter.TabIndex = 14;
            // 
            // EventItem
            // 
            
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoSize = true;
            this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.BackColor = Color.FromArgb(((int)(((byte)(32)))), ((int)(((byte)(32)))), ((int)(((byte)(32)))));
            this.Controls.Add(this.eventParameter);
            this.Controls.Add(this.flowLayoutPanel1);
            this.Controls.Add(this.panelEdit);
            this.Controls.Add(this.eventBox);
            this.Controls.Add(this.lblTrigger);
            this.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.ForeColor = Color.White;
            this.Margin = new Padding(0, 3, 0, 3);
            this.MaximumSize = new Size(850, 0);
            this.MinimumSize = new Size(850, 84);
            this.Name = "EventItem";
            this.Size = new Size(850, 84);
            this.Load += new EventHandler(this.EventItem_Load);
            this.panelEdit.ResumeLayout(false);
            ((ISupportInitialize)(this.btnRemove)).EndInit();
            this.addItemContextMenu.ResumeLayout(false);
            ((ISupportInitialize)(this.btnAdd)).EndInit();
            this.flowLayoutPanel1.ResumeLayout(false);
            this.flowLayoutPanel1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private RoundedComboBox eventBox;
        private Label lblTrigger;
        private FlowLayoutPanel panelEdit;
        private PictureButton btnRemove;
        private ContextMenuStrip addItemContextMenu;
        private ToolStripMenuItem menuItemAction;
        private ToolStripMenuItem menuItemCondition;
        private ToolStripMenuItem menuItemDelay;
        private PictureButton btnAdd;
        private FlowLayoutPanel actionsList;
        private FlowLayoutPanel flowLayoutPanel1;
        private RoundedComboBox eventParameter;
    }
}
