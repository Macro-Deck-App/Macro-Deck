using MacroDeck.Ui.Model.Surfaces;

namespace MacroDeckHost.Application.Ui.Sessions;

public interface IUiEventOriginTracker
{
	bool IsClientAttached(string providerId, string sessionId, string clientId);

	UiSurface? SurfaceOf(string sessionId);

	string? TakeRecentEventConnection(string providerId, string sessionId, string clientId, TimeSpan within);
}
