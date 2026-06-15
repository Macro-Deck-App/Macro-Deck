using System.Drawing.Drawing2D;
using System.IO;
using SuchByte.MacroDeck.Properties;
using SuchByte.MacroDeck.Utils;
using Icon = SuchByte.MacroDeck.Icons.Icon;

namespace SuchByte.MacroDeck.GUI.CustomControls;

/// <summary>
/// A virtualized, owner-drawn grid of icons. Only the icons currently scrolled into view are
/// decoded and drawn, which keeps switching between large icon packs fast and memory usage low.
/// Animated GIFs that are on screen are animated via <see cref="ImageAnimator"/>, so they play
/// at the speed defined by their frame delays.
/// </summary>
public class IconGrid : Panel
{
    private const int ItemSize = 100;
    private const int Spacing = 6;
    private const int Margin = 3;
    private const int CellSize = ItemSize + Spacing;

    private sealed class CachedImage
    {
        public Image Image;
        public Stream Stream;
        public bool IsGif;

        public void Dispose()
        {
            Image?.Dispose();
            Stream?.Dispose();
        }
    }

    private List<Icon> _icons = new();
    private readonly Dictionary<int, CachedImage> _cache = new();
    private readonly HashSet<int> _pending = new();
    private int _generation;

    private Icon _selectedIcon;
    private int _radius;

    public event EventHandler<Icon> IconSelected;

    public IconGrid()
    {
        DoubleBuffered = true;
        AutoScroll = true;
        BackColor = Color.FromArgb(45, 45, 45);
    }

    public Icon SelectedIcon
    {
        get => _selectedIcon;
        set
        {
            _selectedIcon = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Replaces the displayed icons. Any cached images and running GIF animations from the
    /// previous pack are released immediately.
    /// </summary>
    public void SetIcons(IList<Icon> icons)
    {
        Reset();
        _icons = icons != null ? new List<Icon>(icons) : new List<Icon>();
        UpdateScrollSize();
        // Reset scroll to the top when switching packs.
        AutoScrollPosition = new Point(0, 0);
        Invalidate();
    }

    public void ScrollToBottom()
    {
        UpdateScrollSize();
        AutoScrollPosition = new Point(0, AutoScrollMinSize.Height);
        Invalidate();
    }

    private void Reset()
    {
        _generation++;
        foreach (var cached in _cache.Values)
        {
            if (cached.IsGif)
            {
                GifAnimator.Unregister(cached.Image);
            }

            cached.Dispose();
        }

        _cache.Clear();
        _pending.Clear();
        _selectedIcon = null;
    }

    private int Columns => Math.Max(1, (ClientSize.Width - Margin) / CellSize);

    private void UpdateScrollSize()
    {
        var rows = (int)Math.Ceiling(_icons.Count / (double)Columns);
        AutoScrollMinSize = new Size(0, rows * CellSize + Margin * 2);
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        UpdateScrollSize();
        Invalidate();
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_icons.Count == 0)
        {
            return;
        }

        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var columns = Columns;
        var scroll = AutoScrollPosition;

        var firstRow = Math.Max(0, (-scroll.Y) / CellSize);
        var lastRow = (-scroll.Y + Height) / CellSize;
        var first = firstRow * columns;
        var last = Math.Min(_icons.Count - 1, (lastRow + 1) * columns - 1);

        EnsureImages(first, last, columns);

        for (var i = first; i <= last; i++)
        {
            var row = i / columns;
            var col = i % columns;
            var x = Margin + col * CellSize + scroll.X;
            var y = Margin + row * CellSize + scroll.Y;
            DrawItem(g, i, x, y);
        }
    }

    private void DrawItem(Graphics g, int index, int x, int y)
    {
        var rect = new Rectangle(x, y, ItemSize, ItemSize);
        var radius = (int)(_radius / 100.0f * ItemSize);

        using var path = GetFigurePath(rect, radius);

        using (var bgBrush = new SolidBrush(Color.FromArgb(35, 35, 35)))
        {
            g.FillPath(bgBrush, path);
        }

        var cached = _cache.TryGetValue(index, out var c) ? c : null;
        if (cached?.Image != null)
        {
            using var savedClip = g.Clip;
            g.SetClip(path);
            g.DrawImage(cached.Image, rect);
            g.Clip = savedClip;

            if (cached.IsGif)
            {
                var gifRect = new Rectangle(x + ItemSize - radius / 2 - 27, y + 2, 25, 14);
                g.DrawImage(Resources.gif, gifRect);
            }
        }

        if (_selectedIcon != null && index < _icons.Count && ReferenceEquals(_icons[index], _selectedIcon))
        {
            using var pen = new Pen(Color.FromArgb(0, 89, 184), 3);
            g.DrawPath(pen, path);
        }
    }

    private void EnsureImages(int first, int last, int columns)
    {
        var loadFirst = Math.Max(0, first - columns);
        var loadLast = Math.Min(_icons.Count - 1, last + columns);
        for (var i = loadFirst; i <= loadLast; i++)
        {
            if (!_cache.ContainsKey(i) && _pending.Add(i))
            {
                BeginDecode(i);
            }
        }

        // Release images well outside the visible range to bound memory for large packs.
        var keepFirst = first - columns * 3;
        var keepLast = last + columns * 3;
        var stale = _cache.Keys.Where(i => i < keepFirst || i > keepLast).ToArray();
        foreach (var i in stale)
        {
            EvictImage(i);
        }
    }

    private void BeginDecode(int index)
    {
        var generation = _generation;
        var icon = _icons[index];
        Task.Run(() =>
        {
            CachedImage decoded = null;
            try
            {
                decoded = Decode(icon);
            }
            catch
            {
                decoded = null;
            }

            try
            {
                BeginInvoke(() =>
                {
                    _pending.Remove(index);
                    if (generation != _generation || index >= _icons.Count ||
                        !ReferenceEquals(_icons[index], icon))
                    {
                        decoded?.Dispose();
                        return;
                    }

                    if (decoded == null)
                    {
                        return;
                    }

                    _cache[index] = decoded;
                    if (decoded.IsGif)
                    {
                        GifAnimator.Register(decoded.Image, Invalidate);
                    }

                    Invalidate();
                });
            }
            catch (ObjectDisposedException)
            {
                decoded?.Dispose();
            }
            catch (InvalidOperationException)
            {
                // Handle not created / control disposed.
                decoded?.Dispose();
            }
        });
    }

    private static CachedImage Decode(Icon icon)
    {
        var isGif = Path.GetExtension(icon.FilePath)?.ToLowerInvariant() == ".gif";
        if (!isGif)
        {
            // Static icons are downscaled to the cell size to keep memory low.
            return new CachedImage
            {
                Image = icon.GetThumbnail(ItemSize, ItemSize),
                IsGif = false
            };
        }

        // Animated GIFs need their original multi-frame image kept alive (and the backing
        // stream must stay open) so ImageAnimator can play every frame.
        var bytes = File.ReadAllBytes(icon.FilePath);
        var stream = new MemoryStream(bytes);
        var image = Image.FromStream(stream);
        return new CachedImage
        {
            Image = image,
            Stream = stream,
            IsGif = true
        };
    }

    private void EvictImage(int index)
    {
        if (!_cache.TryGetValue(index, out var cached))
        {
            return;
        }

        if (cached.IsGif)
        {
            GifAnimator.Unregister(cached.Image);
        }

        cached.Dispose();
        _cache.Remove(index);
    }

    private int HitTest(Point location)
    {
        var scroll = AutoScrollPosition;
        var x = location.X - Margin - scroll.X;
        var y = location.Y - Margin - scroll.Y;
        if (x < 0 || y < 0)
        {
            return -1;
        }

        var columns = Columns;
        var col = x / CellSize;
        var row = y / CellSize;
        if (col >= columns)
        {
            return -1;
        }

        // Ignore clicks that land in the spacing between items.
        if (x % CellSize > ItemSize || y % CellSize > ItemSize)
        {
            return -1;
        }

        var index = row * columns + col;
        return index >= 0 && index < _icons.Count ? index : -1;
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        var index = HitTest(e.Location);
        if (index < 0)
        {
            return;
        }

        _selectedIcon = _icons[index];
        Invalidate();
        IconSelected?.Invoke(this, _selectedIcon);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = HitTest(e.Location) >= 0 ? Cursors.Hand : Cursors.Default;
    }

    private static GraphicsPath GetFigurePath(Rectangle rect, float radius)
    {
        var path = new GraphicsPath();
        if (radius <= 2)
        {
            path.AddRectangle(rect);
            return path;
        }

        var curveSize = radius;
        path.StartFigure();
        path.AddArc(rect.X, rect.Y, curveSize, curveSize, 180, 90);
        path.AddArc(rect.Right - curveSize, rect.Y, curveSize, curveSize, 270, 90);
        path.AddArc(rect.Right - curveSize, rect.Bottom - curveSize, curveSize, curveSize, 0, 90);
        path.AddArc(rect.X, rect.Bottom - curveSize, curveSize, curveSize, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// Corner radius of the icon cells, on the same 0-100 scale as <see cref="RoundedButton.Radius"/>.
    /// </summary>
    public int Radius
    {
        get => _radius;
        set
        {
            _radius = value;
            Invalidate();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Reset();
        }

        base.Dispose(disposing);
    }
}
