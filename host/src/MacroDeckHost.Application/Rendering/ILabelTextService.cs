using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

namespace MacroDeckHost.Application.Rendering;

public interface ILabelTextService
{
	Task<string?> ResolveText(Guid widgetId, string state, CancellationToken cancellationToken = default);

	Task<string?> ResolvePreview(LabelImagePreviewRequest request, CancellationToken cancellationToken = default);
}
