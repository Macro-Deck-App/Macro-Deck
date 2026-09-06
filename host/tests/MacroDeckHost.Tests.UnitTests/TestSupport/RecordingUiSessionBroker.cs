using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

/// <summary>A broker that serves nothing, so a test can hand one to a collaborator that only needs it to
/// exist. <see cref="InvalidatedWidgets" /> records the one member a test does assert on.</summary>
internal sealed class RecordingUiSessionBroker : IUiSessionBroker
{
	public UiSessionOpenTicket Open(string providerId, UiSurface surface, string ownerPrincipal)
		=> UiSessionOpenTicket.Rejected("not_supported", "not supported in this fake");

	public Task<UiSessionOpenTicket> OpenAsync(string providerId,
		UiSurface surface,
		string ownerPrincipal,
		CancellationToken cancellationToken)
		=> Task.FromResult(Open(providerId, surface, ownerPrincipal));

	public List<Guid> InvalidatedWidgets { get; } = [];

	public List<Guid> ClosedWidgets { get; } = [];

	public void InvalidateWidgetSessions(Guid widgetId) => InvalidatedWidgets.Add(widgetId);

	public void CloseWidgetSessions(Guid widgetId, string reason) => ClosedWidgets.Add(widgetId);

	public Task CloseAsync(string sessionId, string reason, CancellationToken cancellationToken)
		=> Task.CompletedTask;

	public bool CloseOwned(string? sessionId, string principal, string reason) => false;

	public UiAttachSessionResponse Attach(string? sessionId, string connectionId, string principal)
		=> new() { Accepted = false, SessionId = string.Empty, Code = "not_supported", Message = null };

	public void Detach(string? sessionId, string connectionId)
	{
	}

	public void DetachConnection(string connectionId)
	{
	}

	public UiSendEventResponse SendEvent(UiSendEventRequest request,
		string connectionId,
		string? actingClientId = null)
		=> new() { Accepted = false, Code = "not_supported", Message = null };

	public void SweepDraining()
	{
	}

	public UiSessionIngestResult PublishSnapshot(string providerId, string sessionId, UiRawJson tree)
		=> new() { Accepted = false, Code = "not_supported", Message = null };

	public UiSessionIngestResult PublishPatch(string providerId, string sessionId, UiRawJson patch)
		=> new() { Accepted = false, Code = "not_supported", Message = null };

	public void PublishFault(string providerId, string sessionId, string code, string? message)
	{
	}
}
