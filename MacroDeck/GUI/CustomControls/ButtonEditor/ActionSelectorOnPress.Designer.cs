
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.Interfaces;

namespace SuchByte.MacroDeck.GUI.CustomControls.ButtonEditor
{
    partial class ActionSelectorOnPress
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
            this.components = new Container();
            this.actionsOnPress = new FlowLayoutPanel();
            this.addItemContextMenu = new ContextMenuStrip(this.components);
            this.menuItemAction = new ToolStripMenuItem();
            this.menuItemCondition = new ToolStripMenuItem();
            this.menuItemDelay = new ToolStripMenuItem();
            this.btnAdd = new ButtonPrimary();
            this.flowLayoutPanel1 = new FlowLayoutPanel();
            this.addItemContextMenu.SuspendLayout();
            this.flowLayoutPanel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // actionsOnPress
            // 
            this.actionsOnPress.AutoScroll = true;
            this.actionsOnPress.AutoSize = true;
            this.actionsOnPress.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.actionsOnPress.Location = new Point(3, 3);
            this.actionsOnPress.Margin = new Padding(0);
            this.actionsOnPress.MaximumSize = new Size(869, 450);
            this.actionsOnPress.MinimumSize = new Size(869, 1);
            this.actionsOnPress.Name = "actionsOnPress";
            this.actionsOnPress.Size = new Size(869, 1);
            this.actionsOnPress.TabIndex = 2;
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
            this.menuItemDelay.Click += new EventHandler(this.DelayToolStripMenuItem_Click);
            // 
            // btnAdd
            // 
            this.btnAdd.BorderRadius = 8;
            this.btnAdd.Cursor = Cursors.Hand;
            this.btnAdd.FlatAppearance.BorderSize = 0;
            this.btnAdd.FlatStyle = FlatStyle.Flat;
            this.btnAdd.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnAdd.ForeColor = Color.White;
            this.btnAdd.HoverColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnAdd.Icon = null;
            this.btnAdd.Location = new Point(6, 7);
            this.btnAdd.Name = "btnAdd";
            this.btnAdd.Progress = 0;
            this.btnAdd.ProgressColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnAdd.Size = new Size(165, 30);
            this.btnAdd.TabIndex = 7;
            this.btnAdd.Text = "+";
            this.btnAdd.UseMnemonic = false;
            this.btnAdd.UseVisualStyleBackColor = false;
            this.btnAdd.UseWindowsAccentColor = true;
            this.btnAdd.Click += new EventHandler(this.BtnAdd_Click);
            // 
            // flowLayoutPanel1
            // 
            this.flowLayoutPanel1.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.flowLayoutPanel1.Controls.Add(this.actionsOnPress);
            this.flowLayoutPanel1.Controls.Add(this.btnAdd);
            this.flowLayoutPanel1.Dock = DockStyle.Fill;
            this.flowLayoutPanel1.Location = new Point(5, 5);
            this.flowLayoutPanel1.Name = "flowLayoutPanel1";
            this.flowLayoutPanel1.Padding = new Padding(3);
            this.flowLayoutPanel1.Size = new Size(866, 512);
            this.flowLayoutPanel1.TabIndex = 8;
            // 
            // ActionSelectorOnPress
            // 
            this.AutoScaleDimensions = new SizeF(96F, 96F);
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.Controls.Add(this.flowLayoutPanel1);
            this.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.Name = "ActionSelectorOnPress";
            this.Size = new Size(876, 522);
            this.Load += new EventHandler(this.ActionSelectorOnPress_Load);
            this.addItemContextMenu.ResumeLayout(false);
            this.flowLayoutPanel1.ResumeLayout(false);
            this.flowLayoutPanel1.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion
        private FlowLayoutPanel actionsOnPress;
        private ContextMenuStrip addItemContextMenu;
        private ToolStripMenuItem menuItemAction;
        private ToolStripMenuItem menuItemCondition;
        private ButtonPrimary btnAdd;
        private FlowLayoutPanel flowLayoutPanel1;
        private ToolStripMenuItem menuItemDelay;
    }
}
