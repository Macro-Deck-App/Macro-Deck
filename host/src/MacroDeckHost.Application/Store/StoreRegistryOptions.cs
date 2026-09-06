namespace MacroDeckHost.Application.Store;

public sealed record StoreRegistryOptions
{
	public static readonly StoreRegistryOptions Default = new();

	public Uri BaseUrl { get; init; } =
		new("https://raw.githubusercontent.com/Macro-Deck-App/Macro-Deck-Store-Registry/main/");

	public TimeSpan RefreshInterval { get; init; } = TimeSpan.FromHours(1);

	public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

	public TimeSpan DownloadTimeout { get; init; } = TimeSpan.FromMinutes(10);

	// A validly signed snapshot stays valid forever, so an origin that simply stops updating can
	// suppress revocations and security releases indefinitely without failing any signature check.
	// Snapshots older than this are reported stale even when every signature verifies.
	public TimeSpan MaxSnapshotAge { get; init; } = TimeSpan.FromDays(14);

	public long MaxRegistryFileBytes { get; init; } = 2L * 1024 * 1024;

	public long MaxRegistryTotalBytes { get; init; } = 64L * 1024 * 1024;

	public int MaxRegistryFiles { get; init; } = 10_000;

	public long MaxArtifactBytes { get; init; } = 256L * 1024 * 1024;

	public long MaxMediaBytes { get; init; } = 8L * 1024 * 1024;

	// Only tests set this; production always anchors on the pinned MacroDeckRootKey.
	public ReadOnlyMemory<byte>? RootPublicKeyOverride { get; init; }
}
