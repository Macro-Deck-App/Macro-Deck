using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

namespace MacroDeckHost.Application.Rendering;

public interface ILabelTextService
{
	Task<LabelTexts> ResolveTexts(Guid widgetId, string state, CancellationToken cancellationToken = default);

	Task<string?> ResolvePreview(LabelImagePreviewRequest request, CancellationToken cancellationToken = default);
}

public sealed record LabelTexts(string? Primary, IReadOnlyList<string?> Extras)
{
	public static LabelTexts None { get; } = new(null, []);
}
