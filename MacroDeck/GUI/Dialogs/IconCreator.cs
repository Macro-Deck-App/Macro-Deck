using System.Drawing;
using System.IO;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Utils;

namespace SuchByte.MacroDeck.GUI.Dialogs;

public partial class IconCreator : DialogForm
{
    private Dictionary<string, Bitmap> _layers = new();

    public event EventHandler OnLayersChanged;

    private Image _image;

    public Image Image => _image;

    public IconCreator()
    {
        InitializeComponent();
        lblLayers.Text = LanguageManager.Strings.Layers;
        btnAddImage.Text = LanguageManager.Strings.AddImage;
        btnBackgroundColor.Text = LanguageManager.Strings.Color;
        btnOk.Text = LanguageManager.Strings.Ok;
    }

    private void BtnAddLayer_Click(object sender, EventArgs e)
    {
        AddLayer();
    }

    private void BtnRemoveLayer_Click(object sender, EventArgs e)
    {
        if (_layers.Count <= 1) return;
        if (Layers.SelectedItem == null || _layers[Layers.SelectedItem.ToString()] == null) return;
        _layers.Remove(Layers.SelectedItem.ToString());
        Layers.Items.Remove(Layers.SelectedItem);
        OnLayersChanged?.Invoke(null, new EventArgs());
    }

    private void AddLayer()
    {
        var layer = _layers.Count + 1;
        var name = string.Format("Layer {0}", layer);
        var layerPictureBox = new PictureBox();
        layerPictureBox.Name = name;
        layerPictureBox.Width = preview.Width;
        layerPictureBox.Height = preview.Height;
        layerPictureBox.BackgroundImageLayout = ImageLayout.Stretch;
        layerPictureBox.BackColor = Color.Transparent;
        layerPictureBox.Location = new Point(0, 0);
        preview.Controls.Add(layerPictureBox);
        Layers.Items.Insert(0, name);
        Layers.SelectedItem = name;
        _layers[name] = new Bitmap(350, 350);

    }

    private void IconCreator_Load(object sender, EventArgs e)
    {
        AddLayer();
        OnLayersChanged += LayersChanged;
    }

    private void LayersChanged(object sender, EventArgs e)
    {
        var layers = new List<Bitmap>();
        layers.AddRange(_layers.Values);

        preview.BackgroundImage = CombineBitmaps.CombineAll(layers);
    }

    private void BtnAddImage_Click(object sender, EventArgs e)
    {
        using var openFileDialog = new OpenFileDialog
        {
            Title = "Import icon",
            CheckFileExists = true,
            CheckPathExists = true,
            DefaultExt = "png",
            Filter = "Image files (*.png;*.jpg;*.bmp;*.gif)|*.png;*.jpg;*.bmp;*.gif"
        };
        if (openFileDialog.ShowDialog() == DialogResult.OK)
        {
            var bitmap = new Bitmap(Path.GetFullPath(openFileDialog.FileName));
            bitmap = new Bitmap(bitmap, new Size(350, 350));
                    
            if (Layers.SelectedItem == null)
            {
                Layers.SelectedIndex = 0;
            }
            _layers[Layers.SelectedItem.ToString()] = bitmap;
            OnLayersChanged?.Invoke(bitmap, new EventArgs());
        }
        openFileDialog.Dispose();
    }

    private Image GetImageByName(string name)
    {
        foreach(var layerName in _layers.Keys)
        {
            if (layerName.Equals(name))
            {
                return _layers[layerName];
            }
        }
        return null;
    }

    private void BtnOk_Click(object sender, EventArgs e)
    {
        var layers = new List<Bitmap>();
        layers.AddRange(_layers.Values);

        _image = CombineBitmaps.CombineAll(layers);

        DialogResult = DialogResult.OK;
    }

    private void BtnBackgroundColor_Click(object sender, EventArgs e)
    {
        using var colorPicker = new ColorDialog();
        if (colorPicker.ShowDialog() == DialogResult.OK)
        {
            var bitmap = new Bitmap(350, 350);
            using (var gfx = Graphics.FromImage(bitmap))
            {
                using (var brush = new SolidBrush(colorPicker.Color))
                {
                    gfx.FillRectangle(brush, 0, 0, 350, 350);
                }
            }

            if (Layers.SelectedItem == null)
            {
                Layers.SelectedIndex = 0;
            }
            _layers[Layers.SelectedItem.ToString()] = bitmap;
            OnLayersChanged?.Invoke(bitmap, new EventArgs());
        }
    }
}