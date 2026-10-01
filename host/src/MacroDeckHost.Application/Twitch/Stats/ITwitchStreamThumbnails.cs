using MacroDeck.Ui.Model.Resources;

namespace MacroDeckHost.Application.Twitch.Stats;

public interface ITwitchStreamThumbnails
{
	event EventHandler<string>? Changed;

	UiResource? Find(string userId);

	void Track(string userId, string? url);
}
