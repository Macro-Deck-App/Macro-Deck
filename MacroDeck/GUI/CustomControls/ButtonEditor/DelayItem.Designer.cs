
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.Properties;

namespace SuchByte.MacroDeck.GUI.CustomControls.ButtonEditor
{
    partial class DelayItem
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
            this.panelEdit = new FlowLayoutPanel();
            this.btnRemove = new PictureButton();
            this.btnDown = new PictureButton();
            this.btnUp = new PictureButton();
            this.lblWait = new Label();
            this.minutes = new NumericUpDown();
            this.label1 = new Label();
            this.seconds = new NumericUpDown();
            this.label2 = new Label();
            this.millis = new NumericUpDown();
            this.label3 = new Label();
            this.panelEdit.SuspendLayout();
            ((ISupportInitialize)(this.btnRemove)).BeginInit();
            ((ISupportInitialize)(this.btnDown)).BeginInit();
            ((ISupportInitialize)(this.btnUp)).BeginInit();
            ((ISupportInitialize)(this.minutes)).BeginInit();
            ((ISupportInitialize)(this.seconds)).BeginInit();
            ((ISupportInitialize)(this.millis)).BeginInit();
            this.SuspendLayout();
            // 
            // panelEdit
            // 
            this.panelEdit.Controls.Add(this.btnRemove);
            this.panelEdit.Controls.Add(this.btnDown);
            this.panelEdit.Controls.Add(this.btnUp);
            this.panelEdit.FlowDirection = FlowDirection.RightToLeft;
            this.panelEdit.Location = new Point(743, 2);
            this.panelEdit.Name = "panelEdit";
            this.panelEdit.Size = new Size(94, 26);
            this.panelEdit.TabIndex = 7;
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
            this.btnRemove.Location = new Point(67, 0);
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
            this.btnDown.Location = new Point(38, 0);
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
            this.btnUp.Location = new Point(9, 0);
            this.btnUp.Margin = new Padding(2, 0, 2, 0);
            this.btnUp.Name = "btnUp";
            this.btnUp.Size = new Size(25, 25);
            this.btnUp.TabIndex = 5;
            this.btnUp.TabStop = false;
            this.btnUp.Click += new EventHandler(this.BtnUp_Click);
            // 
            // lblWait
            // 
            this.lblWait.Font = new Font("Tahoma", 11.25F, FontStyle.Bold, GraphicsUnit.Point);
            this.lblWait.Location = new Point(8, 5);
            this.lblWait.Name = "lblWait";
            this.lblWait.Size = new Size(134, 42);
            this.lblWait.TabIndex = 8;
            this.lblWait.Text = "Wait";
            this.lblWait.TextAlign = ContentAlignment.MiddleLeft;
            this.lblWait.UseMnemonic = false;
            // 
            // minutes
            // 
            this.minutes.BackColor = Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.minutes.Font = new Font("Tahoma", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.minutes.ForeColor = Color.White;
            this.minutes.Location = new Point(148, 10);
            this.minutes.Maximum = new decimal(new int[] {
            59,
            0,
            0,
            0});
            this.minutes.Name = "minutes";
            this.minutes.Size = new Size(84, 30);
            this.minutes.TabIndex = 9;
            this.minutes.ValueChanged += new EventHandler(this.DelayValueChanged);
            // 
            // label1
            // 
            this.label1.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.label1.Location = new Point(238, 4);
            this.label1.Name = "label1";
            this.label1.Size = new Size(10, 43);
            this.label1.TabIndex = 10;
            this.label1.Text = ":";
            this.label1.TextAlign = ContentAlignment.MiddleLeft;
            this.label1.UseMnemonic = false;
            // 
            // seconds
            // 
            this.seconds.BackColor = Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.seconds.Font = new Font("Tahoma", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.seconds.ForeColor = Color.White;
            this.seconds.Location = new Point(254, 10);
            this.seconds.Maximum = new decimal(new int[] {
            59,
            0,
            0,
            0});
            this.seconds.Name = "seconds";
            this.seconds.Size = new Size(84, 30);
            this.seconds.TabIndex = 11;
            this.seconds.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.seconds.ValueChanged += new EventHandler(this.DelayValueChanged);
            // 
            // label2
            // 
            this.label2.Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.label2.Location = new Point(344, 4);
            this.label2.Name = "label2";
            this.label2.Size = new Size(10, 43);
            this.label2.TabIndex = 12;
            this.label2.Text = ":";
            this.label2.TextAlign = ContentAlignment.MiddleLeft;
            this.label2.UseMnemonic = false;
            // 
            // millis
            // 
            this.millis.BackColor = Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.millis.Font = new Font("Tahoma", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            this.millis.ForeColor = Color.White;
            this.millis.Location = new Point(360, 10);
            this.millis.Maximum = new decimal(new int[] {
            999,
            0,
            0,
            0});
            this.millis.Name = "millis";
            this.millis.Size = new Size(84, 30);
            this.millis.TabIndex = 13;
            this.millis.ValueChanged += new EventHandler(this.DelayValueChanged);
            // 
            // label3
            // 
            this.label3.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.label3.ForeColor = Color.Silver;
            this.label3.Location = new Point(450, 4);
            this.label3.Name = "label3";
            this.label3.Size = new Size(239, 43);
            this.label3.TabIndex = 14;
            this.label3.Text = "(Minutes:Seconds:Milliseconds)";
            this.label3.TextAlign = ContentAlignment.MiddleLeft;
            this.label3.UseMnemonic = false;
            // 
            // DelayItem
            // 
            
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = Color.FromArgb(((int)(((byte)(8)))), ((int)(((byte)(107)))), ((int)(((byte)(138)))));
            this.Controls.Add(this.label3);
            this.Controls.Add(this.millis);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.seconds);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.minutes);
            this.Controls.Add(this.lblWait);
            this.Controls.Add(this.panelEdit);
            this.Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.ForeColor = Color.White;
            this.Margin = new Padding(0, 3, 0, 3);
            this.Name = "DelayItem";
            this.Size = new Size(842, 50);
            this.panelEdit.ResumeLayout(false);
            ((ISupportInitialize)(this.btnRemove)).EndInit();
            ((ISupportInitialize)(this.btnDown)).EndInit();
            ((ISupportInitialize)(this.btnUp)).EndInit();
            ((ISupportInitialize)(this.minutes)).EndInit();
            ((ISupportInitialize)(this.seconds)).EndInit();
            ((ISupportInitialize)(this.millis)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private FlowLayoutPanel panelEdit;
        private PictureButton btnRemove;
        private PictureButton btnDown;
        private PictureButton btnUp;
        private Label lblWait;
        private NumericUpDown minutes;
        private Label label1;
        private NumericUpDown seconds;
        private Label label2;
        private NumericUpDown millis;
        private Label label3;
    }
}
