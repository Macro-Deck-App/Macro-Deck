
using SuchByte.MacroDeck.GUI.CustomControls;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    partial class IconCreator
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
            this.preview = new SuchByte.MacroDeck.GUI.CustomControls.BufferedPanel();
            this.Layers = new System.Windows.Forms.ListBox();
            this.btnAddLayer = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.btnRemoveLayer = new SuchByte.MacroDeck.GUI.CustomControls.PictureButton();
            this.lblLayers = new System.Windows.Forms.Label();
            this.btnAddImage = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.btnOk = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            this.btnBackgroundColor = new SuchByte.MacroDeck.GUI.CustomControls.ButtonPrimary();
            ((System.ComponentModel.ISupportInitialize)(this.btnAddLayer)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnRemoveLayer)).BeginInit();
            this.SuspendLayout();
            // 
            // preview
            // 
            this.preview.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.preview.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.preview.Location = new System.Drawing.Point(12, 31);
            this.preview.Name = "preview";
            this.preview.Size = new System.Drawing.Size(250, 250);
            this.preview.TabIndex = 3;
            // 
            // Layers
            // 
            this.Layers.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(55)))), ((int)(((byte)(55)))));
            this.Layers.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.Layers.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.Layers.ForeColor = System.Drawing.Color.White;
            this.Layers.FormattingEnabled = true;
            this.Layers.ItemHeight = 23;
            this.Layers.Location = new System.Drawing.Point(268, 53);
            this.Layers.Name = "Layers";
            this.Layers.Size = new System.Drawing.Size(194, 186);
            this.Layers.TabIndex = 4;
            // 
            // btnAddLayer
            // 
            this.btnAddLayer.BackColor = System.Drawing.Color.Transparent;
            this.btnAddLayer.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Create_Normal;
            this.btnAddLayer.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnAddLayer.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnAddLayer.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnAddLayer.ForeColor = System.Drawing.Color.White;
            this.btnAddLayer.HoverImage = global::SuchByte.MacroDeck.Properties.Resources.Create_Hover;
            this.btnAddLayer.Location = new System.Drawing.Point(409, 245);
            this.btnAddLayer.Name = "btnAddLayer";
            this.btnAddLayer.Size = new System.Drawing.Size(25, 25);
            this.btnAddLayer.TabIndex = 5;
            this.btnAddLayer.TabStop = false;
            this.btnAddLayer.Click += new System.EventHandler(this.BtnAddLayer_Click);
            // 
            // btnRemoveLayer
            // 
            this.btnRemoveLayer.BackColor = System.Drawing.Color.Transparent;
            this.btnRemoveLayer.BackgroundImage = global::SuchByte.MacroDeck.Properties.Resources.Delete_Normal;
            this.btnRemoveLayer.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnRemoveLayer.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnRemoveLayer.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnRemoveLayer.ForeColor = System.Drawing.Color.White;
            this.btnRemoveLayer.HoverImage = global::SuchByte.MacroDeck.Properties.Resources.Delete_Hover;
            this.btnRemoveLayer.Location = new System.Drawing.Point(437, 245);
            this.btnRemoveLayer.Name = "btnRemoveLayer";
            this.btnRemoveLayer.Size = new System.Drawing.Size(25, 25);
            this.btnRemoveLayer.TabIndex = 6;
            this.btnRemoveLayer.TabStop = false;
            this.btnRemoveLayer.Click += new System.EventHandler(this.BtnRemoveLayer_Click);
            // 
            // lblLayers
            // 
            this.lblLayers.AutoSize = true;
            this.lblLayers.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.lblLayers.Location = new System.Drawing.Point(268, 31);
            this.lblLayers.Name = "lblLayers";
            this.lblLayers.Size = new System.Drawing.Size(62, 19);
            this.lblLayers.TabIndex = 7;
            this.lblLayers.Text = "Layers";
            // 
            // btnAddImage
            // 
            this.btnAddImage.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnAddImage.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnAddImage.FlatAppearance.BorderSize = 0;
            this.btnAddImage.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAddImage.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnAddImage.ForeColor = System.Drawing.Color.White;
            this.btnAddImage.Location = new System.Drawing.Point(12, 287);
            this.btnAddImage.Name = "btnAddImage";
            this.btnAddImage.Size = new System.Drawing.Size(128, 23);
            this.btnAddImage.TabIndex = 8;
            this.btnAddImage.Text = "Add image";
            this.btnAddImage.UseVisualStyleBackColor = false;
            this.btnAddImage.Click += new System.EventHandler(this.BtnAddImage_Click);
            // 
            // btnOk
            // 
            this.btnOk.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnOk.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnOk.FlatAppearance.BorderSize = 0;
            this.btnOk.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOk.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnOk.ForeColor = System.Drawing.Color.White;
            this.btnOk.Location = new System.Drawing.Point(388, 354);
            this.btnOk.Name = "btnOk";
            this.btnOk.Size = new System.Drawing.Size(75, 25);
            this.btnOk.TabIndex = 9;
            this.btnOk.Text = "Ok";
            this.btnOk.UseVisualStyleBackColor = false;
            this.btnOk.Click += new System.EventHandler(this.BtnOk_Click);
            // 
            // btnBackgroundColor
            // 
            this.btnBackgroundColor.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(123)))), ((int)(((byte)(255)))));
            this.btnBackgroundColor.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnBackgroundColor.FlatAppearance.BorderSize = 0;
            this.btnBackgroundColor.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnBackgroundColor.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.btnBackgroundColor.ForeColor = System.Drawing.Color.White;
            this.btnBackgroundColor.Location = new System.Drawing.Point(12, 321);
            this.btnBackgroundColor.Name = "btnBackgroundColor";
            this.btnBackgroundColor.Size = new System.Drawing.Size(128, 23);
            this.btnBackgroundColor.TabIndex = 10;
            this.btnBackgroundColor.Text = "Background color";
            this.btnBackgroundColor.UseVisualStyleBackColor = false;
            this.btnBackgroundColor.Click += new System.EventHandler(this.BtnBackgroundColor_Click);
            // 
            // IconCreator
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(475, 391);
            this.Controls.Add(this.btnBackgroundColor);
            this.Controls.Add(this.btnOk);
            this.Controls.Add(this.btnAddImage);
            this.Controls.Add(this.lblLayers);
            this.Controls.Add(this.btnRemoveLayer);
            this.Controls.Add(this.btnAddLayer);
            this.Controls.Add(this.Layers);
            this.Controls.Add(this.preview);
            this.Name = "IconCreator";
            this.Text = "IconCreator";
            this.Load += new System.EventHandler(this.IconCreator_Load);
            this.Controls.SetChildIndex(this.preview, 0);
            this.Controls.SetChildIndex(this.Layers, 0);
            this.Controls.SetChildIndex(this.btnAddLayer, 0);
            this.Controls.SetChildIndex(this.btnRemoveLayer, 0);
            this.Controls.SetChildIndex(this.lblLayers, 0);
            this.Controls.SetChildIndex(this.btnAddImage, 0);
            this.Controls.SetChildIndex(this.btnOk, 0);
            this.Controls.SetChildIndex(this.btnBackgroundColor, 0);
            ((System.ComponentModel.ISupportInitialize)(this.btnAddLayer)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnRemoveLayer)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private CustomControls.BufferedPanel preview;
        private System.Windows.Forms.ListBox Layers;
        private PictureButton btnAddLayer;
        private PictureButton btnRemoveLayer;
        private System.Windows.Forms.Label lblLayers;
        private ButtonPrimary btnAddImage;
        private ButtonPrimary btnOk;
        private ButtonPrimary btnBackgroundColor;
    }
}