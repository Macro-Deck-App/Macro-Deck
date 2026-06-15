using System.Drawing.Imaging;

namespace SuchByte.MacroDeck.Utils;

/// <summary>
/// Drives animated-GIF playback for any number of <see cref="Image"/> instances from a single
/// shared UI timer. Unlike <see cref="ImageAnimator"/> it honors each frame's delay and clamps
/// very small delays to 100ms, matching how web browsers play GIFs (GIFs frequently encode a
/// frame delay of 0, expecting the renderer to clamp it). Consumers register the image together
/// with an invalidate callback; the animator selects the active frame and triggers a redraw.
/// </summary>
public static class GifAnimator
{
    private sealed class Entry
    {
        public Image Image;
        public int[] DelaysMs;
        public int Frame;
        public int ElapsedMs;
        public Action Invalidate;
    }

    private const int TickMs = 20;

    // GIFs frequently encode a frame delay of 0 (or 1 centisecond) and rely on the renderer to
    // substitute a sane default. Browsers clamp delays at or below ~10ms to 100ms but honor
    // anything faster, so we do the same.
    private const int NearZeroThresholdMs = 10;
    private const int ClampedDelayMs = 100;

    private const int PropertyTagFrameDelay = 0x5100;

    private static readonly List<Entry> Entries = new();
    private static System.Windows.Forms.Timer _timer;

    public static bool IsAnimated(Image image)
    {
        if (image == null)
        {
            return false;
        }

        try
        {
            return image.GetFrameCount(FrameDimension.Time) > 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Starts animating <paramref name="image"/>. <paramref name="invalidate"/> is called on the
    /// UI thread whenever a new frame becomes active. Does nothing for non-animated images or
    /// images that are already registered. Must be called on the UI thread.
    /// </summary>
    public static void Register(Image image, Action invalidate)
    {
        if (image == null || invalidate == null)
        {
            return;
        }

        if (Entries.Any(e => ReferenceEquals(e.Image, image)))
        {
            return;
        }

        int[] delays;
        try
        {
            delays = GetFrameDelays(image);
        }
        catch
        {
            return;
        }

        if (delays.Length <= 1)
        {
            return;
        }

        Entries.Add(new Entry { Image = image, DelaysMs = delays, Invalidate = invalidate });
        EnsureTimerRunning();
    }

    public static void Unregister(Image image)
    {
        if (image == null)
        {
            return;
        }

        Entries.RemoveAll(e => ReferenceEquals(e.Image, image));
        if (Entries.Count == 0)
        {
            _timer?.Stop();
        }
    }

    private static void EnsureTimerRunning()
    {
        if (_timer == null)
        {
            _timer = new System.Windows.Forms.Timer { Interval = TickMs };
            _timer.Tick += OnTick;
        }

        if (!_timer.Enabled)
        {
            _timer.Start();
        }
    }

    private static void OnTick(object sender, EventArgs e)
    {
        for (var i = Entries.Count - 1; i >= 0; i--)
        {
            if (i >= Entries.Count)
            {
                continue;
            }

            var entry = Entries[i];
            entry.ElapsedMs += TickMs;
            if (entry.ElapsedMs < entry.DelaysMs[entry.Frame])
            {
                continue;
            }

            entry.ElapsedMs = 0;
            entry.Frame = (entry.Frame + 1) % entry.DelaysMs.Length;
            try
            {
                entry.Image.SelectActiveFrame(FrameDimension.Time, entry.Frame);
                entry.Invalidate();
            }
            catch
            {
                // The image was disposed without unregistering; drop it.
                Entries.RemoveAt(i);
            }
        }

        if (Entries.Count == 0)
        {
            _timer?.Stop();
        }
    }

    private static int[] GetFrameDelays(Image image)
    {
        var frameCount = image.GetFrameCount(FrameDimension.Time);
        var delays = new int[frameCount];

        int[] raw = null;
        try
        {
            var item = image.GetPropertyItem(PropertyTagFrameDelay);
            if (item?.Value != null)
            {
                raw = new int[item.Value.Length / 4];
                for (var i = 0; i < raw.Length; i++)
                {
                    raw[i] = BitConverter.ToInt32(item.Value, i * 4);
                }
            }
        }
        catch
        {
            raw = null;
        }

        for (var i = 0; i < frameCount; i++)
        {
            // Frame delays are stored in hundredths of a second.
            var ms = (raw != null && i < raw.Length ? raw[i] : 0) * 10;
            if (ms <= NearZeroThresholdMs)
            {
                ms = ClampedDelayMs;
            }

            delays[i] = ms;
        }

        return delays;
    }
}
