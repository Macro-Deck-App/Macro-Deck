
namespace SuchByte.MacroDeck.GUI.Dialogs
{
    partial class DeviceConfigurator
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.btnOk = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.lblBrightness = new System.Windows.Forms.Label();
            this.brightness = new System.Windows.Forms.TrackBar();
            this.checkAutoConnect = new System.Windows.Forms.CheckBox();
            this.lblKeepWake = new System.Windows.Forms.Label();
            this.radioKeepAwakeNever = new SuchByte.MacroDeck.GUI.CustomControls.TabRadioButton();
            this.flowLayoutPanel1 = new System.Windows.Forms.FlowLayoutPanel();
            this.radioKeepAwakeConnected = new SuchByte.MacroDeck.GUI.CustomControls.TabRadioButton();
            this.radioKeepAwakeAlways = new SuchByte.MacroDeck.GUI.CustomControls.TabRadioButton();
            ((System.ComponentModel.ISupportInitialize)(this.brightness)).BeginInit();
            this.flowLayoutPanel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // btnOk
            // 
            this.btnOk.BorderRadius = 8;
            this.btnOk.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnOk.FlatAppearance.BorderSize = 0;
            this.btnOk.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOk.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnOk.ForeColor = System.Drawing.Color.White;
            this.btnOk.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(89)))), ((int)(((byte)(184)))));
            this.btnOk.Icon = null;
            this.btnOk.Location = new System.Drawing.Point(483, 156);
            this.btnOk.Name = "btnOk";
            this.btnOk.Progress = 0;
            this.btnOk.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(46)))), ((int)(((byte)(94)))));
            this.btnOk.Size = new System.Drawing.Size(75, 25);
            this.btnOk.TabIndex = 2;
            this.btnOk.Text = "Ok";
            this.btnOk.UseMnemonic = false;
            this.btnOk.UseVisualStyleBackColor = false;
            this.btnOk.UseWindowsAccentColor = true;
            this.btnOk.Click += new System.EventHandler(this.BtnOk_Click);
            // 
            // lblBrightness
            // 
            this.lblBrightness.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblBrightness.Location = new System.Drawing.Point(17, 27);
            this.lblBrightness.Name = "lblBrightness";
            this.lblBrightness.Size = new System.Drawing.Size(110, 19);
            this.lblBrightness.TabIndex = 3;
            this.lblBrightness.Text = "Brightness";
            this.lblBrightness.UseMnemonic = false;
            // 
            // brightness
            // 
            this.brightness.Location = new System.Drawing.Point(299, 27);
            this.brightness.Minimum = 1;
            this.brightness.Name = "brightness";
            this.brightness.Size = new System.Drawing.Size(259, 45);
            this.brightness.TabIndex = 4;
            this.brightness.TickStyle = System.Windows.Forms.TickStyle.None;
            this.brightness.Value = 10;
            this.brightness.Scroll += new System.EventHandler(this.Brightness_Scroll);
            // 
            // checkAutoConnect
            // 
            this.checkAutoConnect.CheckAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.checkAutoConnect.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.checkAutoConnect.Location = new System.Drawing.Point(17, 59);
            this.checkAutoConnect.Name = "checkAutoConnect";
            this.checkAutoConnect.Size = new System.Drawing.Size(541, 22);
            this.checkAutoConnect.TabIndex = 5;
            this.checkAutoConnect.Text = "Connect automatically";
            this.checkAutoConnect.UseMnemonic = false;
            this.checkAutoConnect.UseVisualStyleBackColor = true;
            this.checkAutoConnect.CheckedChanged += new System.EventHandler(this.CheckAutoConnect_CheckedChanged);
            // 
            // lblKeepWake
            // 
            this.lblKeepWake.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblKeepWake.Location = new System.Drawing.Point(17, 93);
            this.lblKeepWake.Name = "lblKeepWake";
            this.lblKeepWake.Size = new System.Drawing.Size(169, 19);
            this.lblKeepWake.TabIndex = 6;
            this.lblKeepWake.Text = "Keep awake";
            this.lblKeepWake.UseMnemonic = false;
            // 
            // radioKeepAwakeNever
            // 
            this.radioKeepAwakeNever.AutoSize = true;
            this.radioKeepAwakeNever.Cursor = System.Windows.Forms.Cursors.Hand;
            this.radioKeepAwakeNever.Location = new System.Drawing.Point(3, 0);
            this.radioKeepAwakeNever.Margin = new System.Windows.Forms.Padding(3, 0, 3, 0);
            this.radioKeepAwakeNever.Name = "radioKeepAwakeNever";
            this.radioKeepAwakeNever.Size = new System.Drawing.Size(58, 20);
            this.radioKeepAwakeNever.TabIndex = 0;
            this.radioKeepAwakeNever.Text = "Never";
            this.radioKeepAwakeNever.UseMnemonic = false;
            this.radioKeepAwakeNever.UseVisualStyleBackColor = true;
            this.radioKeepAwakeNever.CheckedChanged += new System.EventHandler(this.RadioKeepAwakeNever_CheckedChanged);
            // 
            // flowLayoutPanel1
            // 
            this.flowLayoutPanel1.Controls.Add(this.radioKeepAwakeNever);
            this.flowLayoutPanel1.Controls.Add(this.radioKeepAwakeConnected);
            this.flowLayoutPanel1.Controls.Add(this.radioKeepAwakeAlways);
            this.flowLayoutPanel1.Location = new System.Drawing.Point(192, 93);
            this.flowLayoutPanel1.Margin = new System.Windows.Forms.Padding(0);
            this.flowLayoutPanel1.Name = "flowLayoutPanel1";
            this.flowLayoutPanel1.Size = new System.Drawing.Size(366, 29);
            this.flowLayoutPanel1.TabIndex = 7;
            // 
            // radioKeepAwakeConnected
            // 
            this.radioKeepAwakeConnected.AutoSize = true;
            this.radioKeepAwakeConnected.Checked = true;
            this.radioKeepAwakeConnected.Cursor = System.Windows.Forms.Cursors.Hand;
            this.radioKeepAwakeConnected.Location = new System.Drawing.Point(67, 0);
            this.radioKeepAwakeConnected.Margin = new System.Windows.Forms.Padding(3, 0, 3, 0);
            this.radioKeepAwakeConnected.Name = "radioKeepAwakeConnected";
            this.radioKeepAwakeConnected.Size = new System.Drawing.Size(120, 20);
            this.radioKeepAwakeConnected.TabIndex = 1;
            this.radioKeepAwakeConnected.TabStop = true;
            this.radioKeepAwakeConnected.Text = "When connected";
            this.radioKeepAwakeConnected.UseMnemonic = false;
            this.radioKeepAwakeConnected.UseVisualStyleBackColor = true;
            this.radioKeepAwakeConnected.CheckedChanged += new System.EventHandler(this.RadioKeepAwakeConnected_CheckedChanged);
            // 
            // radioKeepAwakeAlways
            // 
            this.radioKeepAwakeAlways.AutoSize = true;
            this.radioKeepAwakeAlways.Cursor = System.Windows.Forms.Cursors.Hand;
            this.radioKeepAwakeAlways.Location = new System.Drawing.Point(193, 0);
            this.radioKeepAwakeAlways.Margin = new System.Windows.Forms.Padding(3, 0, 3, 0);
            this.radioKeepAwakeAlways.Name = "radioKeepAwakeAlways";
            this.radioKeepAwakeAlways.Size = new System.Drawing.Size(65, 20);
            this.radioKeepAwakeAlways.TabIndex = 2;
            this.radioKeepAwakeAlways.Text = "Always";
            this.radioKeepAwakeAlways.UseMnemonic = false;
            this.radioKeepAwakeAlways.UseVisualStyleBackColor = true;
            this.radioKeepAwakeAlways.CheckedChanged += new System.EventHandler(this.RadioKeepAwakeAlways_CheckedChanged);
            // 
            // DeviceConfigurator
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.ClientSize = new System.Drawing.Size(573, 194);
            this.Controls.Add(this.flowLayoutPanel1);
            this.Controls.Add(this.lblKeepWake);
            this.Controls.Add(this.checkAutoConnect);
            this.Controls.Add(this.brightness);
            this.Controls.Add(this.lblBrightness);
            this.Controls.Add(this.btnOk);
            this.Location = new System.Drawing.Point(0, 0);
            this.Name = "DeviceConfigurator";
            this.Text = "DeviceConfigurator";
            this.Load += new System.EventHandler(this.DeviceConfigurator_Load);
            this.Controls.SetChildIndex(this.btnOk, 0);
            this.Controls.SetChildIndex(this.lblBrightness, 0);
            this.Controls.SetChildIndex(this.brightness, 0);
            this.Controls.SetChildIndex(this.checkAutoConnect, 0);
            this.Controls.SetChildIndex(this.lblKeepWake, 0);
            this.Controls.SetChildIndex(this.flowLayoutPanel1, 0);
            ((System.ComponentModel.ISupportInitialize)(this.brightness)).EndInit();
            this.flowLayoutPanel1.ResumeLayout(false);
            this.flowLayoutPanel1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private CustomControls.ButtonPrimary btnOk;
        private System.Windows.Forms.Label lblBrightness;
        private System.Windows.Forms.TrackBar brightness;
        private System.Windows.Forms.CheckBox checkAutoConnect;
        private System.Windows.Forms.Label lblKeepWake;
        private CustomControls.TabRadioButton radioKeepAwakeNever;
        private System.Windows.Forms.FlowLayoutPanel flowLayoutPanel1;
        private CustomControls.TabRadioButton radioKeepAwakeConnected;
        private CustomControls.TabRadioButton radioKeepAwakeAlways;
    }
}