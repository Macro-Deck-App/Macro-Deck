
namespace SuchByte.MacroDeck.GUI.CustomControls.ButtonEditor
{
    partial class EventSelector
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
            this.eventsList = new System.Windows.Forms.FlowLayoutPanel();
            this.btnAddEvent = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.flowLayoutPanel1 = new System.Windows.Forms.FlowLayoutPanel();
            this.flowLayoutPanel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // eventsList
            // 
            this.eventsList.AutoScroll = true;
            this.eventsList.AutoSize = true;
            this.eventsList.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.eventsList.Location = new System.Drawing.Point(3, 3);
            this.eventsList.Margin = new System.Windows.Forms.Padding(0);
            this.eventsList.MaximumSize = new System.Drawing.Size(869, 490);
            this.eventsList.MinimumSize = new System.Drawing.Size(869, 1);
            this.eventsList.Name = "eventsList";
            this.eventsList.Size = new System.Drawing.Size(869, 1);
            this.eventsList.TabIndex = 4;
            // 
            // btnAddEvent
            // 
            this.btnAddEvent.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnAddEvent.BorderRadius = 8;
            this.btnAddEvent.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnAddEvent.FlatAppearance.BorderSize = 0;
            this.btnAddEvent.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAddEvent.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnAddEvent.ForeColor = System.Drawing.Color.White;
            this.btnAddEvent.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnAddEvent.Icon = null;
            this.btnAddEvent.Location = new System.Drawing.Point(6, 7);
            this.btnAddEvent.Name = "btnAddEvent";
            this.btnAddEvent.Progress = 0;
            this.btnAddEvent.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnAddEvent.Size = new System.Drawing.Size(165, 30);
            this.btnAddEvent.TabIndex = 6;
            this.btnAddEvent.Text = "+ Event";
            this.btnAddEvent.UseVisualStyleBackColor = false;
            this.btnAddEvent.Click += new System.EventHandler(this.BtnAddEvent_Click);
            // 
            // flowLayoutPanel1
            // 
            this.flowLayoutPanel1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.flowLayoutPanel1.Controls.Add(this.eventsList);
            this.flowLayoutPanel1.Controls.Add(this.btnAddEvent);
            this.flowLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.flowLayoutPanel1.Location = new System.Drawing.Point(5, 5);
            this.flowLayoutPanel1.Name = "flowLayoutPanel1";
            this.flowLayoutPanel1.Padding = new System.Windows.Forms.Padding(3);
            this.flowLayoutPanel1.Size = new System.Drawing.Size(869, 531);
            this.flowLayoutPanel1.TabIndex = 7;
            // 
            // EventSelector
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.Controls.Add(this.flowLayoutPanel1);
            this.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.ForeColor = System.Drawing.Color.White;
            this.Name = "EventSelector";
            this.Padding = new System.Windows.Forms.Padding(5);
            this.Size = new System.Drawing.Size(879, 541);
            this.flowLayoutPanel1.ResumeLayout(false);
            this.flowLayoutPanel1.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.FlowLayoutPanel eventsList;
        private ButtonPrimary btnAddEvent;
        private System.Windows.Forms.FlowLayoutPanel flowLayoutPanel1;
    }
}
