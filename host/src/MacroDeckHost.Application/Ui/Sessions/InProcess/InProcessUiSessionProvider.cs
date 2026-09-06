using System.Collections.Concurrent;
using System.Threading.Channels;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Serialization;
using Serilog;

namespace MacroDeckHost.Application.Ui.Sessions.InProcess;

// Serves an in-process IUiProvider through the same session API a plugin is served
// through. Everything the provider produces is canonically serialized here, once, so from the sink
// onward the in-process and the remote path carry identical bytes.
public sealed class InProcessUiSessionProvider : IUiSessionProvider, IAsyncDisposable
{
	private readonly IUiProvider _provider;
	private readonly IUiSessionSink _sink;
	private readonly ILogger _logger;

	private readonly ConcurrentDictionary<string, LiveSession> _sessions = new(StringComparer.Ordinal);

	public InProcessUiSessionProvider(string providerId, IUiProvider provider, IUiSessionSink sink, ILogger logger)
	{
		ProviderId = providerId;
		_provider = provider;
		_sink = sink;
		_logger = logger.ForContext<InProcessUiSessionProvider>();
	}

	public string ProviderId { get; }

	public async Task<UiSessionOpenOutcome> OpenAsync(UiSessionOpenCommand command,
		CancellationToken cancellationToken)
	{
		var session = await _provider.CreateSessionAsync(
				new UiSessionRequest { Surface = command.Surface, UiModelVersion = command.UiModelVersion },
				cancellationToken)
			.ConfigureAwait(false);

		if (session is null)
		{
			return UiSessionOpenOutcome.Reject(UiSessionErrorCodes.ProviderRejected);
		}

		var live = new LiveSession(this, command.SessionId, session);
		_sessions[command.SessionId] = live;
		live.Start();

		return UiSessionOpenOutcome.Accept(command.UiModelVersion);
	}

	public async Task CloseAsync(string sessionId, string reason, CancellationToken cancellationToken)
	{
		if (_sessions.TryRemove(sessionId, out var live))
		{
			await live.DisposeAsync().ConfigureAwait(false);
		}
	}

	public Task RequestSnapshotAsync(string sessionId, CancellationToken cancellationToken)
	{
		if (_sessions.TryGetValue(sessionId, out var live))
		{
			live.RequestSnapshot();
		}

		return Task.CompletedTask;
	}

	public Task DispatchEventAsync(string sessionId,
		UiSessionEventCommand command,
		CancellationToken cancellationToken)
	{
		if (_sessions.TryGetValue(sessionId, out var live))
		{
			live.Dispatch(new UiEvent
				{
					NodeId = command.NodeId,
					Name = command.Name,
					Data = command.Data.IsEmpty ? null : command.Data.ToElement(),
					Revision = command.Revision
				},
				command.ClientId);
		}

		return Task.CompletedTask;
	}

	public async ValueTask DisposeAsync()
	{
		foreach (var sessionId in _sessions.Keys)
		{
			if (_sessions.TryRemove(sessionId, out var live))
			{
				await live.DisposeAsync().ConfigureAwait(false);
			}
		}
	}

	private enum WorkKind
	{
		Drain,

		Snapshot,

		Event
	}

	private sealed class LiveSession : IAsyncDisposable
	{
		private readonly InProcessUiSessionProvider _owner;
		private readonly string _sessionId;
		private readonly IUiSession _session;
		private readonly CancellationTokenSource _stopping = new();

		private readonly Channel<(WorkKind Kind, UiEvent? Event, string? OriginClientId)> _work =
			Channel.CreateUnbounded<(WorkKind, UiEvent?, string?)>(new UnboundedChannelOptions
			{
				SingleReader = true, AllowSynchronousContinuations = false
			});

		private Task? _pump;
		private int _drainQueued;

		public LiveSession(InProcessUiSessionProvider owner, string sessionId, IUiSession session)
		{
			_owner = owner;
			_sessionId = sessionId;
			_session = session;
		}

		public void Start()
		{
			_session.Changed += OnChanged;
			_session.Faulted += OnFaulted;
			_pump = Task.Run(RunAsync);
		}

		public void RequestSnapshot() => _work.Writer.TryWrite((WorkKind.Snapshot, null, null));

		public void Dispatch(UiEvent uiEvent, string? originClientId)
			=> _work.Writer.TryWrite((WorkKind.Event, uiEvent, originClientId));

		public async ValueTask DisposeAsync()
		{
			_session.Changed -= OnChanged;
			_session.Faulted -= OnFaulted;
			_work.Writer.TryComplete();
			await _stopping.CancelAsync().ConfigureAwait(false);

			if (_pump is { } pump)
			{
				await pump.ConfigureAwait(false);
			}

			_stopping.Dispose();

			try
			{
				await _session.DisposeAsync().ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				UiSessionLog.ProviderCallFailed(_owner._logger, _sessionId, "dispose", exception);
			}
		}

		private void OnChanged(object? sender, EventArgs e)
		{
			// Changed may be raised from any thread and several times for one batch of work. Only one
			// drain is ever queued at a time; the pump then takes whatever has accumulated, so the host
			// never depends on the number of raises matching the number of patches.
			if (Interlocked.Exchange(ref _drainQueued, 1) == 0)
			{
				_work.Writer.TryWrite((WorkKind.Drain, null, null));
			}
		}

		private void OnFaulted(object? sender, UiSessionFaultedEventArgs e)
		{
			// The provider's own reason and exception never reach a client, but losing them entirely
			// would make a faulted integration undiagnosable.
			if (e.Exception is { } exception)
			{
				UiSessionLog.ProviderCallFailed(_owner._logger, _sessionId, "faulted", exception);
			}

			_owner._sink.PublishFault(_owner.ProviderId, _sessionId, UiSessionErrorCodes.ProviderFaulted, e.Reason);
		}

		private async Task RunAsync()
		{
			try
			{
				await foreach (var (kind, uiEvent, originClientId) in _work.Reader.ReadAllAsync().ConfigureAwait(false))
				{
					Handle(kind, uiEvent, originClientId);
				}
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				Fault(exception, "pump");
			}
		}

		private void Handle(WorkKind kind, UiEvent? uiEvent, string? originClientId)
		{
			switch (kind)
			{
				case WorkKind.Drain:
					Interlocked.Exchange(ref _drainQueued, 0);
					DrainPatches();
					break;

				case WorkKind.Snapshot:
					PublishSnapshot();
					break;

				case WorkKind.Event when uiEvent is not null:
					DispatchEvent(uiEvent, originClientId);
					break;
			}
		}

		private void DrainPatches()
		{
			try
			{
				foreach (var patch in _session.DrainPatches())
				{
					_owner._sink.PublishPatch(_owner.ProviderId,
						_sessionId,
						new UiRawJson(UiCanonicalJson.SerializeToUtf8Bytes(patch)));
				}
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				Fault(exception, "drain");
			}
		}

		private void PublishSnapshot()
		{
			try
			{
				var tree = _session.BuildTree();
				_owner._sink.PublishSnapshot(_owner.ProviderId,
					_sessionId,
					new UiRawJson(UiCanonicalJson.SerializeToUtf8Bytes(tree)));
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				Fault(exception, "snapshot");
			}
		}

		private void DispatchEvent(UiEvent uiEvent, string? originClientId)
		{
			try
			{
				if (_session is IOriginAwareUiSession originAware)
				{
					originAware.Dispatch(uiEvent, originClientId);
				}
				else
				{
					_session.Dispatch(uiEvent);
				}
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				Fault(exception, "event");
			}
		}

		private void Fault(Exception exception, string operation)
		{
			// A provider's throw becomes a terminal session error and never propagates: the broker's
			// callers are a realtime operation and a plugin's inbound pump, and neither may be torn down by
			// integration code.
			UiSessionLog.ProviderCallFailed(_owner._logger, _sessionId, operation, exception);
			_owner._sink.PublishFault(_owner.ProviderId,
				_sessionId,
				UiSessionErrorCodes.ProviderFaulted,
				exception.Message);
		}
	}
}
