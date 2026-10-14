using MacroDeck.Ui.Model.Resources;

namespace MacroDeck.Sdk.Ui;

internal sealed class UnsupportedUiResourceRegistry : IUiResourceRegistry
{
	public static readonly UnsupportedUiResourceRegistry Instance = new();

	public Task<UiResource> RegisterAsync(
		string name,
		ReadOnlyMemory<byte> content,
		string mediaType,
		CancellationToken cancellationToken = default)
	{
		return Task.FromException<UiResource>(Unsupported());
	}

	public Task RemoveAsync(string name, CancellationToken cancellationToken = default)
	{
		return Task.FromException(Unsupported());
	}

	public Task<UiResource> GetPluginIconAsync(string key, string name, CancellationToken cancellationToken = default)
	{
		return Task.FromException<UiResource>(Unsupported());
	}

	public Task<UiResource> GetIconAsync(Guid iconId, CancellationToken cancellationToken = default)
	{
		return Task.FromException<UiResource>(Unsupported());
	}

	public Task<UiResource?> RegisterMusicPlayerArtworkAsync(
		string name,
		string instanceId,
		string artworkId,
		CancellationToken cancellationToken = default)
	{
		return Task.FromException<UiResource?>(Unsupported());
	}

	private static UiResourceException Unsupported()
	{
		return new UiResourceException(UiResourceErrorCode.Unsupported, "This context cannot register UI resources.");
	}
}
