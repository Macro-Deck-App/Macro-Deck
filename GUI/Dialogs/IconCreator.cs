using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    public partial class IconCreator : CustomControls.DialogForm
    {
        private Dictionary<string, Bitmap> _layers = new Dictionary<string, Bitmap>();

        public event EventHandler OnLayersChanged;

        private Image _image;

        public Image Image { get { return this._image; } }

        public IconCreator()
        {
            InitializeComponent();
            this.lblLayers.Text = Language.LanguageManager.Strings.Layers;
            this.btnAddImage.Text = Language.LanguageManager.Strings.AddImage;
            this.btnBackgroundColor.Text = Language.LanguageManager.Strings.Color;
            this.btnOk.Text = Language.LanguageManager.Strings.Ok;
        }

        private void BtnAddLayer_Click(object sender, EventArgs e)
        {
            this.AddLayer();
        }

        private void BtnRemoveLayer_Click(object sender, EventArgs e)
        {
            if (this._layers.Count <= 1) return;
            if (Layers.SelectedItem == null || this._layers[Layers.SelectedItem.ToString()] == null) return;
            this._layers.Remove(Layers.SelectedItem.ToString());
            this.Layers.Items.Remove(Layers.SelectedItem);
            if (this.OnLayersChanged != null)
            {
                this.OnLayersChanged(null, new EventArgs());
            }
        }

        private void AddLayer()
        {
            int layer = this._layers.Count + 1;
            string name = String.Format("Layer {0}", layer);
            PictureBox layerPictureBox = new PictureBox();
            layerPictureBox.Name = name;
            layerPictureBox.Width = preview.Width;
            layerPictureBox.Height = preview.Height;
            layerPictureBox.BackgroundImageLayout = ImageLayout.Stretch;
            layerPictureBox.BackColor = Color.Transparent;
            layerPictureBox.Location = new Point(0, 0);
            this.preview.Controls.Add(layerPictureBox);
            this.Layers.Items.Insert(0, name);
            this.Layers.SelectedItem = name;
            this._layers[name] = new Bitmap(350, 350);

        }

        private void IconCreator_Load(object sender, EventArgs e)
        {
            this.AddLayer();
            this.OnLayersChanged += new EventHandler(this.LayersChanged);
        }

        private void LayersChanged(object sender, EventArgs e)
        {
            List<Bitmap> layers = new List<Bitmap>();
            layers.AddRange(this._layers.Values);

            this.preview.BackgroundImage = Utils.CombineBitmaps.CombineAll(layers);
        }

        private void BtnAddImage_Click(object sender, EventArgs e)
        {
            using (var openFileDialog = new OpenFileDialog
            {
                Title = "Import icon",
                CheckFileExists = true,
                CheckPathExists = true,
                DefaultExt = "png",
                Filter = "Image files (*.png;*.jpg;*.bmp;*.gif)|*.png;*.jpg;*.bmp;*.gif"
            })
            {
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    Bitmap bitmap = new Bitmap(Path.GetFullPath(openFileDialog.FileName));
                    bitmap = new Bitmap(bitmap, new Size(350, 350));
                    
                    if (this.Layers.SelectedItem == null)
                    {
                        this.Layers.SelectedIndex = 0;
                    }
                    this._layers[this.Layers.SelectedItem.ToString()] = bitmap;
                    if (this.OnLayersChanged != null)
                    {
                        this.OnLayersChanged(bitmap, new EventArgs());
                    }
                }
                openFileDialog.Dispose();
            }
        }

        private Image GetImageByName(string name)
        {
            foreach(string layerName in this._layers.Keys)
            {
                if (layerName.Equals(name))
                {
                    return this._layers[layerName];
                }
            }
            return null;
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            List<Bitmap> layers = new List<Bitmap>();
            layers.AddRange(this._layers.Values);

            this._image = Utils.CombineBitmaps.CombineAll(layers);

            this.DialogResult = DialogResult.OK;
        }

        private void BtnBackgroundColor_Click(object sender, EventArgs e)
        {
            using (var colorPicker = new ColorDialog())
            {
                if (colorPicker.ShowDialog() == DialogResult.OK)
                {
                    Bitmap bitmap = new Bitmap(350, 350);
                    using (Graphics gfx = Graphics.FromImage(bitmap))
                    {
                        using (SolidBrush brush = new SolidBrush(colorPicker.Color))
                        {
                            gfx.FillRectangle(brush, 0, 0, 350, 350);
                        }
                    }

                    if (this.Layers.SelectedItem == null)
                    {
                        this.Layers.SelectedIndex = 0;
                    }
                    this._layers[this.Layers.SelectedItem.ToString()] = bitmap;
                    if (this.OnLayersChanged != null)
                    {
                        this.OnLayersChanged(bitmap, new EventArgs());
                    }
                }
            }
        }
    }
    
}
