
using SuchByte.MacroDeck.Interfaces;

namespace SuchByte.MacroDeck.GUI.CustomControls.ButtonEditor
{
    partial class EventItem
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
            this.eventBox = new System.Windows.Forms.ComboBox();
            this.lblTrigger = new System.Windows.Forms.Label();
            this.panelEdit = new System.Windows.Forms.FlowLayoutPanel();
            this.btnRemove = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.addItemContextMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.menuItemAction = new System.Windows.Forms.ToolStripMenuItem();
            this.menuItemCondition = new System.Windows.Forms.ToolStripMenuItem();
            this.btnAdd = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.actionsList = new System.Windows.Forms.FlowLayoutPanel();
            this.panelEdit.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.btnRemove)).BeginInit();
            this.addItemContextMenu.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.btnAdd)).BeginInit();
            this.SuspendLayout();
            // 
            // eventBox
            // 
            this.eventBox.AutoCompleteMode = System.Windows.Forms.AutoCompleteMode.SuggestAppend;
            this.eventBox.AutoCompleteSource = System.Windows.Forms.AutoCompleteSource.ListItems;
            this.eventBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.eventBox.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.eventBox.FormattingEnabled = true;
            this.eventBox.Location = new System.Drawing.Point(114, 8);
            this.eventBox.Name = "eventBox";
            this.eventBox.Size = new System.Drawing.Size(444, 31);
            this.eventBox.TabIndex = 7;
            this.eventBox.SelectedIndexChanged += new System.EventHandler(this.EventBox_SelectedIndexChanged);
            // 
            // lblTrigger
            // 
            this.lblTrigger.AutoSize = true;
            this.lblTrigger.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblTrigger.Location = new System.Drawing.Point(9, 11);
            this.lblTrigger.Name = "lblTrigger";
            this.lblTrigger.Size = new System.Drawing.Size(71, 23);
            this.lblTrigger.TabIndex = 6;
            this.lblTrigger.Text = "Trigger";
            // 
            // panelEdit
            // 
            this.panelEdit.Controls.Add(this.btnRemove);
            this.panelEdit.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            this.panelEdit.Location = new System.Drawing.Point(848, 8);
            this.panelEdit.Name = "panelEdit";
            this.panelEdit.Size = new System.Drawing.Size(30, 26);
            this.panelEdit.TabIndex = 12;
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
            this.btnRemove.Location = new System.Drawing.Point(3, 0);
            this.btnRemove.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.btnRemove.Name = "btnRemove";
            this.btnRemove.Size = new System.Drawing.Size(25, 25);
            this.btnRemove.TabIndex = 2;
            this.btnRemove.TabStop = false;
            this.btnRemove.Click += new System.EventHandler(this.btnRemove_Click);
            // 
            // addItemContextMenu
            // 
            this.addItemContextMenu.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.addItemContextMenu.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.addItemContextMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menuItemAction,
            this.menuItemCondition});
            this.addItemContextMenu.LayoutStyle = System.Windows.Forms.ToolStripLayoutStyle.Table;
            this.addItemContextMenu.Name = "addItemContextMenu";
            this.addItemContextMenu.ShowImageMargin = false;
            this.addItemContextMenu.Size = new System.Drawing.Size(134, 60);
            // 
            // menuItemAction
            // 
            this.menuItemAction.ForeColor = System.Drawing.Color.White;
            this.menuItemAction.Name = "menuItemAction";
            this.menuItemAction.Size = new System.Drawing.Size(133, 28);
            this.menuItemAction.Text = "Action";
            this.menuItemAction.Click += new System.EventHandler(this.menuItemAction_Click);
            // 
            // menuItemCondition
            // 
            this.menuItemCondition.ForeColor = System.Drawing.Color.White;
            this.menuItemCondition.Name = "menuItemCondition";
            this.menuItemCondition.Size = new System.Drawing.Size(133, 28);
            this.menuItemCondition.Text = "Condition";
            this.menuItemCondition.Click += new System.EventHandler(this.MenuItemCondition_Click);
            // 
            // btnAdd
            // 
            this.btnAdd.BackColor = System.Drawing.Color.Transparent;
            this.btnAdd.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Create_Normal;
            this.btnAdd.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnAdd.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnAdd.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnAdd.ForeColor = System.Drawing.Color.White;
            this.btnAdd.HoverImage = global::SuchByte.MacroDeck.Properties.Resources.Create_Hover;
            this.btnAdd.Location = new System.Drawing.Point(12, 45);
            this.btnAdd.Name = "btnAdd";
            this.btnAdd.Size = new System.Drawing.Size(27, 27);
            this.btnAdd.TabIndex = 9;
            this.btnAdd.TabStop = false;
            this.btnAdd.Click += new System.EventHandler(this.BtnAdd_Click);
            // 
            // actionsList
            // 
            this.actionsList.AutoSize = true;
            this.actionsList.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.actionsList.Location = new System.Drawing.Point(9, 75);
            this.actionsList.Margin = new System.Windows.Forms.Padding(0);
            this.actionsList.MaximumSize = new System.Drawing.Size(885, 0);
            this.actionsList.MinimumSize = new System.Drawing.Size(893, 1);
            this.actionsList.Name = "actionsList";
            this.actionsList.Padding = new System.Windows.Forms.Padding(0, 0, 0, 10);
            this.actionsList.Size = new System.Drawing.Size(893, 10);
            this.actionsList.TabIndex = 8;
            // 
            // EventItem
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSize = true;
            this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(32)))), ((int)(((byte)(32)))), ((int)(((byte)(32)))));
            this.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.Controls.Add(this.actionsList);
            this.Controls.Add(this.btnAdd);
            this.Controls.Add(this.panelEdit);
            this.Controls.Add(this.eventBox);
            this.Controls.Add(this.lblTrigger);
            this.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.ForeColor = System.Drawing.Color.White;
            this.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            this.MinimumSize = new System.Drawing.Size(903, 84);
            this.Name = "EventItem";
            this.Size = new System.Drawing.Size(902, 85);
            this.Load += new System.EventHandler(this.EventItem_Load);
            this.panelEdit.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.btnRemove)).EndInit();
            this.addItemContextMenu.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.btnAdd)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ComboBox eventBox;
        private System.Windows.Forms.Label lblTrigger;
        private System.Windows.Forms.FlowLayoutPanel panelEdit;
        private PictureButton btnRemove;
        private System.Windows.Forms.ContextMenuStrip addItemContextMenu;
        private System.Windows.Forms.ToolStripMenuItem menuItemAction;
        private System.Windows.Forms.ToolStripMenuItem menuItemCondition;
        private PictureButton btnAdd;
        private System.Windows.Forms.FlowLayoutPanel actionsList;
    }
}
