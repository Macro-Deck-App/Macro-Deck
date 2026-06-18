namespace SuchByte.MacroDeck.Utils;

/// <summary>
/// A small process-wide, bounded cache of downloaded extension-store icon bytes keyed by package id.
/// Paging back and forth (or re-opening the store) reuses the bytes instead of re-downloading them.
/// Raw bytes are cached rather than <see cref="Image"/>s so each consumer can decode its own
/// short-lived bitmap and dispose it freely without affecting the cache.
/// </summary>
public static class ExtensionIconCache
{
    private const int MaxEntries = 256;

    private static readonly object Lock = new();
    private static readonly Dictionary<string, byte[]> Cache = new();
    private static readonly LinkedList<string> Lru = new();

    public static bool TryGet(string key, out byte[] bytes)
    {
        lock (Lock)
        {
            if (Cache.TryGetValue(key, out bytes))
            {
                Lru.Remove(key);
                Lru.AddFirst(key);
                return true;
            }

            return false;
        }
    }

    public static void Set(string key, byte[] bytes)
    {
        lock (Lock)
        {
            if (Cache.ContainsKey(key))
            {
                Cache[key] = bytes;
                Lru.Remove(key);
                Lru.AddFirst(key);
                return;
            }

            Cache[key] = bytes;
            Lru.AddFirst(key);

            while (Lru.Count > MaxEntries)
            {
                var oldest = Lru.Last!.Value;
                Lru.RemoveLast();
                Cache.Remove(oldest);
            }
        }
    }
}
