using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;

namespace MacroDeckHost.Tests.PluginContractTests.Harness;

internal sealed class AcceptingWidgetUiSessionOpener : IWidgetUiSessionOpener
{
	public UiSessionOpenTicket Open(OpenWidgetUiSessionRequest request, string ownerPrincipal, bool isAdmin)
		=> UiSessionOpenTicket.Opened($"tile-tree-{request.WidgetId}");
}

internal sealed class PendingTreeUiSessionBroker : IUiSessionBroker
{
	public TaskCompletionSource<UiRawJson?> Tree { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public async Task<UiRawJson?> FirstTreeAsync(string sessionId, CancellationToken cancellationToken)
		=> await Tree.Task.WaitAsync(cancellationToken);

	public bool DispatchHostEvent(string sessionId, UiSessionEventCommand command) => true;

	public UiSessionOpenTicket Open(string providerId, UiSurface surface, string ownerPrincipal)
		=> UiSessionOpenTicket.Rejected("not_supported", "not supported in this double");

	public Task<UiSessionOpenTicket> OpenAsync(string providerId,
		UiSurface surface,
		string ownerPrincipal,
		CancellationToken cancellationToken)
		=> Task.FromResult(Open(providerId, surface, ownerPrincipal));

	public Task CloseAsync(string sessionId, string reason, CancellationToken cancellationToken) => Task.CompletedTask;

	public void InvalidateWidgetSessions(Guid widgetId)
	{
	}

	public void CloseWidgetSessions(Guid widgetId, string reason)
	{
	}

	public bool CloseOwned(string? sessionId, string principal, string reason) => false;

	public UiAttachSessionResponse Attach(string? sessionId, string connectionId, string principal)
		=> new() { Accepted = false, SessionId = string.Empty, Code = "not_supported", Message = null };

	public void Detach(string? sessionId, string connectionId)
	{
	}

	public void DetachConnection(string connectionId)
	{
	}

	public UiSendEventResponse SendEvent(UiSendEventRequest request, string connectionId, string? actingClientId = null)
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
