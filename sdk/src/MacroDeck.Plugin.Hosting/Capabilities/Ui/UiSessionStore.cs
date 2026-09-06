using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using MacroDeck.Plugin.Hosting.Logging;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Callbacks.Ui;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Serialization;
using Serilog;

namespace MacroDeck.Plugin.Hosting.Capabilities.Ui;

/// <summary>
/// The plugin's live UI sessions: which <see cref="IUiSession" /> serves each host-issued session id,
/// and the pump that turns what it produces into <c>host.invoke ui/snapshot</c>, <c>ui/patch</c> and
/// <c>ui/fault</c>.
/// </summary>
internal sealed class UiSessionStore : IAsyncDisposable
{
	private readonly IHostInvoker _hostInvoker;
	private readonly ILogger _logger;

	private readonly ConcurrentDictionary<string, LiveSession> _sessions = new(StringComparer.Ordinal);

	public UiSessionStore(IHostInvoker hostInvoker, ILogger logger)
	{
		_hostInvoker = hostInvoker;
		_logger = logger.ForContext<UiSessionStore>();
	}

	public bool TryAdd(string sessionId, IUiSession session)
	{
		var live = new LiveSession(this, sessionId, session);

		if (!_sessions.TryAdd(sessionId, live))
		{
			return false;
		}

		live.Start();
		return true;
	}

	public async Task<bool> RemoveAsync(string sessionId)
	{
		if (!_sessions.TryRemove(sessionId, out var live))
		{
			return false;
		}

		await live.DisposeAsync().ConfigureAwait(false);
		return true;
	}

	public bool RequestSnapshot(string sessionId)
	{
		if (!_sessions.TryGetValue(sessionId, out var live))
		{
			return false;
		}

		live.RequestSnapshot();
		return true;
	}

	public bool Dispatch(string sessionId, UiEvent uiEvent)
	{
		if (!_sessions.TryGetValue(sessionId, out var live))
		{
			return false;
		}

		live.Dispatch(uiEvent);
		return true;
	}

	public async ValueTask DisposeAsync()
	{
		foreach (var sessionId in _sessions.Keys)
		{
			await RemoveAsync(sessionId).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Hands the host an already-serialized payload without re-encoding it.
	/// </summary>
	/// <remarks>
	/// Parsing pins the bytes rather than rewriting them, and the protocol's own serializer writes a
	/// <see cref="JsonElement" /> back out verbatim, so the client applies exactly the bytes
	/// <c>UiCanonicalJson</c> produced here. Building the element any other way - from a model object,
	/// or through a writer - would renormalise numbers and member order the model deliberately fixed.
	/// </remarks>
	private static JsonElement Opaque(byte[] utf8)
	{
		using var document = JsonDocument.Parse(utf8);
		return document.RootElement.Clone();
	}

	private enum WorkKind
	{
		Drain,

		Snapshot,

		Event
	}

	private sealed class LiveSession : IAsyncDisposable
	{
		private readonly UiSessionStore _owner;
		private readonly string _sessionId;
		private readonly IUiSession _session;

		private readonly Channel<(WorkKind Kind, UiEvent? Event)> _work =
			Channel.CreateUnbounded<(WorkKind, UiEvent?)>(new UnboundedChannelOptions
			{
				SingleReader = true, AllowSynchronousContinuations = false
			});

		private Task? _pump;
		private int _drainQueued;

		public LiveSession(UiSessionStore owner, string sessionId, IUiSession session)
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

		public void RequestSnapshot() => _work.Writer.TryWrite((WorkKind.Snapshot, null));

		public void Dispatch(UiEvent uiEvent) => _work.Writer.TryWrite((WorkKind.Event, uiEvent));

		public async ValueTask DisposeAsync()
		{
			_session.Changed -= OnChanged;
			_session.Faulted -= OnFaulted;
			_work.Writer.TryComplete();

			if (_pump is { } pump)
			{
				await pump.ConfigureAwait(false);
			}

			try
			{
				await _session.DisposeAsync().ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				_owner._logger.UiSessionCallFailed(_sessionId, "dispose", exception);
			}
		}

		private void OnChanged(object? sender, EventArgs e)
		{
			// Changed may be raised from any thread and several times for one batch of work, so only one
			// drain is ever queued at a time and the pump takes whatever has accumulated. Counting raises
			// would either lose patches or drain an empty session.
			if (Interlocked.Exchange(ref _drainQueued, 1) == 0)
			{
				_work.Writer.TryWrite((WorkKind.Drain, null));
			}
		}

		private void OnFaulted(object? sender, UiSessionFaultedEventArgs e)
			=> _ = FaultAsync(ProtocolErrorCodes.InternalError);

		private async Task RunAsync()
		{
			try
			{
				await foreach (var (kind, uiEvent) in _work.Reader.ReadAllAsync().ConfigureAwait(false))
				{
					await HandleAsync(kind, uiEvent).ConfigureAwait(false);
				}
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				_owner._logger.UiSessionCallFailed(_sessionId, "pump", exception);
				await FaultAsync(ProtocolErrorCodes.InternalError).ConfigureAwait(false);
			}
		}

		private Task HandleAsync(WorkKind kind, UiEvent? uiEvent) => kind switch
		{
			WorkKind.Drain => DrainAsync(),
			WorkKind.Snapshot => PublishSnapshotAsync(),
			WorkKind.Event when uiEvent is not null => DispatchAsync(uiEvent),
			_ => Task.CompletedTask
		};

		private async Task DrainAsync()
		{
			Interlocked.Exchange(ref _drainQueued, 0);

			IReadOnlyList<UiPatch> patches;

			try
			{
				patches = _session.DrainPatches();
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				await FaultOnThrowAsync(exception, "drain").ConfigureAwait(false);
				return;
			}

			foreach (var patch in patches)
			{
				await SendAsync(HostOperations.Ui.Patch,
						new UiPatchArguments
						{
							SessionId = _sessionId, Patch = Opaque(UiCanonicalJson.SerializeToUtf8Bytes(patch))
						})
					.ConfigureAwait(false);
			}
		}

		private async Task PublishSnapshotAsync()
		{
			byte[] tree;

			try
			{
				tree = UiCanonicalJson.SerializeToUtf8Bytes(_session.BuildTree());
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				await FaultOnThrowAsync(exception, "snapshot").ConfigureAwait(false);
				return;
			}

			await SendAsync(HostOperations.Ui.Snapshot,
					new UiSnapshotArguments { SessionId = _sessionId, Tree = Opaque(tree) })
				.ConfigureAwait(false);
		}

		private async Task DispatchAsync(UiEvent uiEvent)
		{
			try
			{
				_session.Dispatch(uiEvent);
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				await FaultOnThrowAsync(exception, "event").ConfigureAwait(false);
			}
		}

		private Task FaultOnThrowAsync(Exception exception, string operation)
		{
			_owner._logger.UiSessionCallFailed(_sessionId, operation, exception);
			return FaultAsync(ProtocolErrorCodes.InternalError);
		}

		private async Task FaultAsync(string code)
		{
			try
			{
				await _owner._hostInvoker.InvokeAsync(HostApis.Ui,
						HostOperations.Ui.Fault,
						new UiFaultArguments { SessionId = _sessionId, Code = code },
						CancellationToken.None)
					.ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				_owner._logger.HostCallbackFailed(HostApis.Ui, HostOperations.Ui.Fault, exception);
			}
		}

		private async Task SendAsync(string operation, object arguments)
		{
			try
			{
				await _owner._hostInvoker.InvokeAsync(HostApis.Ui, operation, arguments, CancellationToken.None)
					.ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				// A refusal - a limit trip, a stale revision, a session the host has already ended -
				// comes back as the host.result error. The host resynchronises or ends the session
				// itself, so there is nothing for the provider to retry and the pump must keep running.
				_owner._logger.HostCallbackFailed(HostApis.Ui, operation, exception);
			}
		}
	}
}
