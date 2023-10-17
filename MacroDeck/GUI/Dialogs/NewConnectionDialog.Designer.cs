
using System.ComponentModel;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    partial class NewConnectionDialog
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private IContainer components = null;

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

            this._denyTimer?.Dispose();

            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.lblNewConnectionRequest = new System.Windows.Forms.Label();
            this.lblClientId = new System.Windows.Forms.Label();
            this.lblType = new System.Windows.Forms.Label();
            this.type = new System.Windows.Forms.Label();
            this.clientId = new System.Windows.Forms.Label();
            this.btnAccept = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.btnDeny = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.checkBlockThisDevice = new System.Windows.Forms.CheckBox();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // pictureBox1
            // 
            this.pictureBox1.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.pictureBox1.Image = global::SuchByte.MacroDeck.Properties.Resources.Macro_Deck_2021;
            this.pictureBox1.Location = new System.Drawing.Point(16, 27);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(53, 53);
            this.pictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.pictureBox1.TabIndex = 2;
            this.pictureBox1.TabStop = false;
            // 
            // lblNewConnectionRequest
            // 
            this.lblNewConnectionRequest.Font = new System.Drawing.Font("Tahoma", 21.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.lblNewConnectionRequest.Location = new System.Drawing.Point(75, 27);
            this.lblNewConnectionRequest.Name = "lblNewConnectionRequest";
            this.lblNewConnectionRequest.Size = new System.Drawing.Size(451, 53);
            this.lblNewConnectionRequest.TabIndex = 3;
            this.lblNewConnectionRequest.Text = "New connection request";
            this.lblNewConnectionRequest.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblClientId
            // 
            this.lblClientId.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.lblClientId.Location = new System.Drawing.Point(32, 127);
            this.lblClientId.Name = "lblClientId";
            this.lblClientId.Size = new System.Drawing.Size(165, 27);
            this.lblClientId.TabIndex = 4;
            this.lblClientId.Text = "Client Id";
            this.lblClientId.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblType
            // 
            this.lblType.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.lblType.Location = new System.Drawing.Point(32, 181);
            this.lblType.Name = "lblType";
            this.lblType.Size = new System.Drawing.Size(165, 27);
            this.lblType.TabIndex = 6;
            this.lblType.Text = "Type";
            this.lblType.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // type
            // 
            this.type.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.type.Location = new System.Drawing.Point(203, 181);
            this.type.Name = "type";
            this.type.Size = new System.Drawing.Size(307, 27);
            this.type.TabIndex = 9;
            this.type.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // clientId
            // 
            this.clientId.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.clientId.Location = new System.Drawing.Point(203, 127);
            this.clientId.Name = "clientId";
            this.clientId.Size = new System.Drawing.Size(307, 27);
            this.clientId.TabIndex = 7;
            this.clientId.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // btnAccept
            // 
            this.btnAccept.BorderRadius = 8;
            this.btnAccept.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnAccept.FlatAppearance.BorderSize = 0;
            this.btnAccept.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAccept.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnAccept.ForeColor = System.Drawing.Color.White;
            this.btnAccept.HoverColor = System.Drawing.Color.Empty;
            this.btnAccept.Icon = null;
            this.btnAccept.Location = new System.Drawing.Point(167, 256);
            this.btnAccept.Name = "btnAccept";
            this.btnAccept.Progress = 0;
            this.btnAccept.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(103)))), ((int)(((byte)(205)))));
            this.btnAccept.Size = new System.Drawing.Size(209, 60);
            this.btnAccept.TabIndex = 10;
            this.btnAccept.Text = "Accept";
            this.btnAccept.UseVisualStyleBackColor = true;
            this.btnAccept.UseWindowsAccentColor = true;
            this.btnAccept.Click += new System.EventHandler(this.BtnAccept_Click);
            // 
            // btnDeny
            // 
            this.btnDeny.BorderRadius = 8;
            this.btnDeny.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnDeny.FlatAppearance.BorderSize = 0;
            this.btnDeny.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnDeny.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnDeny.ForeColor = System.Drawing.Color.White;
            this.btnDeny.HoverColor = System.Drawing.Color.Empty;
            this.btnDeny.Icon = null;
            this.btnDeny.Location = new System.Drawing.Point(167, 322);
            this.btnDeny.Name = "btnDeny";
            this.btnDeny.Progress = 0;
            this.btnDeny.ProgressColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(103)))), ((int)(((byte)(205)))));
            this.btnDeny.Size = new System.Drawing.Size(209, 60);
            this.btnDeny.TabIndex = 11;
            this.btnDeny.Text = "Deny";
            this.btnDeny.UseVisualStyleBackColor = true;
            this.btnDeny.UseWindowsAccentColor = false;
            this.btnDeny.Click += new System.EventHandler(this.BtnDeny_Click);
            // 
            // checkBlockThisDevice
            // 
            this.checkBlockThisDevice.CheckAlign = System.Drawing.ContentAlignment.TopLeft;
            this.checkBlockThisDevice.Location = new System.Drawing.Point(172, 388);
            this.checkBlockThisDevice.Name = "checkBlockThisDevice";
            this.checkBlockThisDevice.Size = new System.Drawing.Size(204, 42);
            this.checkBlockThisDevice.TabIndex = 12;
            this.checkBlockThisDevice.Text = "Block this device";
            this.checkBlockThisDevice.TextAlign = System.Drawing.ContentAlignment.TopLeft;
            this.checkBlockThisDevice.UseVisualStyleBackColor = true;
            // 
            // NewConnectionDialog
            // 
            this.AcceptButton = this.btnAccept;
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.ClientSize = new System.Drawing.Size(543, 441);
            this.Controls.Add(this.checkBlockThisDevice);
            this.Controls.Add(this.btnDeny);
            this.Controls.Add(this.btnAccept);
            this.Controls.Add(this.type);
            this.Controls.Add(this.clientId);
            this.Controls.Add(this.lblType);
            this.Controls.Add(this.lblClientId);
            this.Controls.Add(this.lblNewConnectionRequest);
            this.Controls.Add(this.pictureBox1);
            this.Location = new System.Drawing.Point(0, 0);
            this.Name = "NewConnectionDialog";
            this.ShowIcon = true;
            this.ShowInTaskbar = true;
            this.Text = "Macro Deck :: New connection";
            this.Load += new System.EventHandler(this.NewConnectionDialog_Load);
            this.Controls.SetChildIndex(this.pictureBox1, 0);
            this.Controls.SetChildIndex(this.lblNewConnectionRequest, 0);
            this.Controls.SetChildIndex(this.lblClientId, 0);
            this.Controls.SetChildIndex(this.lblType, 0);
            this.Controls.SetChildIndex(this.clientId, 0);
            this.Controls.SetChildIndex(this.type, 0);
            this.Controls.SetChildIndex(this.btnAccept, 0);
            this.Controls.SetChildIndex(this.btnDeny, 0);
            this.Controls.SetChildIndex(this.checkBlockThisDevice, 0);
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private PictureBox pictureBox1;
        private Label lblNewConnectionRequest;
        private Label lblClientId;
        private Label lblType;
        private Label type;
        private Label clientId;
        private ButtonPrimary btnAccept;
        private ButtonPrimary btnDeny;
        private CheckBox checkBlockThisDevice;
    }
}