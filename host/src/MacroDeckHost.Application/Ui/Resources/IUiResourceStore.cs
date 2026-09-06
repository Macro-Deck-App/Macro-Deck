using MacroDeck.Ui.Model.Resources;

namespace MacroDeckHost.Application.Ui.Resources;

public sealed record UiResourceRegistration
{
	public required string OwnerId { get; init; }

	public required string Name { get; init; }

	public required string MediaType { get; init; }

	public required ReadOnlyMemory<byte> Content { get; init; }
}

public sealed record UiResourceContent
{
	public required ReadOnlyMemory<byte> Content { get; init; }

	public required string MediaType { get; init; }

	public required string ContentHash { get; init; }
}

public interface IUiResourceStore
{
	UiResource Register(UiResourceRegistration registration);

	bool TryGet(string resourceId, out UiResourceContent content);
}
