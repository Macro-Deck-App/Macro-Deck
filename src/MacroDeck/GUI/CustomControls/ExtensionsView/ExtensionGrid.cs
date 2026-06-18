using System.Drawing.Drawing2D;

namespace SuchByte.MacroDeck.GUI.CustomControls.ExtensionsView;

/// <summary>
/// A virtualized, owner-drawn grid of extension cards modeled on <see cref="IconGrid"/>. Only the
/// cards currently scrolled into view are painted, and their icons are decoded off the UI thread
/// into a bounded cache, which keeps the extensions page fast and memory usage low regardless of
/// how many extensions are listed. Card buttons/links are drawn by the grid and dispatched via
/// hit-testing, so there are no per-item child controls.
/// </summary>
public class ExtensionGrid : Panel
{
    private const int CardWidth = 300;
    private const int CardHeight = 150;
    private const int Spacing = 12;
    private const int Margin = 12;
    private const int CellW = CardWidth + Spacing;
    private const int CellH = CardHeight + Spacing;

    private const int IconSize = 50;
    private const int Pad = 12;
    private const int TextLeft = Pad + IconSize + 12;
    private const int ButtonHeight = 28;
    private const int CornerRadius = 14;

    private sealed class CachedImage
    {
        public Image Image;
        public bool Owned;

        public void Dispose()
        {
            if (Owned)
            {
                Image?.Dispose();
            }
        }
    }

    private List<ExtensionCard> _cards = new();
    private readonly Dictionary<int, CachedImage> _cache = new();
    private readonly HashSet<int> _pending = new();
    private int _generation;

    private readonly List<(Rectangle Bounds, CardAction Action)> _actionRegions = new();
    private CardAction _hoveredAction;

    private bool _loading;

    private readonly Font _titleFont;
    private readonly Font _subtitleFont;
    private readonly Font _descriptionFont;
    private readonly Font _badgeFont;
    private readonly Font _buttonFont;
    private readonly Font _messageFont;

    public ExtensionGrid()
    {
        DoubleBuffered = true;
        AutoScroll = true;
        BackColor = Color.FromArgb(45, 45, 45);

        _titleFont = new Font("Tahoma", 11.25f, FontStyle.Bold);
        _subtitleFont = new Font("Tahoma", 8.25f);
        _descriptionFont = new Font("Tahoma", 9.75f);
        _badgeFont = new Font("Tahoma", 8.25f);
        _buttonFont = new Font("Tahoma", 9.75f);
        _messageFont = new Font("Tahoma", 12f);
    }

    /// <summary>When true a centered "Loading…" message is drawn over the grid.</summary>
    public bool Loading
    {
        get => _loading;
        set
        {
            if (_loading == value)
            {
                return;
            }

            _loading = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Replaces the displayed cards. Any cached images from the previous set are released.
    /// </summary>
    public void SetCards(IList<ExtensionCard> cards)
    {
        Reset();
        _cards = cards != null ? new List<ExtensionCard>(cards) : new List<ExtensionCard>();
        UpdateScrollSize();
        AutoScrollPosition = new Point(0, 0);
        Invalidate();
    }

    private void Reset()
    {
        _generation++;
        foreach (var cached in _cache.Values)
        {
            cached.Dispose();
        }

        _cache.Clear();
        _pending.Clear();
        _actionRegions.Clear();
        _hoveredAction = null;
    }

    private int Columns => Math.Max(1, (ClientSize.Width - Margin) / CellW);

    private void UpdateScrollSize()
    {
        var rows = (int)Math.Ceiling(_cards.Count / (double)Columns);
        AutoScrollMinSize = new Size(0, rows * CellH + Margin * 2);
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
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        _actionRegions.Clear();

        if (_cards.Count > 0)
        {
            var columns = Columns;
            var scroll = AutoScrollPosition;

            var firstRow = Math.Max(0, -scroll.Y / CellH);
            var lastRow = (-scroll.Y + Height) / CellH;
            var first = firstRow * columns;
            var last = Math.Min(_cards.Count - 1, (lastRow + 1) * columns - 1);

            EnsureImages(first, last, columns);

            for (var i = first; i <= last; i++)
            {
                var row = i / columns;
                var col = i % columns;
                var x = Margin + col * CellW + scroll.X;
                var y = Margin + row * CellH + scroll.Y;
                DrawCard(g, _cards[i], i, x, y);
            }
        }

        if (_loading)
        {
            DrawCenteredMessage(g, "Loading…");
        }
        else if (_cards.Count == 0)
        {
            DrawCenteredMessage(g, "No extensions found");
        }
    }

    private void DrawCenteredMessage(Graphics g, string text)
    {
        var size = g.MeasureString(text, _messageFont);
        using var brush = new SolidBrush(Color.Silver);
        g.DrawString(text, _messageFont, brush,
            (ClientSize.Width - size.Width) / 2f,
            (ClientSize.Height - size.Height) / 2f);
    }

    private void DrawCard(Graphics g, ExtensionCard card, int index, int x, int y)
    {
        var rect = new Rectangle(x, y, CardWidth, CardHeight);
        using var path = GetFigurePath(rect, CornerRadius);

        using (var bgBrush = new SolidBrush(card.BackgroundTint))
        {
            g.FillPath(bgBrush, path);
        }

        // Icon
        var iconRect = new Rectangle(x + Pad, y + Pad, IconSize, IconSize);
        var cached = _cache.TryGetValue(index, out var c) ? c : null;
        if (cached?.Image != null)
        {
            using var iconPath = GetFigurePath(iconRect, 8);
            var savedClip = g.Clip;
            g.SetClip(iconPath);
            g.DrawImage(cached.Image, iconRect);
            g.Clip = savedClip;
        }

        var textLeft = x + TextLeft;
        var textRight = x + CardWidth - Pad;

        // Type badge (top-right). Drawn before the title so the title can be clipped to stop
        // short of it and the two never overlap.
        var titleRight = textRight;
        if (!string.IsNullOrEmpty(card.BadgeText))
        {
            var badgeSize = TextRenderer.MeasureText(card.BadgeText, _badgeFont);
            var badgeW = badgeSize.Width + 16;
            var badgeRect = new Rectangle(textRight - badgeW, y + Pad, badgeW, 20);
            using (var badgePath = GetFigurePath(badgeRect, 8))
            using (var badgeBrush = new SolidBrush(card.BadgeColor))
            {
                g.FillPath(badgeBrush, badgePath);
            }

            TextRenderer.DrawText(g, card.BadgeText, _badgeFont, badgeRect, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            titleRight = badgeRect.Left - 8;
        }

        // Title
        var titleRect = new Rectangle(textLeft, y + Pad, Math.Max(0, titleRight - textLeft), 22);
        TextRenderer.DrawText(g, card.Title, _titleFont, titleRect, Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);

        var textWidth = textRight - textLeft;

        // Subtitle (author / version)
        var subtitleRect = new Rectangle(textLeft, y + Pad + 26, textWidth, 18);
        TextRenderer.DrawText(g, card.Subtitle, _subtitleFont, subtitleRect, Color.LightGray,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);

        // Description (store) or status (installed)
        var bodyRect = new Rectangle(textLeft, y + Pad + 46, textWidth, 42);
        if (!string.IsNullOrEmpty(card.Description))
        {
            TextRenderer.DrawText(g, card.Description, _descriptionFont, bodyRect, Color.White,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
        else if (!string.IsNullOrEmpty(card.StatusText))
        {
            var statusRect = new Rectangle(textLeft, y + Pad + 46, textWidth, 20);
            TextRenderer.DrawText(g, card.StatusText, _descriptionFont, statusRect, card.StatusColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }

        DrawActions(g, card, x, y);
    }

    private void DrawActions(Graphics g, ExtensionCard card, int x, int y)
    {
        var actionsTop = y + CardHeight - ButtonHeight - Pad;
        var rightEdge = x + CardWidth - Pad;
        var leftEdge = x + Pad;

        foreach (var action in card.Actions)
        {
            var textSize = TextRenderer.MeasureText(action.Label, _buttonFont);

            if (action.Style == CardActionStyle.Link)
            {
                var linkRect = new Rectangle(leftEdge, actionsTop, textSize.Width + 4, ButtonHeight);
                var hovered = ReferenceEquals(_hoveredAction, action);
                var color = !action.Enabled
                    ? Color.Gray
                    : hovered
                        ? Color.FromArgb(0, 162, 255)
                        : Color.White;
                TextRenderer.DrawText(g, action.Label, _buttonFont, linkRect, color,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                leftEdge += linkRect.Width + 8;
                if (action.Enabled)
                {
                    _actionRegions.Add((linkRect, action));
                }

                continue;
            }

            var buttonW = textSize.Width + 28;
            var buttonRect = new Rectangle(rightEdge - buttonW, actionsTop, buttonW, ButtonHeight);
            var baseColor = action.Style == CardActionStyle.Accent
                ? Color.FromArgb(20, 153, 0)
                : Color.FromArgb(0, 103, 225);
            Color fill;
            if (!action.Enabled)
            {
                fill = Color.FromArgb(70, 70, 70);
            }
            else if (ReferenceEquals(_hoveredAction, action))
            {
                fill = ControlPaint.Light(baseColor, 0.2f);
            }
            else
            {
                fill = baseColor;
            }

            using (var buttonPath = GetFigurePath(buttonRect, 8))
            using (var buttonBrush = new SolidBrush(fill))
            {
                g.FillPath(buttonBrush, buttonPath);
            }

            TextRenderer.DrawText(g, action.Label, _buttonFont, buttonRect,
                action.Enabled ? Color.White : Color.Silver,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            rightEdge -= buttonW + 8;
            if (action.Enabled)
            {
                _actionRegions.Add((buttonRect, action));
            }
        }
    }

    private void EnsureImages(int first, int last, int columns)
    {
        var loadFirst = Math.Max(0, first - columns);
        var loadLast = Math.Min(_cards.Count - 1, last + columns);
        for (var i = loadFirst; i <= loadLast; i++)
        {
            if (!_cache.ContainsKey(i) && _pending.Add(i))
            {
                BeginDecode(i);
            }
        }

        // Release images well outside the visible range to bound memory for large lists.
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
        var card = _cards[index];
        if (card.LoadIconAsync == null)
        {
            _pending.Remove(index);
            return;
        }

        Task.Run(async () =>
        {
            Image image = null;
            try
            {
                image = await card.LoadIconAsync(default);
            }
            catch
            {
                image = null;
            }

            try
            {
                BeginInvoke(() =>
                {
                    _pending.Remove(index);
                    if (generation != _generation || index >= _cards.Count ||
                        !ReferenceEquals(_cards[index], card))
                    {
                        if (card.DisposableIcon)
                        {
                            image?.Dispose();
                        }

                        return;
                    }

                    if (image == null)
                    {
                        return;
                    }

                    _cache[index] = new CachedImage { Image = image, Owned = card.DisposableIcon };
                    Invalidate();
                });
            }
            catch (ObjectDisposedException)
            {
                if (card.DisposableIcon)
                {
                    image?.Dispose();
                }
            }
            catch (InvalidOperationException)
            {
                // Handle not created / control disposed.
                if (card.DisposableIcon)
                {
                    image?.Dispose();
                }
            }
        });
    }

    private void EvictImage(int index)
    {
        if (!_cache.TryGetValue(index, out var cached))
        {
            return;
        }

        cached.Dispose();
        _cache.Remove(index);
    }

    private CardAction HitTest(Point location)
    {
        for (var i = _actionRegions.Count - 1; i >= 0; i--)
        {
            if (_actionRegions[i].Bounds.Contains(location))
            {
                return _actionRegions[i].Action;
            }
        }

        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var action = HitTest(e.Location);
        Cursor = action != null ? Cursors.Hand : Cursors.Default;
        if (!ReferenceEquals(action, _hoveredAction))
        {
            _hoveredAction = action;
            Invalidate();
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        HitTest(e.Location)?.OnClick?.Invoke();
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Reset();
            _titleFont.Dispose();
            _subtitleFont.Dispose();
            _descriptionFont.Dispose();
            _badgeFont.Dispose();
            _buttonFont.Dispose();
            _messageFont.Dispose();
        }

        base.Dispose(disposing);
    }
}
