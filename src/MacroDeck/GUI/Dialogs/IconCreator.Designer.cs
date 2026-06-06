
using System.ComponentModel;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    partial class IconCreator
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
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            preview = new BufferedPanel();
            Layers = new ListBox();
            btnAddLayer = new PictureButton();
            btnRemoveLayer = new PictureButton();
            lblLayers = new Label();
            btnAddImage = new ButtonPrimary();
            btnOk = new ButtonPrimary();
            btnBackgroundColor = new ButtonPrimary();
            ((ISupportInitialize)btnAddLayer).BeginInit();
            ((ISupportInitialize)btnRemoveLayer).BeginInit();
            SuspendLayout();
            // 
            // preview
            // 
            preview.BackColor = Color.FromArgb(55, 55, 55);
            preview.BackgroundImageLayout = ImageLayout.Stretch;
            preview.Location = new Point(12, 31);
            preview.Name = "preview";
            preview.Size = new Size(250, 250);
            preview.TabIndex = 3;
            // 
            // Layers
            // 
            Layers.BackColor = Color.FromArgb(55, 55, 55);
            Layers.BorderStyle = BorderStyle.FixedSingle;
            Layers.Font = new Font("Tahoma", 14.25F);
            Layers.ForeColor = Color.White;
            Layers.FormattingEnabled = true;
            Layers.ItemHeight = 23;
            Layers.Location = new Point(268, 53);
            Layers.Name = "Layers";
            Layers.Size = new Size(194, 186);
            Layers.TabIndex = 4;
            // 
            // btnAddLayer
            // 
            btnAddLayer.BackColor = Color.Transparent;
            btnAddLayer.BackgroundImage = Properties.Resources.Create_Normal;
            btnAddLayer.BackgroundImageLayout = ImageLayout.Stretch;
            btnAddLayer.Cursor = Cursors.Hand;
            btnAddLayer.Font = new Font("Tahoma", 9.75F);
            btnAddLayer.ForeColor = Color.White;
            btnAddLayer.HoverImage = Properties.Resources.Create_Hover;
            btnAddLayer.Location = new Point(409, 245);
            btnAddLayer.Name = "btnAddLayer";
            btnAddLayer.Size = new Size(25, 25);
            btnAddLayer.TabIndex = 5;
            btnAddLayer.TabStop = false;
            btnAddLayer.Click += BtnAddLayer_Click;
            // 
            // btnRemoveLayer
            // 
            btnRemoveLayer.BackColor = Color.Transparent;
            btnRemoveLayer.BackgroundImage = Properties.Resources.Delete_Normal;
            btnRemoveLayer.BackgroundImageLayout = ImageLayout.Stretch;
            btnRemoveLayer.Cursor = Cursors.Hand;
            btnRemoveLayer.Font = new Font("Tahoma", 9.75F);
            btnRemoveLayer.ForeColor = Color.White;
            btnRemoveLayer.HoverImage = Properties.Resources.Delete_Hover;
            btnRemoveLayer.Location = new Point(437, 245);
            btnRemoveLayer.Name = "btnRemoveLayer";
            btnRemoveLayer.Size = new Size(25, 25);
            btnRemoveLayer.TabIndex = 6;
            btnRemoveLayer.TabStop = false;
            btnRemoveLayer.Click += BtnRemoveLayer_Click;
            // 
            // lblLayers
            // 
            lblLayers.AutoSize = true;
            lblLayers.Font = new Font("Tahoma", 12F, FontStyle.Bold);
            lblLayers.Location = new Point(268, 31);
            lblLayers.Name = "lblLayers";
            lblLayers.Size = new Size(62, 19);
            lblLayers.TabIndex = 7;
            lblLayers.Text = "Layers";
            lblLayers.UseMnemonic = false;
            // 
            // btnAddImage
            // 
            btnAddImage.BorderRadius = 8;
            btnAddImage.Cursor = Cursors.Hand;
            btnAddImage.FlatAppearance.BorderSize = 0;
            btnAddImage.FlatStyle = FlatStyle.Flat;
            btnAddImage.Font = new Font("Tahoma", 9.75F);
            btnAddImage.ForeColor = Color.White;
            btnAddImage.HoverColor = Color.Empty;
            btnAddImage.Icon = null;
            btnAddImage.Location = new Point(12, 287);
            btnAddImage.Name = "btnAddImage";
            btnAddImage.Progress = 0;
            btnAddImage.ProgressColor = Color.FromArgb(0, 103, 205);
            btnAddImage.Size = new Size(128, 23);
            btnAddImage.TabIndex = 8;
            btnAddImage.Text = "Add image";
            btnAddImage.UseMnemonic = false;
            btnAddImage.UseVisualStyleBackColor = false;
            btnAddImage.UseWindowsAccentColor = true;
            btnAddImage.WriteProgress = true;
            btnAddImage.Click += BtnAddImage_Click;
            // 
            // btnOk
            // 
            btnOk.BorderRadius = 8;
            btnOk.Cursor = Cursors.Hand;
            btnOk.FlatAppearance.BorderSize = 0;
            btnOk.FlatStyle = FlatStyle.Flat;
            btnOk.Font = new Font("Tahoma", 9.75F);
            btnOk.ForeColor = Color.White;
            btnOk.HoverColor = Color.Empty;
            btnOk.Icon = null;
            btnOk.Location = new Point(388, 354);
            btnOk.Name = "btnOk";
            btnOk.Progress = 0;
            btnOk.ProgressColor = Color.FromArgb(0, 103, 205);
            btnOk.Size = new Size(75, 25);
            btnOk.TabIndex = 9;
            btnOk.Text = "Ok";
            btnOk.UseMnemonic = false;
            btnOk.UseVisualStyleBackColor = false;
            btnOk.UseWindowsAccentColor = true;
            btnOk.WriteProgress = true;
            btnOk.Click += BtnOk_Click;
            // 
            // btnBackgroundColor
            // 
            btnBackgroundColor.BorderRadius = 8;
            btnBackgroundColor.Cursor = Cursors.Hand;
            btnBackgroundColor.FlatAppearance.BorderSize = 0;
            btnBackgroundColor.FlatStyle = FlatStyle.Flat;
            btnBackgroundColor.Font = new Font("Tahoma", 9.75F);
            btnBackgroundColor.ForeColor = Color.White;
            btnBackgroundColor.HoverColor = Color.Empty;
            btnBackgroundColor.Icon = null;
            btnBackgroundColor.Location = new Point(12, 321);
            btnBackgroundColor.Name = "btnBackgroundColor";
            btnBackgroundColor.Progress = 0;
            btnBackgroundColor.ProgressColor = Color.FromArgb(0, 103, 205);
            btnBackgroundColor.Size = new Size(128, 23);
            btnBackgroundColor.TabIndex = 10;
            btnBackgroundColor.Text = "Background color";
            btnBackgroundColor.UseMnemonic = false;
            btnBackgroundColor.UseVisualStyleBackColor = false;
            btnBackgroundColor.UseWindowsAccentColor = true;
            btnBackgroundColor.WriteProgress = true;
            btnBackgroundColor.Click += BtnBackgroundColor_Click;
            // 
            // IconCreator
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(475, 391);
            Controls.Add(btnBackgroundColor);
            Controls.Add(btnOk);
            Controls.Add(btnAddImage);
            Controls.Add(lblLayers);
            Controls.Add(btnRemoveLayer);
            Controls.Add(btnAddLayer);
            Controls.Add(Layers);
            Controls.Add(preview);
            Name = "IconCreator";
            Text = "Icon creator";
            Load += IconCreator_Load;
            ((ISupportInitialize)btnAddLayer).EndInit();
            ((ISupportInitialize)btnRemoveLayer).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private BufferedPanel preview;
        private ListBox Layers;
        private PictureButton btnAddLayer;
        private PictureButton btnRemoveLayer;
        private Label lblLayers;
        private ButtonPrimary btnAddImage;
        private ButtonPrimary btnOk;
        private ButtonPrimary btnBackgroundColor;
    }
}