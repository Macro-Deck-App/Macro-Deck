using MacroDeck.Ui.Model.Resources;

namespace MacroDeckHost.Application.StreamStats;

public interface IStreamThumbnails
{
	event EventHandler<string>? Changed;

	UiResource? Find(string accountId);

	void Track(string accountId, string? url);
}
