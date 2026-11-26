using System.Text.Json;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

/// <summary>A broker that serves nothing, so a test can hand one to a collaborator that only needs it to
/// exist. <see cref="InvalidatedWidgets" /> records the one member a test does assert on.</summary>
internal sealed class RecordingUiSessionBroker : IUiSessionBroker
{
	public UiSurface? LastOpenedSurface { get; private set; }

	public UiSessionOpenTicket Open(string providerId, UiSurface surface, string ownerPrincipal)
	{
		LastOpenedSurface = surface;
		return UiSessionOpenTicket.Rejected("not_supported", "not supported in this fake");
	}

	public Task<UiSessionOpenTicket> OpenAsync(
		string providerId,
		UiSurface surface,
		string ownerPrincipal,
		CancellationToken cancellationToken)
	{
		return Task.FromResult(Open(providerId, surface, ownerPrincipal));
	}

	public List<Guid> InvalidatedWidgets { get; } = [];

	public List<Guid> ClosedWidgets { get; } = [];

	public void InvalidateWidgetSessions(Guid widgetId)
	{
		InvalidatedWidgets.Add(widgetId);
	}

	public void CloseWidgetSessions(Guid widgetId, string reason)
	{
		ClosedWidgets.Add(widgetId);
	}

	public UiRawJson? Tree { get; set; }

	public TaskCompletionSource<UiRawJson?>? PendingTree { get; set; }

	public List<(string SessionId, UiSessionEventCommand Command)> HostEvents { get; } = [];

	public List<string> ClosedSessions { get; } = [];

	public async Task<UiRawJson?> FirstTreeAsync(string sessionId, CancellationToken cancellationToken)
	{
		if (Tree is { } tree)
		{
			return tree;
		}

		if (PendingTree is { } pending)
		{
			return await pending.Task.WaitAsync(cancellationToken);
		}

		await Task.Delay(Timeout.Infinite, cancellationToken);
		return null;
	}

	private readonly Lock _observerGate = new();

	private readonly Dictionary<string, List<IUiHostObserver>> _hostObservers = new(StringComparer.Ordinal);

	public List<string> HostResyncs { get; } = [];

	public Dictionary<string, UiRawJson> HostTrees { get; } = new(StringComparer.Ordinal);

	public IUiHostObservation? ObserveHost(string sessionId, IUiHostObserver observer)
	{
		lock (_observerGate)
		{
			if (!_hostObservers.TryGetValue(sessionId, out var observers))
			{
				observers = [];
				_hostObservers[sessionId] = observers;
			}

			observers.Add(observer);
		}

		if (HostTrees.TryGetValue(sessionId, out var tree))
		{
			observer.OnSnapshot(tree, 1);
		}

		return new HostObservation(this, sessionId, observer);
	}

	public IReadOnlyList<string> ObservedSessions
	{
		get
		{
			lock (_observerGate)
			{
				return [.. _hostObservers.Where(pair => pair.Value.Count > 0).Select(pair => pair.Key)];
			}
		}
	}

	public void PublishHostSnapshot(string sessionId, UiRawJson tree, int revision)
	{
		foreach (var observer in ObserversOf(sessionId))
		{
			observer.OnSnapshot(tree, revision);
		}
	}

	public void PublishHostPatch(string sessionId, UiRawJson patch, int fromRevision, int toRevision)
	{
		foreach (var observer in ObserversOf(sessionId))
		{
			observer.OnPatch(patch, fromRevision, toRevision);
		}
	}

	public void EndHostSession(string sessionId, bool retryable)
	{
		foreach (var observer in ObserversOf(sessionId))
		{
			observer.OnEnded(UiSessionEndReason.Invalidated, UiSessionErrorCodes.WidgetReconfigured, retryable);
		}
	}

	private IUiHostObserver[] ObserversOf(string sessionId)
	{
		lock (_observerGate)
		{
			return _hostObservers.TryGetValue(sessionId, out var observers) ? [.. observers] : [];
		}
	}

	private sealed class HostObservation(RecordingUiSessionBroker broker, string sessionId, IUiHostObserver observer)
		: IUiHostObservation
	{
		public void RequestResync()
		{
			lock (broker._observerGate)
			{
				broker.HostResyncs.Add(sessionId);
			}
		}

		public void Dispose()
		{
			lock (broker._observerGate)
			{
				if (broker._hostObservers.TryGetValue(sessionId, out var observers))
				{
					observers.Remove(observer);
				}
			}
		}
	}

	public bool DispatchHostEvent(string sessionId, UiSessionEventCommand command)
	{
		HostEvents.Add((sessionId, command));
		return true;
	}

	public Task CloseAsync(string sessionId, string reason, CancellationToken cancellationToken)
	{
		ClosedSessions.Add(sessionId);
		return Task.CompletedTask;
	}

	public bool CloseOwned(string? sessionId, string principal, string reason)
	{
		return false;
	}

	public UiAttachSessionResponse Attach(string? sessionId, string connectionId, string principal)
	{
		return new UiAttachSessionResponse
			{ Accepted = false, SessionId = string.Empty, Code = "not_supported", Message = null };
	}

	public void Detach(string? sessionId, string connectionId)
	{
	}

	public void DetachConnection(string connectionId)
	{
	}

	public UiSendEventResponse SendEvent(
		UiSendEventRequest request,
		string connectionId,
		string? actingClientId = null)
	{
		return new UiSendEventResponse { Accepted = false, Code = "not_supported", Message = null };
	}

	public void SweepDraining()
	{
	}

	public UiSessionIngestResult PublishSnapshot(string providerId, string sessionId, UiRawJson tree)
	{
		return new UiSessionIngestResult { Accepted = false, Code = "not_supported", Message = null };
	}

	public UiSessionIngestResult PublishPatch(string providerId, string sessionId, UiRawJson patch)
	{
		return new UiSessionIngestResult { Accepted = false, Code = "not_supported", Message = null };
	}

	public void PublishFault(string providerId, string sessionId, string code, string? message)
	{
	}

	public void PublishReload(string providerId, string sessionId)
	{
	}

	public FlushUiSessionResponse FlushSession(FlushUiSessionRequest request, string connectionId)
	{
		return new FlushUiSessionResponse { Accepted = false, Code = "not_supported", Message = null };
	}

	public UiSessionIngestResult PublishParameterValues(
		string providerId,
		string sessionId,
		IReadOnlyDictionary<string, JsonElement> values)
	{
		return new UiSessionIngestResult { Accepted = false, Code = "not_supported", Message = null };
	}
}
