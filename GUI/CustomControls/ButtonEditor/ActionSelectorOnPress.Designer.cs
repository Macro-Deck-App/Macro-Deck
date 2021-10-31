
using SuchByte.MacroDeck.Interfaces;

namespace SuchByte.MacroDeck.GUI.CustomControls.ButtonEditor
{
    partial class ActionSelectorOnPress
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

            foreach (IActionConditionItem actionItem in this.actionsOnPress.Controls)
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
            this.actionsOnPress = new System.Windows.Forms.FlowLayoutPanel();
            this.addItemContextMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.menuItemAction = new System.Windows.Forms.ToolStripMenuItem();
            this.menuItemCondition = new System.Windows.Forms.ToolStripMenuItem();
            this.menuItemDelay = new System.Windows.Forms.ToolStripMenuItem();
            this.btnAdd = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.flowLayoutPanel1 = new System.Windows.Forms.FlowLayoutPanel();
            this.addItemContextMenu.SuspendLayout();
            this.flowLayoutPanel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // actionsOnPress
            // 
            this.actionsOnPress.AutoScroll = true;
            this.actionsOnPress.AutoSize = true;
            this.actionsOnPress.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.actionsOnPress.Location = new System.Drawing.Point(3, 3);
            this.actionsOnPress.MaximumSize = new System.Drawing.Size(925, 380);
            this.actionsOnPress.MinimumSize = new System.Drawing.Size(925, 1);
            this.actionsOnPress.Name = "actionsOnPress";
            this.actionsOnPress.Size = new System.Drawing.Size(925, 1);
            this.actionsOnPress.TabIndex = 2;
            // 
            // addItemContextMenu
            // 
            this.addItemContextMenu.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.addItemContextMenu.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.addItemContextMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menuItemAction,
            this.menuItemCondition,
            this.menuItemDelay});
            this.addItemContextMenu.LayoutStyle = System.Windows.Forms.ToolStripLayoutStyle.Table;
            this.addItemContextMenu.Name = "addItemContextMenu";
            this.addItemContextMenu.ShowImageMargin = false;
            this.addItemContextMenu.Size = new System.Drawing.Size(156, 110);
            // 
            // menuItemAction
            // 
            this.menuItemAction.ForeColor = System.Drawing.Color.White;
            this.menuItemAction.Name = "menuItemAction";
            this.menuItemAction.Size = new System.Drawing.Size(155, 28);
            this.menuItemAction.Text = "Action";
            this.menuItemAction.Click += new System.EventHandler(this.MenuItemAction_Click);
            // 
            // menuItemCondition
            // 
            this.menuItemCondition.ForeColor = System.Drawing.Color.White;
            this.menuItemCondition.Name = "menuItemCondition";
            this.menuItemCondition.Size = new System.Drawing.Size(155, 28);
            this.menuItemCondition.Text = "Condition";
            this.menuItemCondition.Click += new System.EventHandler(this.MenuItemCondition_Click);
            // 
            // menuItemDelay
            // 
            this.menuItemDelay.ForeColor = System.Drawing.Color.White;
            this.menuItemDelay.Name = "menuItemDelay";
            this.menuItemDelay.Size = new System.Drawing.Size(155, 28);
            this.menuItemDelay.Text = "Delay";
            this.menuItemDelay.Click += new System.EventHandler(this.DelayToolStripMenuItem_Click);
            // 
            // btnAdd
            // 
            this.btnAdd.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnAdd.BorderRadius = 8;
            this.btnAdd.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnAdd.FlatAppearance.BorderSize = 0;
            this.btnAdd.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAdd.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnAdd.ForeColor = System.Drawing.Color.White;
            this.btnAdd.Location = new System.Drawing.Point(3, 10);
            this.btnAdd.Name = "btnAdd";
            this.btnAdd.Size = new System.Drawing.Size(165, 30);
            this.btnAdd.TabIndex = 7;
            this.btnAdd.Text = "+";
            this.btnAdd.UseVisualStyleBackColor = false;
            this.btnAdd.Click += new System.EventHandler(this.BtnAdd_Click);
            // 
            // flowLayoutPanel1
            // 
            this.flowLayoutPanel1.Controls.Add(this.actionsOnPress);
            this.flowLayoutPanel1.Controls.Add(this.btnAdd);
            this.flowLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.flowLayoutPanel1.Location = new System.Drawing.Point(0, 0);
            this.flowLayoutPanel1.Name = "flowLayoutPanel1";
            this.flowLayoutPanel1.Size = new System.Drawing.Size(931, 427);
            this.flowLayoutPanel1.TabIndex = 8;
            // 
            // ActionSelectorOnPress
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(45)))));
            this.Controls.Add(this.flowLayoutPanel1);
            this.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.Name = "ActionSelectorOnPress";
            this.Size = new System.Drawing.Size(931, 427);
            this.Load += new System.EventHandler(this.ActionSelectorOnPress_Load);
            this.addItemContextMenu.ResumeLayout(false);
            this.flowLayoutPanel1.ResumeLayout(false);
            this.flowLayoutPanel1.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.FlowLayoutPanel actionsOnPress;
        private System.Windows.Forms.ContextMenuStrip addItemContextMenu;
        private System.Windows.Forms.ToolStripMenuItem menuItemAction;
        private System.Windows.Forms.ToolStripMenuItem menuItemCondition;
        private ButtonPrimary btnAdd;
        private System.Windows.Forms.FlowLayoutPanel flowLayoutPanel1;
        private System.Windows.Forms.ToolStripMenuItem menuItemDelay;
    }
}
