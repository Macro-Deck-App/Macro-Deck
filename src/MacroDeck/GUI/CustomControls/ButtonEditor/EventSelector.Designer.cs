
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls.ButtonEditor
{
    partial class EventSelector
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
            this.eventsList = new FlowLayoutPanel();
            this.btnAddEvent = new ButtonPrimary();
            this.flowLayoutPanel1 = new FlowLayoutPanel();
            this.flowLayoutPanel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // eventsList
            // 
            this.eventsList.AutoScroll = true;
            this.eventsList.AutoSize = true;
            this.eventsList.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.eventsList.Location = new Point(3, 3);
            this.eventsList.Margin = new Padding(0);
            this.eventsList.MaximumSize = new Size(869, 490);
            this.eventsList.MinimumSize = new Size(869, 1);
            this.eventsList.Name = "eventsList";
            this.eventsList.Size = new Size(869, 1);
            this.eventsList.TabIndex = 4;
            // 
            // btnAddEvent
            // 
            this.btnAddEvent.BackColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnAddEvent.BorderRadius = 8;
            this.btnAddEvent.Cursor = Cursors.Hand;
            this.btnAddEvent.FlatAppearance.BorderSize = 0;
            this.btnAddEvent.FlatStyle = FlatStyle.Flat;
            this.btnAddEvent.Font = new Font("Tahoma", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            this.btnAddEvent.ForeColor = Color.White;
            this.btnAddEvent.HoverColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnAddEvent.Icon = null;
            this.btnAddEvent.Location = new Point(6, 7);
            this.btnAddEvent.Name = "btnAddEvent";
            this.btnAddEvent.Progress = 0;
            this.btnAddEvent.ProgressColor = Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnAddEvent.Size = new Size(165, 30);
            this.btnAddEvent.TabIndex = 6;
            this.btnAddEvent.Text = "+ Event";
            this.btnAddEvent.UseMnemonic = false;
            this.btnAddEvent.UseVisualStyleBackColor = false;
            this.btnAddEvent.Click += new EventHandler(this.BtnAddEvent_Click);
            // 
            // flowLayoutPanel1
            // 
            this.flowLayoutPanel1.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.flowLayoutPanel1.Controls.Add(this.eventsList);
            this.flowLayoutPanel1.Controls.Add(this.btnAddEvent);
            this.flowLayoutPanel1.Dock = DockStyle.Fill;
            this.flowLayoutPanel1.Location = new Point(5, 5);
            this.flowLayoutPanel1.Name = "flowLayoutPanel1";
            this.flowLayoutPanel1.Padding = new Padding(3);
            this.flowLayoutPanel1.Size = new Size(869, 531);
            this.flowLayoutPanel1.TabIndex = 7;
            // 
            // EventSelector
            // 
            
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.Controls.Add(this.flowLayoutPanel1);
            this.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.ForeColor = Color.White;
            this.Name = "EventSelector";
            this.Padding = new Padding(5);
            this.Size = new Size(879, 541);
            this.flowLayoutPanel1.ResumeLayout(false);
            this.flowLayoutPanel1.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion
        private FlowLayoutPanel eventsList;
        private ButtonPrimary btnAddEvent;
        private FlowLayoutPanel flowLayoutPanel1;
    }
}
