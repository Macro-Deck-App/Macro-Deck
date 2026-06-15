using System.Drawing.Drawing2D;
using System.IO;
using SuchByte.MacroDeck.Utils;

namespace SuchByte.MacroDeck.Icons;

public class Icon
{
    private string _filePath;

    public string FilePath
    {
        get => _filePath;
        set => _filePath = value;
    }

    public string IconId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Loads the icon from disk on every access. The returned <see cref="Image"/> is a fresh
    /// instance and is NOT cached; the caller takes ownership and MUST dispose it.
    /// </summary>
    public Image IconImage
    {
        get
        {
            using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read);
            var ms = new MemoryStream();
            fs.CopyTo(ms);
            ms.Position = 0;
            return Image.FromStream(ms);
        }
    }

    /// <summary>
    /// Loads the icon downscaled to the requested size. Only the small thumbnail bitmap is
    /// retained, which keeps memory usage low when displaying many icons in a grid. The
    /// returned <see cref="Image"/> is a fresh instance; the caller takes ownership and MUST
    /// dispose it.
    /// </summary>
    public Image GetThumbnail(int width, int height)
    {
        using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read);
        using var source = Image.FromStream(fs);
        var thumbnail = new Bitmap(width, height);
        using var g = Graphics.FromImage(thumbnail);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(source, 0, 0, width, height);
        return thumbnail;
    }

    /// <summary>
    /// Computes the base64 representation on demand. Nothing is cached, so the icon data
    /// is never kept in memory permanently.
    /// </summary>
    public string IconBase64
    {
        get
        {
            using var image = IconImage;
            return Base64.GetBase64FromImage(image);
        }
    }

    public string IconHex128_64Base64
    {
        get
        {
            using var image = IconImage;
            return Base64.GetBase64ByteArray((Bitmap)image, new Size(128, 64));
        }
    }
}
