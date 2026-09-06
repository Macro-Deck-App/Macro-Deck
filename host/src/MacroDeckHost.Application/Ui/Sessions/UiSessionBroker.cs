using System.Collections.Concurrent;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Model.Versioning;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using Serilog;

namespace MacroDeckHost.Application.Ui.Sessions;

public sealed class UiSessionBroker : IUiSessionBroker, IDisposable
{
	private const string ProviderFaultMessage = "The provider of this view stopped responding.";

	private const string ProviderDisconnectedMessage = "The plugin serving this view disconnected.";

	private readonly IUiSessionProviderResolver _resolver;
	private readonly IUiTransport _transport;
	private readonly UiSessionRegistry _registry;
	private readonly IPluginSessionRegistry _pluginSessions;
	private readonly IIntegrationRegistry _integrations;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	private readonly ConcurrentDictionary<string, SessionContext> _contexts = new(StringComparer.Ordinal);

	public UiSessionBroker(
		IUiSessionProviderResolver resolver,
		IUiTransport transport,
		UiSessionRegistry registry,
		IPluginSessionRegistry pluginSessions,
		IIntegrationRegistry integrations,
		TimeProvider timeProvider,
		ILogger logger)
	{
		_resolver = resolver;
		_transport = transport;
		_registry = registry;
		_pluginSessions = pluginSessions;
		_integrations = integrations;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<UiSessionBroker>();

		_registry.Ended += OnSessionEnded;
		_pluginSessions.SessionEnded += OnPluginSessionEnded;
		_integrations.AvailabilityChanged += OnIntegrationAvailabilityChanged;
	}

	public UiSessionOpenTicket Open(string providerId, UiSurface surface, string ownerPrincipal)
	{
		ArgumentException.ThrowIfNullOrEmpty(providerId);
		ArgumentNullException.ThrowIfNull(surface);

		var provider = _resolver.Resolve(providerId);
		if (provider is null)
		{
			return UiSessionOpenTicket.Rejected(UiSessionErrorCodes.ProviderUnavailable,
				"No provider serves that id.");
		}

		// The context is published before the registry record is, not after: a terminal transition
		// landing between the two would find no context, and the session would then never complete its
		// ticket and never dispose its pumps.
		var sessionId = Guid.CreateVersion7().ToString("N");
		var context = new SessionContext
		{
			SessionId = sessionId,
			Provider = provider,
			PluginSessionId = CurrentPluginSessionId(providerId),
			Outbound = new UiSessionWorkPump(sessionId, _logger),
			ProviderPump = new UiSessionWorkPump(sessionId, _logger)
		};

		_contexts[sessionId] = context;

		var created = _registry.Create(sessionId, providerId, surface, ownerPrincipal ?? string.Empty);
		if (!created.Accepted)
		{
			_contexts.TryRemove(sessionId, out _);
			_ = DisposePumpsAsync(context);

			return UiSessionOpenTicket.Rejected(created.Code!, created.Message!);
		}

		context.ProviderPump.Enqueue(_ => OpenOnProviderAsync(context, sessionId, surface));
		_ = ArmOpenDeadlineAsync(context);

		return new UiSessionOpenTicket { Accepted = true, SessionId = sessionId, Ready = context.Ready.Task };
	}

	public async Task<UiSessionOpenTicket> OpenAsync(string providerId,
		UiSurface surface,
		string ownerPrincipal,
		CancellationToken cancellationToken)
	{
		var ticket = Open(providerId, surface, ownerPrincipal);
		if (!ticket.Accepted)
		{
			return ticket;
		}

		return await ticket.Ready.WaitAsync(cancellationToken).ConfigureAwait(false);
	}

	public Task CloseAsync(string sessionId, string reason, CancellationToken cancellationToken)
	{
		_registry.TryClose(sessionId, reason);
		return Task.CompletedTask;
	}

	public bool CloseOwned(string? sessionId, string principal, string reason)
	{
		var session = _registry.Find(sessionId);
		if (session is null || !string.Equals(session.OwnerPrincipal, principal, StringComparison.Ordinal))
		{
			return false;
		}

		return _registry.TryClose(sessionId, reason);
	}

	public UiAttachSessionResponse Attach(string? sessionId, string connectionId, string principal)
	{
		var outcome = _registry.Attach(sessionId, connectionId, principal ?? string.Empty);

		if (!outcome.Accepted)
		{
			return new UiAttachSessionResponse
			{
				Accepted = false,
				SessionId = sessionId ?? string.Empty,
				Code = outcome.Code,
				Message = outcome.Message
			};
		}

		if (_contexts.TryGetValue(sessionId!, out var context))
		{
			lock (context.Gate)
			{
				context.PendingAttachments.Add(connectionId);
			}

			// The connection joins the pending set first, so a snapshot already on its way carries it -
			// which is what makes asking again unnecessary rather than merely wasteful.
			RequestSnapshotUnlessOneIsComing(context);
		}

		return new UiAttachSessionResponse
		{
			Accepted = true,
			SessionId = sessionId!,
			SurfaceKind = outcome.SurfaceKind,
			SessionMode = outcome.SessionMode,
			Revision = outcome.Revision
		};
	}

	public void Detach(string? sessionId, string connectionId)
	{
		var outcome = _registry.Detach(sessionId, connectionId);
		if (!outcome.Detached || !_contexts.TryGetValue(sessionId!, out var context))
		{
			return;
		}

		lock (context.Gate)
		{
			context.PendingAttachments.Remove(connectionId);
			context.EstablishedAttachments.Remove(connectionId);
		}

		var group = UiSessionGroups.For(sessionId!);
		context.Outbound.Enqueue(ct => _transport.RemoveFromGroup(connectionId, group, ct));
	}

	public void DetachConnection(string connectionId)
	{
		foreach (var sessionId in _registry.SessionIdsFor(connectionId))
		{
			Detach(sessionId, connectionId);
		}
	}

	public UiSendEventResponse SendEvent(UiSendEventRequest request, string connectionId, string? actingClientId = null)
	{
		ArgumentNullException.ThrowIfNull(request);

		var session = _registry.Find(request.SessionId);
		if (session is null)
		{
			return _registry.WasEnded(request.SessionId)
				? Refused(UiSessionErrorCodes.SessionClosed, "This session has ended.")
				: Refused(UiSessionErrorCodes.SessionNotFound, "There is no session with that id.");
		}

		if (!_registry.IsAttached(request.SessionId, connectionId))
		{
			return Refused(UiSessionErrorCodes.SessionForbidden, "This connection is not attached to that session.");
		}

		if (!_contexts.TryGetValue(request.SessionId, out var context))
		{
			return Refused(UiSessionErrorCodes.SessionNotFound, "There is no session with that id.");
		}

		var command = new UiSessionEventCommand
		{
			NodeId = request.NodeId,
			Name = request.Name,
			Data = request.Data,
			// Forwarded unchanged even when it names an older tree: the model says the revision exists so
			// the host can discard, not that it must, and the provider is authoritative over its own tree.
			Revision = request.Revision,
			ClientId = actingClientId
		};

		context.ProviderPump.Enqueue(ct => DispatchEventAsync(context, command, ct));

		return new UiSendEventResponse { Accepted = true };
	}

	public UiSessionIngestResult PublishSnapshot(string providerId, string sessionId, UiRawJson tree)
		=> Ingest(providerId, sessionId, tree, UiPayloadShape.Tree);

	public UiSessionIngestResult PublishPatch(string providerId, string sessionId, UiRawJson patch)
		=> Ingest(providerId, sessionId, patch, UiPayloadShape.Patch);

	public void PublishFault(string providerId, string sessionId, string code, string? message)
	{
		if (!Owns(providerId, sessionId))
		{
			return;
		}

		// The provider's own text never reaches the client - it may carry an exception type, a stack
		// trace or a host path - but it is the only description of what went wrong, so it is logged.
		UiSessionLog.ProviderReportedFault(_logger, sessionId, providerId, code, message);

		_registry.TryInvalidate(sessionId, UiSessionErrorCodes.ProviderFaulted, ProviderFaultMessage, retryable: true);
	}

	public void SweepDraining() => _registry.SweepDraining();

	public void Dispose()
	{
		_registry.Ended -= OnSessionEnded;
		_pluginSessions.SessionEnded -= OnPluginSessionEnded;
		_integrations.AvailabilityChanged -= OnIntegrationAvailabilityChanged;
	}

	private static UiSendEventResponse Refused(string code, string message)
		=> new() { Accepted = false, Code = code, Message = message };

	private bool Owns(string providerId, string sessionId)
	{
		var session = _registry.Find(sessionId);
		return session is not null &&
			string.Equals(session.ProviderId, providerId, StringComparison.Ordinal);
	}

	private UiSessionIngestResult Ingest(string providerId, string sessionId, UiRawJson payload, UiPayloadShape shape)
	{
		if (!Owns(providerId, sessionId))
		{
			return UiSessionIngestResult.Reject(UiSessionErrorCodes.SessionNotFound,
				"That session is not open for this provider.");
		}

		var scan = UiPayloadValidator.Scan(payload.Utf8.Span, shape);
		var decision = shape == UiPayloadShape.Tree
			? _registry.EvaluateSnapshot(sessionId, scan)
			: _registry.EvaluatePatch(sessionId, scan);

		if (!_contexts.TryGetValue(sessionId, out var context))
		{
			return UiSessionIngestResult.Reject(UiSessionErrorCodes.SessionNotFound,
				"That session is not open for this provider.");
		}

		switch (decision.Action)
		{
			case UiRelayAction.Deliver when shape == UiPayloadShape.Tree:
				Interlocked.Exchange(ref context.SnapshotsDelivered,
					Interlocked.Read(ref context.SnapshotsRequested));
				DeliverSnapshot(context, payload, decision.ToRevision);
				context.Ready.TrySetResult(UiSessionOpenTicket.Opened(sessionId));
				return UiSessionIngestResult.Accept();

			case UiRelayAction.Deliver:
				DeliverPatch(context, payload, decision.FromRevision, decision.ToRevision);
				return UiSessionIngestResult.Accept();

			case UiRelayAction.Resync:
				RequestResync(context);
				return UiSessionIngestResult.Reject(decision.Code!, "The host asked for a fresh tree instead.");

			case UiRelayAction.Terminate:
				_registry.TryInvalidate(sessionId,
					decision.Code!,
					decision.Code == UiSessionErrorCodes.RateLimited
						? "This view produced updates faster than the host can relay them."
						: "This view is larger than the host can relay.",
					decision.Retryable);
				return UiSessionIngestResult.Reject(decision.Code!, "The session was ended.");

			case UiRelayAction.Reject:
				return UiSessionIngestResult.Reject(decision.Code!, "The host refused this payload.");

			default:
				return UiSessionIngestResult.Accept();
		}
	}

	private void DeliverSnapshot(SessionContext context, UiRawJson tree, int revision)
	{
		string[] pending;
		bool toGroup;

		lock (context.Gate)
		{
			pending = [.. context.PendingAttachments];
			context.PendingAttachments.Clear();
			toGroup = context.EstablishedAttachments.Count > 0 && (pending.Length == 0 || context.ResyncRequested);
			context.ResyncRequested = false;

			foreach (var connectionId in pending)
			{
				context.EstablishedAttachments.Add(connectionId);
			}
		}

		var group = UiSessionGroups.For(context.SessionId);
		var message = new UiSessionTreeUpdatedEvent
		{
			SessionId = context.SessionId, Revision = revision, Tree = tree
		};

		context.Outbound.Enqueue(async ct =>
		{
			if (toGroup)
			{
				await _transport.SendToGroup(group, message, ct).ConfigureAwait(false);
			}

			// The tree reaches a newly attached client before that client can receive a group patch: the
			// unicast and the group join are one ordered step, never two racing ones.
			foreach (var connectionId in pending)
			{
				await _transport.SendToConnection(connectionId, message, ct).ConfigureAwait(false);
				await _transport.AddToGroup(connectionId, group, ct).ConfigureAwait(false);
			}
		});
	}

	private void DeliverPatch(SessionContext context, UiRawJson patch, int fromRevision, int toRevision)
	{
		var group = UiSessionGroups.For(context.SessionId);
		var message = new UiSessionPatchedEvent
		{
			SessionId = context.SessionId,
			FromRevision = fromRevision,
			ToRevision = toRevision,
			Patch = patch
		};

		context.Outbound.Enqueue(ct => _transport.SendToGroup(group, message, ct));
	}

	private void RequestResync(SessionContext context)
	{
		lock (context.Gate)
		{
			context.ResyncRequested = true;
		}

		RequestSnapshot(context);
	}

	/// <summary>
	/// Asks for a tree only when none is already on its way. <see cref="DeliverSnapshot" /> reads the
	/// pending attachments as it delivers rather than as it was asked, so an outstanding request already
	/// serves whoever has attached since - and a second one only puts the same tree in front of everyone
	/// attached twice. Opening and attaching race (opening runs on the provider pump, attaching arrives
	/// as its own client message), so either can be the one that finds a request already outstanding.
	/// A resync deliberately does not come through here: that one is asked for precisely because the
	/// tree in flight is the one being replaced.
	/// </summary>
	private void RequestSnapshotUnlessOneIsComing(SessionContext context)
	{
		if (Interlocked.Read(ref context.SnapshotsRequested) !=
			Interlocked.Read(ref context.SnapshotsDelivered))
		{
			return;
		}

		RequestSnapshot(context);
	}

	// Armed on every outstanding request, not only the first: a provider that answers the opening
	// snapshot and then stops answering resync requests would otherwise leave the client permanently
	// stale with no invalidation at all, which is the frozen UI in its quietest form.
	private void RequestSnapshot(SessionContext context)
	{
		var ticket = Interlocked.Increment(ref context.SnapshotsRequested);
		context.ProviderPump.Enqueue(ct => RequestSnapshotAsync(context, ct));
		_ = ArmSnapshotDeadlineAsync(context, ticket);
	}

	private async Task RequestSnapshotAsync(SessionContext context, CancellationToken cancellationToken)
	{
		try
		{
			await context.Provider.RequestSnapshotAsync(context.SessionId, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			UiSessionLog.ProviderCallFailed(_logger, context.SessionId, "snapshot", exception);
			FaultSession(context.SessionId, exception);
		}
	}

	private async Task DispatchEventAsync(SessionContext context,
		UiSessionEventCommand command,
		CancellationToken cancellationToken)
	{
		try
		{
			await context.Provider.DispatchEventAsync(context.SessionId, command, cancellationToken)
				.ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			UiSessionLog.ProviderCallFailed(_logger, context.SessionId, "event", exception);
			FaultSession(context.SessionId, exception);
		}
	}

	private async Task OpenOnProviderAsync(SessionContext context, string sessionId, UiSurface surface)
	{
		try
		{
			var outcome = await context.Provider.OpenAsync(new UiSessionOpenCommand
					{
						SessionId = sessionId, Surface = surface, UiModelVersion = UiModelVersions.Current
					},
					CancellationToken.None)
				.ConfigureAwait(false);

			if (!outcome.Accepted)
			{
				context.Ready.TrySetResult(UiSessionOpenTicket.Rejected(
					outcome.RejectionCode ?? UiSessionErrorCodes.ProviderRejected,
					"The provider declined to serve that surface."));

				_registry.TryInvalidate(sessionId,
					UiSessionErrorCodes.ProviderRejected,
					"The provider declined to serve that surface.",
					retryable: false);
				return;
			}

			_registry.MarkOpen(sessionId);
			RequestSnapshotUnlessOneIsComing(context);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			UiSessionLog.ProviderCallFailed(_logger, sessionId, "open", exception);
			FaultSession(sessionId, exception);
		}
	}

	private static async Task DisposePumpsAsync(SessionContext context)
	{
		await context.ProviderPump.DisposeAsync().ConfigureAwait(false);
		await context.Outbound.DisposeAsync().ConfigureAwait(false);
	}

	private async Task ArmSnapshotDeadlineAsync(SessionContext context, long ticket)
	{
		try
		{
			await Task.Delay(ProtocolTimeouts.CapabilityInvoke, _timeProvider, CancellationToken.None)
				.ConfigureAwait(false);

			if (Interlocked.Read(ref context.SnapshotsDelivered) >= ticket)
			{
				return;
			}

			_registry.TryInvalidate(context.SessionId,
				UiSessionErrorCodes.ProviderTimeout,
				"The provider of this view did not answer in time.",
				retryable: true);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			UiSessionLog.ProviderCallFailed(_logger, context.SessionId, "deadline", exception);
		}
	}

	private async Task ArmOpenDeadlineAsync(SessionContext context)
	{
		try
		{
			var timeout = Task.Delay(ProtocolTimeouts.CapabilityInvoke, _timeProvider, CancellationToken.None);
			var winner = await Task.WhenAny(context.Ready.Task, timeout).ConfigureAwait(false);

			if (winner == timeout)
			{
				// Only bites while the session is still waiting for its provider. A session already ended
				// for another reason is gone from the registry, so a dead provider is never reported twice
				// under two different codes.
				_registry.TryInvalidate(context.SessionId,
					UiSessionErrorCodes.ProviderTimeout,
					"The provider of this view did not answer in time.",
					retryable: true);
			}
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			UiSessionLog.ProviderCallFailed(_logger, context.SessionId, "deadline", exception);
		}
	}

	// A call that failed because the link is already gone is that disconnection, not a second, separate
	// fault: reporting it as one would race the plugin session registry's own verdict and let the client
	// see either code for one death.
	private void FaultSession(string sessionId, Exception exception)
		=> _registry.TryInvalidate(sessionId,
			exception is UiProviderDisconnectedException
				? UiSessionErrorCodes.ProviderDisconnected
				: UiSessionErrorCodes.ProviderFaulted,
			exception is UiProviderDisconnectedException ? ProviderDisconnectedMessage : ProviderFaultMessage,
			retryable: true);

	// Matched on the plugin session id rather than the plugin id, the way PluginWebSocketEndpoint guards
	// the asset path: Detached fires for every socket drop and Pruned fires for the session a reconnect
	// replaced, so a plugin id alone would let one connection's teardown kill the UI sessions its own
	// replacement had already opened.
	private void OnPluginSessionEnded(object? sender, PluginSessionEndedEventArgs e)
	{
		foreach (var context in _contexts.Values)
		{
			if (string.Equals(context.PluginSessionId, e.SessionId, StringComparison.Ordinal))
			{
				_registry.TryInvalidate(context.SessionId,
					UiSessionErrorCodes.ProviderDisconnected,
					ProviderDisconnectedMessage,
					retryable: true);
			}
		}
	}

	private string? CurrentPluginSessionId(string providerId)
		=> _pluginSessions.Snapshot()
			.FirstOrDefault(session => string.Equals(session.PluginId, providerId, StringComparison.Ordinal))
			?.SessionId;

	private void OnIntegrationAvailabilityChanged(object? sender, IntegrationAvailabilityChangedEventArgs e)
	{
		if (e.IsAvailable)
		{
			return;
		}

		InvalidateProvider(e.IntegrationId,
			UiSessionErrorCodes.ProviderUnavailable,
			"The integration serving this view is no longer available.");
	}

	public void InvalidateWidgetSessions(Guid widgetId)
	{
		foreach (var session in _registry.SessionsForWidget(widgetId.ToString()))
		{
			_registry.TryInvalidate(session.SessionId,
				UiSessionErrorCodes.WidgetReconfigured,
				"The widget was reconfigured and its view has to be built again.",
				retryable: true);
		}
	}

	public void CloseWidgetSessions(Guid widgetId, string reason)
	{
		foreach (var session in _registry.SessionsForWidget(widgetId.ToString(), includeConfiguration: true))
		{
			_registry.TryClose(session.SessionId, reason);
		}
	}

	private void InvalidateProvider(string providerId, string code, string message)
	{
		foreach (var session in _registry.SessionsForProvider(providerId))
		{
			_registry.TryInvalidate(session.SessionId, code, message, retryable: true);
		}
	}

	private void OnSessionEnded(object? sender, UiSessionEndedEventArgs e)
	{
		if (!_contexts.TryRemove(e.Session.SessionId, out var context))
		{
			return;
		}

		var invalidated = e.Reason == UiSessionEndReason.Invalidated
			? new UiSessionInvalidatedEvent
			{
				SessionId = e.Session.SessionId,
				Code = e.Code ?? UiSessionErrorCodes.ProviderFaulted,
				Message = e.Message ?? ProviderFaultMessage,
				Retryable = e.Retryable
			}
			: null;

		var closed = invalidated is null
			? new UiSessionClosedEvent { SessionId = e.Session.SessionId, Reason = e.Message }
			: null;

		// Every terminal transition puts exactly one message in front of every attached client before the
		// session disappears. A session that vanishes silently leaves a client rendering a tree nothing
		// will ever update again - the frozen UI this whole path exists to prevent. Addressed per
		// connection rather than through the group, because a client that attached but has not been added
		// to the group yet must still hear about it, and must hear about it only once.
		var recipients = e.Session.Attachments;
		context.Outbound.Enqueue(async ct =>
		{
			foreach (var connectionId in recipients)
			{
				if (invalidated is not null)
				{
					await _transport.SendToConnection(connectionId, invalidated, ct).ConfigureAwait(false);
				}
				else
				{
					await _transport.SendToConnection(connectionId, closed!, ct).ConfigureAwait(false);
				}
			}
		});

		context.Ready.TrySetResult(UiSessionOpenTicket.Rejected(e.Code ?? UiSessionErrorCodes.SessionClosed,
			e.Message ?? "The session ended."));

		_ = TearDownAsync(context, e.Reason, e.Message ?? string.Empty);
	}

	private async Task TearDownAsync(SessionContext context, UiSessionEndReason reason, string message)
	{
		try
		{
			// Closed on every terminal transition, invalidation included. The provider's session object
			// owns a pump and its event subscriptions, and IUiProvider promises it is disposed when the
			// session ends "including when the host tears it down after a fault"; skipping the close for
			// an invalidation left both alive for the rest of the process.
			var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			context.ProviderPump.Enqueue(async ct =>
			{
				try
				{
					await context.Provider.CloseAsync(context.SessionId, message, ct).ConfigureAwait(false);
				}
				finally
				{
					completion.TrySetResult();
				}
			});

			await completion.Task.ConfigureAwait(false);
			await DisposePumpsAsync(context).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			UiSessionLog.ProviderCallFailed(_logger, context.SessionId, "close", exception);
		}
	}

	private sealed class SessionContext
	{
		// Counters rather than properties so Interlocked can address them.
		public long SnapshotsRequested;

		public long SnapshotsDelivered;

		public required string SessionId { get; init; }

		public required IUiSessionProvider Provider { get; init; }

		// The plugin session this UI session was opened under, or null for an in-process provider.
		public required string? PluginSessionId { get; init; }

		public required UiSessionWorkPump Outbound { get; init; }

		// Provider-bound calls run here rather than on the caller's thread, so no realtime operation and
		// no plugin inbound pump ever waits on a provider.
		public required UiSessionWorkPump ProviderPump { get; init; }

		public object Gate { get; } = new();

		public List<string> PendingAttachments { get; } = [];

		public HashSet<string> EstablishedAttachments { get; } = new(StringComparer.Ordinal);

		public bool ResyncRequested { get; set; }

		public TaskCompletionSource<UiSessionOpenTicket> Ready { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
	}
}
