using MacroDeck.Ui.Model.Resources;

namespace MacroDeck.Sdk.Ui;

internal sealed class UnsupportedUiResourceRegistry : IUiResourceRegistry
{
	public static readonly UnsupportedUiResourceRegistry Instance = new();

	public Task<UiResource> RegisterAsync(string name,
		ReadOnlyMemory<byte> content,
		string mediaType,
		CancellationToken cancellationToken = default)
		=> Task.FromException<UiResource>(Unsupported());

	public Task RemoveAsync(string name, CancellationToken cancellationToken = default)
		=> Task.FromException(Unsupported());

	private static UiResourceException Unsupported()
		=> new(UiResourceErrorCode.Unsupported, "This context cannot register UI resources.");
}
