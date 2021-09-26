
using SuchByte.MacroDeck.Interfaces;

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
            this.label1 = new System.Windows.Forms.Label();
            this.typeBox = new System.Windows.Forms.ComboBox();
            this.methodBox = new System.Windows.Forms.ComboBox();
            this.valueToCompare = new System.Windows.Forms.TextBox();
            this.actionsList = new System.Windows.Forms.FlowLayoutPanel();
            this.btnAddAction = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.source = new System.Windows.Forms.ComboBox();
            this.panelEdit = new System.Windows.Forms.FlowLayoutPanel();
            this.btnRemove = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.btnDown = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.btnUp = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.label2 = new System.Windows.Forms.Label();
            this.elseActionsList = new System.Windows.Forms.FlowLayoutPanel();
            this.btnAddActionElse = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.flowLayoutPanel1 = new System.Windows.Forms.FlowLayoutPanel();
            this.flowLayoutPanel2 = new System.Windows.Forms.FlowLayoutPanel();
            ((System.ComponentModel.ISupportInitialize)(this.btnAddAction)).BeginInit();
            this.panelEdit.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.btnRemove)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnDown)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnUp)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnAddActionElse)).BeginInit();
            this.flowLayoutPanel1.SuspendLayout();
            this.flowLayoutPanel2.SuspendLayout();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.label1.Location = new System.Drawing.Point(9, 11);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(23, 23);
            this.label1.TabIndex = 4;
            this.label1.Text = "If";
            // 
            // typeBox
            // 
            this.typeBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.typeBox.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.typeBox.FormattingEnabled = true;
            this.typeBox.Items.AddRange(new object[] {
            "Variable",
            "Button_State"});
            this.typeBox.Location = new System.Drawing.Point(35, 7);
            this.typeBox.Name = "typeBox";
            this.typeBox.Size = new System.Drawing.Size(148, 31);
            this.typeBox.TabIndex = 5;
            this.typeBox.SelectedIndexChanged += new System.EventHandler(this.typeBox_SelectedIndexChanged);
            // 
            // methodBox
            // 
            this.methodBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.methodBox.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.methodBox.FormattingEnabled = true;
            this.methodBox.Items.AddRange(new object[] {
            "Equals",
            "Not",
            "Bigger",
            "Smaller"});
            this.methodBox.Location = new System.Drawing.Point(407, 0);
            this.methodBox.Margin = new System.Windows.Forms.Padding(3, 0, 3, 0);
            this.methodBox.Name = "methodBox";
            this.methodBox.Size = new System.Drawing.Size(81, 31);
            this.methodBox.TabIndex = 6;
            this.methodBox.SelectedIndexChanged += new System.EventHandler(this.methodBox_SelectedIndexChanged);
            // 
            // valueToCompare
            // 
            this.valueToCompare.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.valueToCompare.AutoCompleteCustomSource.AddRange(new string[] {
            "On",
            "Off",
            "True",
            "False"});
            this.valueToCompare.AutoCompleteMode = System.Windows.Forms.AutoCompleteMode.Suggest;
            this.valueToCompare.AutoCompleteSource = System.Windows.Forms.AutoCompleteSource.CustomSource;
            this.valueToCompare.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.valueToCompare.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.valueToCompare.Location = new System.Drawing.Point(494, 0);
            this.valueToCompare.Margin = new System.Windows.Forms.Padding(3, 0, 3, 0);
            this.valueToCompare.Name = "valueToCompare";
            this.valueToCompare.Size = new System.Drawing.Size(95, 30);
            this.valueToCompare.TabIndex = 7;
            this.valueToCompare.TextChanged += new System.EventHandler(this.valueToCompare_TextChanged);
            // 
            // actionsList
            // 
            this.actionsList.AutoSize = true;
            this.actionsList.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.actionsList.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.actionsList.Location = new System.Drawing.Point(0, 33);
            this.actionsList.Margin = new System.Windows.Forms.Padding(0);
            this.actionsList.MaximumSize = new System.Drawing.Size(883, 0);
            this.actionsList.MinimumSize = new System.Drawing.Size(883, 0);
            this.actionsList.Name = "actionsList";
            this.actionsList.Size = new System.Drawing.Size(883, 0);
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
            this.source.AutoCompleteMode = System.Windows.Forms.AutoCompleteMode.SuggestAppend;
            this.source.AutoCompleteSource = System.Windows.Forms.AutoCompleteSource.ListItems;
            this.source.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.source.FormattingEnabled = true;
            this.source.Location = new System.Drawing.Point(3, 0);
            this.source.Margin = new System.Windows.Forms.Padding(3, 0, 3, 0);
            this.source.Name = "source";
            this.source.Size = new System.Drawing.Size(398, 31);
            this.source.TabIndex = 10;
            this.source.SelectedIndexChanged += new System.EventHandler(this.source_SelectedIndexChanged);
            // 
            // panelEdit
            // 
            this.panelEdit.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.panelEdit.Controls.Add(this.btnRemove);
            this.panelEdit.Controls.Add(this.btnDown);
            this.panelEdit.Controls.Add(this.btnUp);
            this.panelEdit.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            this.panelEdit.Location = new System.Drawing.Point(795, 8);
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
            this.btnRemove.Click += new System.EventHandler(this.btnRemove_Click);
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
            this.btnDown.Click += new System.EventHandler(this.btnDown_Click);
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
            this.btnUp.Click += new System.EventHandler(this.btnUp_Click);
            // 
            // label2
            // 
            this.label2.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.label2.Location = new System.Drawing.Point(3, 33);
            this.label2.Name = "label2";
            this.label2.Padding = new System.Windows.Forms.Padding(0, 10, 0, 0);
            this.label2.Size = new System.Drawing.Size(43, 33);
            this.label2.TabIndex = 12;
            this.label2.Text = "Else";
            // 
            // elseActionsList
            // 
            this.elseActionsList.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.elseActionsList.AutoSize = true;
            this.elseActionsList.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.elseActionsList.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.elseActionsList.Location = new System.Drawing.Point(0, 99);
            this.elseActionsList.Margin = new System.Windows.Forms.Padding(0);
            this.elseActionsList.MaximumSize = new System.Drawing.Size(883, 0);
            this.elseActionsList.MinimumSize = new System.Drawing.Size(883, 0);
            this.elseActionsList.Name = "elseActionsList";
            this.elseActionsList.Size = new System.Drawing.Size(883, 0);
            this.elseActionsList.TabIndex = 9;
            this.elseActionsList.Paint += new System.Windows.Forms.PaintEventHandler(this.elseActionsList_Paint);
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
            this.flowLayoutPanel1.Controls.Add(this.btnAddAction);
            this.flowLayoutPanel1.Controls.Add(this.actionsList);
            this.flowLayoutPanel1.Controls.Add(this.label2);
            this.flowLayoutPanel1.Controls.Add(this.btnAddActionElse);
            this.flowLayoutPanel1.Controls.Add(this.elseActionsList);
            this.flowLayoutPanel1.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.flowLayoutPanel1.Location = new System.Drawing.Point(9, 41);
            this.flowLayoutPanel1.Margin = new System.Windows.Forms.Padding(0);
            this.flowLayoutPanel1.MaximumSize = new System.Drawing.Size(883, 0);
            this.flowLayoutPanel1.MinimumSize = new System.Drawing.Size(883, 0);
            this.flowLayoutPanel1.Name = "flowLayoutPanel1";
            this.flowLayoutPanel1.Padding = new System.Windows.Forms.Padding(0, 0, 0, 10);
            this.flowLayoutPanel1.Size = new System.Drawing.Size(883, 109);
            this.flowLayoutPanel1.TabIndex = 14;
            // 
            // flowLayoutPanel2
            // 
            this.flowLayoutPanel2.Controls.Add(this.source);
            this.flowLayoutPanel2.Controls.Add(this.methodBox);
            this.flowLayoutPanel2.Controls.Add(this.valueToCompare);
            this.flowLayoutPanel2.Location = new System.Drawing.Point(188, 7);
            this.flowLayoutPanel2.Name = "flowLayoutPanel2";
            this.flowLayoutPanel2.Size = new System.Drawing.Size(593, 31);
            this.flowLayoutPanel2.TabIndex = 15;
            // 
            // ConditionItem
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSize = true;
            this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(35)))), ((int)(((byte)(65)))));
            this.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.Controls.Add(this.panelEdit);
            this.Controls.Add(this.typeBox);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.flowLayoutPanel1);
            this.Controls.Add(this.flowLayoutPanel2);
            this.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.ForeColor = System.Drawing.Color.White;
            this.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            this.Name = "ConditionItem";
            this.Size = new System.Drawing.Size(893, 150);
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
            this.flowLayoutPanel2.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.ComboBox typeBox;
        private System.Windows.Forms.ComboBox methodBox;
        private System.Windows.Forms.TextBox valueToCompare;
        private System.Windows.Forms.FlowLayoutPanel actionsList;
        private PictureButton btnAddAction;
        private System.Windows.Forms.ComboBox source;
        private System.Windows.Forms.FlowLayoutPanel panelEdit;
        private PictureButton btnRemove;
        private PictureButton btnDown;
        private PictureButton btnUp;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.FlowLayoutPanel elseActionsList;
        private PictureButton btnAddActionElse;
        private System.Windows.Forms.FlowLayoutPanel flowLayoutPanel1;
        private System.Windows.Forms.FlowLayoutPanel flowLayoutPanel2;
    }
}
