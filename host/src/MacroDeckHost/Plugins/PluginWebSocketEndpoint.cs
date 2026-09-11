using System.Globalization;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using MacroDeck.Plugin.Protocol;
using MacroDeck.Plugin.Protocol.Auth;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Correlation;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Logging;
using MacroDeck.Plugin.Protocol.Reconnection;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Events;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Assets;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Plugins.Logging;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Auth;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.WebSockets;
using Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.IdentityModel.JsonWebTokens;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Plugins;

public static class PluginWebSocketEndpointExtensions
{
	public static IEndpointConventionBuilder MapPluginWebSocket(this IEndpointRouteBuilder endpoints)
		=> endpoints.Map(ProtocolConstants.WebSocketPath,
				async context =>
				{
					var handler = context.RequestServices.GetRequiredService<PluginWebSocketEndpoint>();
					await handler.HandleAsync(context);
				})
			.AllowAnonymous();
}

public sealed class PluginWebSocketEndpoint
{
	private readonly IPluginSessionRegistry _sessionRegistry;
	private readonly IPluginCapabilityInvoker _invoker;
	private readonly IRemotePluginIntegrationRegistrar _registrar;
	private readonly RemotePluginSnapshotRefresher _snapshotRefresher;
	private readonly IPluginCallbackRouter _callbackRouter;
	private readonly IPluginAssetReceiver _assetReceiver;
	private readonly IPluginHostAssetSender? _hostAssetSender;
	private readonly HostStatePusher _statePusher;
	private readonly IEventBus _eventBus;
	private readonly LoginThrottle _throttle;
	private readonly TimeProvider _timeProvider;
	private readonly IMediator _mediator;
	private readonly IHostApplicationLifetime _lifetime;
	private readonly IPluginLogIngestor _logIngestor;
	private readonly ILogger _logger;

	private readonly StateUpdateCoalescer _coalescer = new();

	public PluginWebSocketEndpoint(
		IPluginSessionRegistry sessionRegistry,
		IPluginCapabilityInvoker invoker,
		IRemotePluginIntegrationRegistrar registrar,
		RemotePluginSnapshotRefresher snapshotRefresher,
		IPluginCallbackRouter callbackRouter,
		IPluginAssetReceiver assetReceiver,
		HostStatePusher statePusher,
		IEventBus eventBus,
		[FromKeyedServices("plugin")] LoginThrottle throttle,
		TimeProvider timeProvider,
		IMediator mediator,
		IHostApplicationLifetime lifetime,
		IPluginLogIngestor logIngestor,
		ILogger logger,
		IPluginHostAssetSender? hostAssetSender = null)
	{
		_hostAssetSender = hostAssetSender;
		_sessionRegistry = sessionRegistry;
		_invoker = invoker;
		_registrar = registrar;
		_snapshotRefresher = snapshotRefresher;
		_callbackRouter = callbackRouter;
		_assetReceiver = assetReceiver;
		_statePusher = statePusher;
		_eventBus = eventBus;
		_throttle = throttle;
		_timeProvider = timeProvider;
		_mediator = mediator;
		_lifetime = lifetime;
		_logIngestor = logIngestor;
		_logger = logger.ForContext<PluginWebSocketEndpoint>();
	}

	public async Task HandleAsync(HttpContext context)
	{
		if (!LoopbackConnection.IsLocalRequest(context) || PluginBrowserGuard.IsBrowserRequest(context))
		{
			await WriteErrorAsync(context, PluginErrors.Forbidden(), StatusCodes.Status403Forbidden);
			return;
		}

		var throttleKey = ThrottleKey(context);
		if (_throttle.IsThrottled(throttleKey, out var retryAfter))
		{
			context.Response.Headers.RetryAfter
				= ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
			await WriteErrorAsync(context, PluginErrors.RateLimited(retryAfter), StatusCodes.Status429TooManyRequests);
			return;
		}

		if (!context.WebSockets.IsWebSocketRequest)
		{
			await WriteErrorAsync(context, PluginErrors.InvalidPayload(), StatusCodes.Status400BadRequest);
			return;
		}

		if (!context.WebSockets.WebSocketRequestedProtocols.Contains(ProtocolConstants.WebSocketSubProtocol))
		{
			await WriteErrorAsync(context, PluginErrors.InvalidPayload(), StatusCodes.Status400BadRequest);
			return;
		}

		var authResult = await context.AuthenticateAsync(PluginAuthSchemes.PluginSession);
		if (!authResult.Succeeded || authResult.Principal is null)
		{
			_throttle.RegisterFailure(throttleKey);
			await WriteErrorAsync(context, PluginErrors.Unauthenticated(), StatusCodes.Status401Unauthorized);
			return;
		}

		_throttle.RegisterSuccess(throttleKey);

		var pluginId = authResult.Principal.FindFirst(PluginClaimTypes.PluginId)!.Value;
		var sessionId = authResult.Principal.FindFirst(PluginClaimTypes.SessionId)!.Value;

		var socket = await context.WebSockets.AcceptWebSocketAsync(ProtocolConstants.WebSocketSubProtocol);
		var connection = new PluginWebSocketConnection(socket, Guid.NewGuid().ToString("N"), _timeProvider);

		try
		{
			await RunAsync(context, socket, connection, pluginId, sessionId);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			// Whatever went wrong, the socket must not be left dangling - a client blocked on
			// ReceiveAsync forever is a worse failure mode than a closed connection it can retry.
			PluginWebSocketLog.ConnectionFaulted(_logger, exception);
		}
		finally
		{
			connection.Dispose();
			socket.Dispose();
		}
	}

	private async Task RunAsync(
		HttpContext context,
		WebSocket socket,
		PluginWebSocketConnection connection,
		string pluginId,
		string sessionId)
	{
		// Deliberately not context.RequestAborted: for an upgraded connection that token tracks the
		// framework's notion of "the request", which is not the same lifetime as the socket itself on
		// every host (in particular, TestServer's in-memory pipeline completes the request as soon as
		// the 101 response is written, well before the socket closes). The connection instead lives
		// until the process is shutting down or the socket itself ends the loop.
		var hostCancellationToken = _lifetime.ApplicationStopping;

		// Step 6: the handshake. A closed socket, a malformed body or a non-hello message all become
		// the same "nothing usable arrived" outcome - there is deliberately no close code for this case.
		using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(hostCancellationToken);
		handshakeCts.CancelAfter(ProtocolTimeouts.Handshake);

		byte[]? message;
		try
		{
			message = await ReceiveMessageAsync(socket, handshakeCts.Token);
		}
		catch (OperationCanceledException) when (!hostCancellationToken.IsCancellationRequested)
		{
			await SendProtocolErrorAsync(connection, ProtocolErrorCodes.InvalidPayload, null, hostCancellationToken);
			await CloseNormallyAsync(socket, "Handshake timed out.", hostCancellationToken);
			return;
		}
		catch (WebSocketException)
		{
			return;
		}

		if (message is null)
		{
			return;
		}

		var read = ProtocolEnvelopeReader.Read(message);
		if (!read.Succeeded || read.Envelope is not { Type: MessageTypes.SessionHello } envelope)
		{
			await SendProtocolErrorAsync(connection,
				ProtocolErrorCodes.InvalidPayload,
				read.Envelope?.Id,
				hostCancellationToken);
			await CloseNormallyAsync(socket, "Expected session.hello.", hostCancellationToken);
			return;
		}

		SessionHelloPayload? hello;
		try
		{
			hello = envelope.Payload?.Deserialize<SessionHelloPayload>(PluginProtocolJson.Options);
		}
		catch (JsonException)
		{
			hello = null;
		}

		if (hello is null)
		{
			await SendProtocolErrorAsync(connection,
				ProtocolErrorCodes.InvalidPayload,
				envelope.Id,
				hostCancellationToken);
			await CloseNormallyAsync(socket, "Malformed session.hello.", hostCancellationToken);
			return;
		}

		var record = _sessionRegistry.Snapshot()
			.FirstOrDefault(session => string.Equals(session.SessionId, sessionId, StringComparison.Ordinal));

		// Session id mismatch is SESSION_EXPIRED (4002), never PROTOCOL_VERSION_UNSUPPORTED (4001) - the
		// SDK classifies a version error as fatal and would stop the plugin outright for what is really
		// just a stale/pruned session.
		if (record is null || !string.Equals(hello.SessionId, sessionId, StringComparison.Ordinal))
		{
			await SendProtocolErrorAsync(connection,
				ProtocolErrorCodes.SessionExpired,
				envelope.Id,
				hostCancellationToken);
			await CloseAsync(socket, ProtocolCloseCodes.SessionExpired, "Session expired.", hostCancellationToken);
			return;
		}

		if (hello.ProtocolVersion != record.NegotiatedVersion)
		{
			await SendProtocolErrorAsync(connection,
				ProtocolErrorCodes.ProtocolVersionUnsupported,
				envelope.Id,
				hostCancellationToken);
			await CloseAsync(socket,
				ProtocolCloseCodes.ProtocolVersionUnsupported,
				"Unsupported protocol version.",
				hostCancellationToken);
			return;
		}

		// Step 7: resume-versus-replace. A plugin that already has a live connection and is not
		// resuming is handled upstream, in IPluginSessionRegistry.Create - it closes the prior
		// connection with SessionReplaced (4000) at the moment the new session is created, before this
		// handshake ever runs. instanceId never changes this decision, only what gets logged.
		var resumed = false;

		if (SessionResumeRules.IsResumeAttempt(hello.ResumeSessionId))
		{
			if (!string.Equals(hello.ResumeSessionId, sessionId, StringComparison.Ordinal) ||
				!_sessionRegistry.TryResume(pluginId, hello.ResumeSessionId, _timeProvider.GetUtcNow(), out _))
			{
				await SendProtocolErrorAsync(connection,
					ProtocolErrorCodes.SessionNotResumable,
					envelope.Id,
					hostCancellationToken);
				await CloseNormallyAsync(socket, "The session could not be resumed.", hostCancellationToken);
				return;
			}

			resumed = true;
		}

		if (!_sessionRegistry.TryAttach(sessionId, connection, hello.InstanceId))
		{
			await SendProtocolErrorAsync(connection,
				ProtocolErrorCodes.SessionExpired,
				envelope.Id,
				hostCancellationToken);
			await CloseAsync(socket, ProtocolCloseCodes.SessionExpired, "Session expired.", hostCancellationToken);
			return;
		}

		await connection.Send(new ProtocolEnvelope
			{
				Type = MessageTypes.SessionWelcome,
				Id = NewId(),
				CorrelationId = envelope.Id,
				Payload = JsonSerializer.SerializeToElement(
					new SessionWelcomePayload { SessionId = sessionId, Resumed = resumed },
					PluginProtocolJson.Options)
			},
			hostCancellationToken);

		await _mediator.Publish(new PluginSessionsChangedNotification(), CancellationToken.None);

		// Registration runs *alongside* the message loop, never before it: it describes every declared
		// capability and waits for the icon's asset upload, and both of those are replies that only the
		// loop below can read. Awaiting it here deadlocks the handshake against itself until every
		// describe times out. On rejection the registrar has already sent protocol.error and terminated
		// the session, which ends the loop on its own.
		var registration = RegisterAndPushStateAsync(pluginId, hostCancellationToken);

		await MessageLoopAsync(socket, connection, pluginId, sessionId, hostCancellationToken);
		await registration;
	}

	private async Task RegisterAndPushStateAsync(string pluginId, CancellationToken cancellationToken)
	{
		try
		{
			if (await _registrar.RegisterAsync(pluginId, cancellationToken).ConfigureAwait(false))
			{
				await _statePusher.PushAllAsync(pluginId, cancellationToken).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception exception)
		{
			PluginWebSocketLog.PluginRegistrationFailed(_logger, pluginId, exception);
		}
	}

	private async Task MessageLoopAsync(
		WebSocket socket,
		PluginWebSocketConnection connection,
		string pluginId,
		string sessionId,
		CancellationToken hostCancellationToken)
	{
		using var loopCts = CancellationTokenSource.CreateLinkedTokenSource(hostCancellationToken);
		var lastInboundTicks = _timeProvider.GetUtcNow().UtcTicks;
		var saidGoodbye = false;

		var keepAlive = KeepAliveLoopAsync(socket,
			connection,
			() => Interlocked.Read(ref lastInboundTicks),
			loopCts.Token);

		var dispatch = new InboundDispatchState();
		var processing = ProcessQueuedMessagesAsync(connection, pluginId, dispatch, loopCts.Token);

		// A separate lane from the queue above, deliberately: log.publish shares no depth counter, no
		// QUEUE_OVERFLOW and no connection-closing failure mode with HostInvoke/EventPublish/etc. A log
		// burst must never be able to starve or kill the capability lanes those messages need - see
		// InboundDispatchState.LogQueue's remarks.
		var logProcessing = ProcessLogQueueAsync(connection, pluginId, sessionId, dispatch, loopCts.Token);

		try
		{
			while (!loopCts.IsCancellationRequested)
			{
				byte[]? message;
				try
				{
					message = await ReceiveMessageAsync(socket, loopCts.Token);
				}
				catch (OperationCanceledException)
				{
					break;
				}
				catch (WebSocketException)
				{
					break;
				}

				if (message is null)
				{
					break;
				}

				var inboundAt = _timeProvider.GetUtcNow();
				Interlocked.Exchange(ref lastInboundTicks, inboundAt.UtcTicks);
				_sessionRegistry.Touch(sessionId, inboundAt);

				var read = ProtocolEnvelopeReader.Read(message);
				if (!read.Succeeded)
				{
					await SendProtocolErrorAsync(connection, read.Error!.Code, read.Envelope?.Id, loopCts.Token);
					continue;
				}

				var envelope = read.Envelope!;
				switch (envelope.Type)
				{
					case MessageTypes.SessionPing:
						await connection.Send(new ProtocolEnvelope
							{
								Type = MessageTypes.SessionPong, Id = NewId(), CorrelationId = envelope.Id
							},
							loopCts.Token);
						break;

					case MessageTypes.SessionPong:
						break;

					case MessageTypes.SessionGoodbye:
						saidGoodbye = true;
						_sessionRegistry.MakeNonResumable(sessionId);
						await CloseGracefullyAsync(socket, hostCancellationToken);
						await loopCts.CancelAsync();
						break;

					case MessageTypes.CapabilityResult:
						await HandleCapabilityResultAsync(connection, pluginId, envelope, loopCts.Token);
						break;

					// A reply, not a push: it completes the step PluginHostAssetSender is waiting on, and
					// gets the same inline treatment capability.result does rather than queueing behind
					// work the transfer may itself be feeding.
					case MessageTypes.HostAssetAck:
						_hostAssetSender?.TryComplete(pluginId, envelope);
						break;

					case MessageTypes.FlowPause:
						_sessionRegistry.SetPaused(sessionId, paused: true);
						break;

					case MessageTypes.FlowResume:
						_sessionRegistry.SetPaused(sessionId, paused: false);
						break;

					case MessageTypes.ProtocolError:
						PluginWebSocketLog.PluginProtocolError(_logger, pluginId, envelope.Error?.Code);
						break;

					case MessageTypes.LogPublish:
						// Deliberately a plain TryWrite whose result is ignored: dropping under load is the
						// correct policy for log traffic, not a defect to report - see
						// InboundDispatchState.LogQueue's remarks. No QUEUE_OVERFLOW, no close, no
						// ApplyBackpressureAsync coupling; a log burst must never look like a capability
						// protocol violation to this loop.
						dispatch.LogQueue.Writer.TryWrite(envelope);
						break;

					// Everything below is handled by RouteQueuedAsync off the queue below, rather than
					// inline, so a slow one cannot block this loop. TryEnqueue reserves depth before
					// writing, so a full queue is detected here rather than discovered as a stall - see
					// InboundDispatchState's remarks.
					case MessageTypes.HostInvoke:
					case MessageTypes.HostCancel:
					case MessageTypes.EventPublish:
					case MessageTypes.CapabilityDeclare:
					case MessageTypes.StateUpdate:
					case MessageTypes.AssetBegin:
					case MessageTypes.AssetChunk:
					case MessageTypes.AssetCommit:
					case MessageTypes.AssetAck:
						if (!TryEnqueue(dispatch, envelope))
						{
							await SendProtocolErrorAsync(connection,
								ProtocolErrorCodes.QueueOverflow,
								envelope.Id,
								loopCts.Token);
							await CloseAsync(socket,
								ProtocolCloseCodes.QueueOverflow,
								"Inbound queue overflow.",
								loopCts.Token);
							await loopCts.CancelAsync();
							break;
						}

						await ApplyBackpressureAsync(connection, dispatch, loopCts.Token);
						break;

					default:
						await SendProtocolErrorAsync(connection,
							ProtocolErrorCodes.UnknownMessageType,
							envelope.Id,
							loopCts.Token);
						break;
				}
			}
		}
		finally
		{
			await loopCts.CancelAsync();
			await Quietly(keepAlive);
			await Quietly(processing);
			await Quietly(logProcessing);

			// Frees any transfer this connection had open but never committed - an upload cannot outlive
			// the socket it arrived on, even across a resumable detach: the plugin re-pushes its icon on
			// every fresh connect (see IconAssetPublisherHostedService), so nothing is lost by dropping it.
			// Guarded by IsCurrentConnection because DropSession is keyed by plugin id, not by connection:
			// without this check, this (old, still unwinding) connection's drop could wipe transfers a
			// newer connection for the same plugin has already begun, if that reconnect landed before this
			// finally block ran.
			if (_sessionRegistry.IsCurrentConnection(sessionId, connection))
			{
				_assetReceiver.DropSession(pluginId);
				_hostAssetSender?.DropSession(pluginId);
			}

			if (!saidGoodbye)
			{
				_sessionRegistry.Detach(sessionId, _timeProvider.GetUtcNow());
			}

			await _mediator.Publish(new PluginSessionsChangedNotification(), CancellationToken.None);
		}
	}

	internal async Task HandleCapabilityResultAsync(
		IPluginConnection connection,
		string pluginId,
		ProtocolEnvelope envelope,
		CancellationToken cancellationToken)
	{
		if (_invoker.TryComplete(pluginId, envelope))
		{
			return;
		}

		var outcome = CorrelationRules.Resolve(envelope.Type,
			hasCorrelationId: envelope.CorrelationId is not null,
			isKnownCorrelation: false,
			wasTimedOut: false);

		switch (outcome)
		{
			case CorrelationOutcome.MalformedEnvelope:
				await SendProtocolErrorAsync(connection,
					ProtocolErrorCodes.MalformedEnvelope,
					envelope.Id,
					cancellationToken);
				break;

			case CorrelationOutcome.CorrelationUnknown:
				await SendProtocolErrorAsync(connection,
					ProtocolErrorCodes.CorrelationUnknown,
					envelope.Id,
					cancellationToken);
				break;

			case CorrelationOutcome.Accept:
			case CorrelationOutcome.Drop:
			default:
				break;
		}
	}

	internal sealed class InboundDispatchState
	{
		public Channel<ProtocolEnvelope> Queue { get; } = Channel.CreateBounded<ProtocolEnvelope>(
			new BoundedChannelOptions(ProtocolLimits.MaxInboundQueueDepth)
			{
				FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = true
			});

		public int Depth;

		public int PauseRequested;

		public Channel<ProtocolEnvelope> LogQueue { get; } = Channel.CreateBounded<ProtocolEnvelope>(
			new BoundedChannelOptions(ProtocolLimits.MaxLogInboundQueueDepth)
			{
				FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true, SingleWriter = true
			});
	}

	private async Task ProcessQueuedMessagesAsync(
		PluginWebSocketConnection connection,
		string pluginId,
		InboundDispatchState dispatch,
		CancellationToken cancellationToken)
	{
		try
		{
			await foreach (var envelope in dispatch.Queue.Reader.ReadAllAsync(cancellationToken))
			{
				try
				{
					await RouteQueuedAsync(connection, pluginId, envelope, cancellationToken);
				}
				finally
				{
					Interlocked.Decrement(ref dispatch.Depth);
					await ApplyBackpressureAsync(connection, dispatch, cancellationToken);
				}
			}
		}
		catch (Exception exception) when (exception is OperationCanceledException or WebSocketException)
		{
		}
	}

	private async Task RouteQueuedAsync(
		PluginWebSocketConnection connection,
		string pluginId,
		ProtocolEnvelope envelope,
		CancellationToken cancellationToken)
	{
		switch (envelope.Type)
		{
			case MessageTypes.HostInvoke:
				await HandleHostInvokeAsync(connection, pluginId, envelope, cancellationToken);
				break;

			case MessageTypes.HostCancel:
				break;

			case MessageTypes.EventPublish:
				HandleEventPublish(pluginId, envelope);
				break;

			case MessageTypes.CapabilityDeclare:
				await HandleCapabilityDeclareAsync(connection, pluginId, envelope, cancellationToken);
				break;

			case MessageTypes.StateUpdate:
				await HandleStateUpdateAsync(pluginId, envelope, cancellationToken);
				break;

			case MessageTypes.AssetBegin:
			case MessageTypes.AssetChunk:
			case MessageTypes.AssetCommit:
				await HandleAssetMessageAsync(connection, pluginId, envelope, cancellationToken);
				break;

			case MessageTypes.AssetAck:
				// The host never uploads over asset.* - that direction is plugin-to-host by declaration and
				// stays that way. What the host pushes travels over host.asset.*, whose ack is answered
				// inline in the receive loop.
				break;
		}
	}

	internal async Task HandleStateUpdateAsync(string pluginId,
		ProtocolEnvelope envelope,
		CancellationToken cancellationToken)
	{
		StateUpdatePayload? payload;
		try
		{
			payload = envelope.Payload?.Deserialize<StateUpdatePayload>(PluginProtocolJson.Options);
		}
		catch (JsonException)
		{
			payload = null;
		}

		if (payload is null || string.IsNullOrEmpty(payload.Kind))
		{
			return;
		}

		await _coalescer.RunAsync($"state:{pluginId}:{payload.Kind}",
				async () =>
				{
					try
					{
						var result = await _snapshotRefresher
							.RefreshKindAsync(pluginId, payload.Kind, cancellationToken)
							.ConfigureAwait(false);

						if (result.AllSucceeded)
						{
							await _registrar.ApplyRefreshedSnapshotAsync(pluginId, result.Snapshot, cancellationToken)
								.ConfigureAwait(false);
						}
					}
					catch (Exception exception) when (exception is not OutOfMemoryException)
					{
						PluginWebSocketLog.StateUpdateRefreshFailed(_logger, pluginId, payload.Kind, exception);
					}
				})
			.ConfigureAwait(false);
	}

	internal async Task HandleCapabilityDeclareAsync(
		IPluginConnection connection,
		string pluginId,
		ProtocolEnvelope envelope,
		CancellationToken cancellationToken)
	{
		CapabilityDeclarePayload? payload;
		try
		{
			payload = envelope.Payload?.Deserialize<CapabilityDeclarePayload>(PluginProtocolJson.Options);
		}
		catch (JsonException)
		{
			payload = null;
		}

		if (payload is null)
		{
			return;
		}

		var negotiated = _sessionRegistry.UpdateDeclaredCapabilities(pluginId, payload.Capabilities);
		if (negotiated is null)
		{
			return;
		}

		await connection.Send(new ProtocolEnvelope
			{
				Type = MessageTypes.CapabilityDeclareAck,
				Id = NewId(),
				CorrelationId = envelope.Id,
				Payload = JsonSerializer.SerializeToElement(
					new CapabilityDeclareAckPayload { Capabilities = [.. negotiated.Values] },
					PluginProtocolJson.Options)
			},
			cancellationToken);

		await _coalescer.RunAsync($"declare:{pluginId}",
				async () =>
				{
					try
					{
						await _registrar.UnregisterAsync(pluginId, cancellationToken).ConfigureAwait(false);
						await _registrar.RegisterAsync(pluginId, cancellationToken).ConfigureAwait(false);
					}
					catch (Exception exception) when (exception is not OutOfMemoryException)
					{
						PluginWebSocketLog.CapabilityDeclareReregisterFailed(_logger, pluginId, exception);
					}
				})
			.ConfigureAwait(false);
	}

	internal async Task HandleAssetMessageAsync(
		IPluginConnection connection,
		string pluginId,
		ProtocolEnvelope envelope,
		CancellationToken cancellationToken)
	{
		AssetOperationResult result;
		string? assetId;
		int? index = null;

		switch (envelope.Type)
		{
			case MessageTypes.AssetBegin:
				var begin = TryDeserialize<AssetBeginPayload>(envelope.Payload);
				assetId = begin?.AssetId;
				result = begin is null
					? AssetOperationResult.Fail(ProtocolErrorCodes.InvalidPayload, "Malformed asset.begin.")
					: _assetReceiver.Begin(pluginId,
						begin.AssetId,
						begin.Kind,
						begin.MimeType,
						begin.TotalBytes,
						begin.ContentHash);
				break;

			case MessageTypes.AssetChunk:
				var chunk = TryDeserialize<AssetChunkPayload>(envelope.Payload);
				assetId = chunk?.AssetId;
				index = chunk?.Index;
				result = chunk is null
					? AssetOperationResult.Fail(ProtocolErrorCodes.InvalidPayload, "Malformed asset.chunk.")
					: DecodeAndDispatchChunk(pluginId, chunk);
				break;

			case MessageTypes.AssetCommit:
				var commit = TryDeserialize<AssetCommitPayload>(envelope.Payload);
				assetId = commit?.AssetId;
				result = commit is null
					? AssetOperationResult.Fail(ProtocolErrorCodes.InvalidPayload, "Malformed asset.commit.")
					: _assetReceiver.Commit(pluginId, commit.AssetId);
				break;

			default:
				return;
		}

		if (!result.Accepted)
		{
			PluginWebSocketLog.AssetStepRejected(_logger, pluginId, assetId, result.ErrorCode);
		}

		await connection.Send(new ProtocolEnvelope
			{
				Type = MessageTypes.AssetAck,
				Id = NewId(),
				CorrelationId = envelope.Id,
				Payload = JsonSerializer.SerializeToElement(new AssetAckPayload
						{ AssetId = assetId ?? string.Empty, Index = index, Accepted = result.Accepted },
					PluginProtocolJson.Options)
			},
			cancellationToken);
	}

	private AssetOperationResult DecodeAndDispatchChunk(string pluginId, AssetChunkPayload chunk)
	{
		byte[] data;
		try
		{
			data = Convert.FromBase64String(chunk.Data);
		}
		catch (FormatException)
		{
			return AssetOperationResult.Fail(ProtocolErrorCodes.InvalidPayload, "Malformed asset.chunk base64 data.");
		}

		return _assetReceiver.Chunk(pluginId, chunk.AssetId, chunk.Index, data);
	}

	private static T? TryDeserialize<T>(JsonElement? payload)
		where T : class
	{
		try
		{
			return payload?.Deserialize<T>(PluginProtocolJson.Options);
		}
		catch (JsonException)
		{
			return default;
		}
	}

	private async Task HandleHostInvokeAsync(
		PluginWebSocketConnection connection,
		string pluginId,
		ProtocolEnvelope envelope,
		CancellationToken cancellationToken)
	{
		HostInvokePayload? payload;
		try
		{
			payload = envelope.Payload?.Deserialize<HostInvokePayload>(PluginProtocolJson.Options);
		}
		catch (JsonException)
		{
			payload = null;
		}

		if (payload is null || string.IsNullOrEmpty(payload.Api) || string.IsNullOrEmpty(payload.Operation))
		{
			await SendProtocolErrorAsync(connection, ProtocolErrorCodes.InvalidPayload, envelope.Id, cancellationToken);
			return;
		}

		var result = await _callbackRouter.RouteAsync(pluginId, envelope.Id, payload, cancellationToken);

		await connection.Send(new ProtocolEnvelope
			{
				Type = MessageTypes.HostResult,
				Id = NewId(),
				CorrelationId = envelope.Id,
				Error = result.Error,
				Payload = result.Error is null
					? JsonSerializer.SerializeToElement(new HostResultPayload { Data = result.Data },
						PluginProtocolJson.Options)
					: null
			},
			cancellationToken);
	}

	private async Task ProcessLogQueueAsync(
		PluginWebSocketConnection connection,
		string pluginId,
		string sessionId,
		InboundDispatchState dispatch,
		CancellationToken cancellationToken)
	{
		try
		{
			await foreach (var envelope in dispatch.LogQueue.Reader.ReadAllAsync(cancellationToken))
			{
				await HandleLogPublishAsync(connection, pluginId, sessionId, envelope, cancellationToken);
			}
		}
		catch (Exception exception) when (exception is OperationCanceledException or WebSocketException)
		{
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			PluginWebSocketLog.LogPublishFailed(_logger, pluginId, exception);
		}
	}

	internal async Task HandleLogPublishAsync(
		IPluginConnection connection,
		string pluginId,
		string sessionId,
		ProtocolEnvelope envelope,
		CancellationToken cancellationToken)
	{
		LogPublishPayload? payload;
		try
		{
			payload = envelope.Payload?.Deserialize<LogPublishPayload>(PluginProtocolJson.Options);
		}
		catch (JsonException)
		{
			payload = null;
		}

		var events = payload?.Events;
		if (events is null || events.Count == 0 || events.Any(logEvent => logEvent is null))
		{
			return;
		}

		var result = _logIngestor.Ingest(pluginId, sessionId, events);

		if (result.Outcome == PluginLogIngestOutcome.BatchTooLarge)
		{
			await SendProtocolErrorAsync(connection,
				ProtocolErrorCodes.PayloadTooLarge,
				envelope.Id,
				cancellationToken);
			return;
		}

		if (result.RateLimitTransitionedToLimited)
		{
			PluginWebSocketLog.LogPublishRateLimited(_logger, pluginId, result.RateLimitedCount);
			await SendProtocolErrorAsync(connection, ProtocolErrorCodes.RateLimited, envelope.Id, cancellationToken);
		}
	}

	internal void HandleEventPublish(string pluginId, ProtocolEnvelope envelope)
	{
		EventPublishPayload? payload;
		try
		{
			payload = envelope.Payload?.Deserialize<EventPublishPayload>(PluginProtocolJson.Options);
		}
		catch (JsonException)
		{
			payload = null;
		}

		if (payload is null || string.IsNullOrEmpty(payload.EventId))
		{
			return;
		}

		var parameters = ToParameterDictionary(payload.Parameters);
		new IntegrationEventPublisher(pluginId, _eventBus, Serilog.Log.Logger).Publish(payload.EventId, parameters);
	}

	private static Dictionary<string, object?>? ToParameterDictionary(JsonElement? element)
	{
		if (element is not { ValueKind: JsonValueKind.Object } value)
		{
			return null;
		}

		var result = new Dictionary<string, object?>(StringComparer.Ordinal);
		foreach (var property in value.EnumerateObject())
		{
			result[property.Name] = property.Value.ValueKind switch
			{
				JsonValueKind.String => property.Value.GetString(),
				JsonValueKind.Number => property.Value.TryGetInt64(out var i) ? i : property.Value.GetDouble(),
				JsonValueKind.True => true,
				JsonValueKind.False => false,
				JsonValueKind.Object or JsonValueKind.Array => property.Value.GetRawText(),
				_ => null
			};
		}

		return result;
	}

	internal static bool TryEnqueue(InboundDispatchState dispatch, ProtocolEnvelope envelope)
	{
		if (Interlocked.Increment(ref dispatch.Depth) > ProtocolLimits.MaxInboundQueueDepth)
		{
			Interlocked.Decrement(ref dispatch.Depth);
			return false;
		}

		if (dispatch.Queue.Writer.TryWrite(envelope))
		{
			return true;
		}

		Interlocked.Decrement(ref dispatch.Depth);
		return false;
	}

	internal static async Task ApplyBackpressureAsync(
		IPluginConnection connection,
		InboundDispatchState dispatch,
		CancellationToken cancellationToken)
	{
		var depth = Volatile.Read(ref dispatch.Depth);

		if (depth >= ProtocolLimits.QueueHighWatermark &&
			Interlocked.CompareExchange(ref dispatch.PauseRequested, 1, 0) == 0)
		{
			await connection.Send(new ProtocolEnvelope
				{
					Type = MessageTypes.FlowPause,
					Id = NewId(),
					Payload = JsonSerializer.SerializeToElement(
						new BackpressurePayload { Reason = "The host's inbound queue is filling." },
						PluginProtocolJson.Options)
				},
				cancellationToken);
		}
		else if (depth <= ProtocolLimits.QueueLowWatermark &&
			Interlocked.CompareExchange(ref dispatch.PauseRequested, 0, 1) == 1)
		{
			await connection.Send(new ProtocolEnvelope
				{
					Type = MessageTypes.FlowResume,
					Id = NewId(),
					Payload = JsonSerializer.SerializeToElement(
						new BackpressurePayload { Reason = "The host's inbound queue has drained." },
						PluginProtocolJson.Options)
				},
				cancellationToken);
		}
	}

	private async Task KeepAliveLoopAsync(
		WebSocket socket,
		PluginWebSocketConnection connection,
		Func<long> lastInboundTicks,
		CancellationToken cancellationToken)
	{
		try
		{
			using var timer = new PeriodicTimer(ProtocolTimeouts.KeepAliveInterval, _timeProvider);
			while (await timer.WaitForNextTickAsync(cancellationToken))
			{
				var silence = _timeProvider.GetUtcNow() - new DateTimeOffset(lastInboundTicks(), TimeSpan.Zero);

				if (silence > ProtocolTimeouts.KeepAliveTimeout)
				{
					socket.Abort();
					return;
				}

				await connection.Send(new ProtocolEnvelope { Type = MessageTypes.SessionPing, Id = NewId() },
					cancellationToken);
			}
		}
		catch (Exception exception) when (exception is OperationCanceledException
			or WebSocketException
			or ObjectDisposedException)
		{
		}
	}

	private async Task SendProtocolErrorAsync(
		IPluginConnection connection,
		string code,
		string? correlationId,
		CancellationToken cancellationToken)
	{
		try
		{
			await connection.Send(new ProtocolEnvelope
				{
					Type = MessageTypes.ProtocolError,
					Id = NewId(),
					CorrelationId = correlationId,
					Error = new ProtocolError
						{ Code = code, Message = ProtocolErrorMessages.For(code), Retryable = false }
				},
				cancellationToken);
		}
		catch (Exception exception) when (exception is WebSocketException or OperationCanceledException)
		{
			PluginWebSocketLog.ProtocolErrorDeliveryFailed(_logger, exception);
		}
	}

	private static async Task CloseNormallyAsync(WebSocket socket, string reason, CancellationToken cancellationToken)
		=> await CloseAsync(socket, (int)WebSocketCloseStatus.NormalClosure, reason, cancellationToken);

	private static async Task CloseGracefullyAsync(WebSocket socket, CancellationToken cancellationToken)
	{
		using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		budget.CancelAfter(ProtocolTimeouts.GracefulClose);

		await CloseAsync(socket, (int)WebSocketCloseStatus.NormalClosure, "Goodbye.", budget.Token);
	}

	private static async Task CloseAsync(WebSocket socket,
		int closeCode,
		string reason,
		CancellationToken cancellationToken)
	{
		try
		{
			if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
			{
				await socket.CloseAsync((WebSocketCloseStatus)closeCode, reason, cancellationToken);
			}
		}
		catch (Exception exception) when (exception is WebSocketException
			or OperationCanceledException
			or ObjectDisposedException)
		{
		}
	}

	private static async Task<byte[]?> ReceiveMessageAsync(WebSocket socket, CancellationToken cancellationToken)
		=> await WebSocketMessageIO.ReceiveAsync(socket,
			ProtocolLimits.MaxMessageBytes,
			requireText: false,
			rejectOversized: false,
			cancellationToken);

	private static string ThrottleKey(HttpContext context)
	{
		var header = context.Request.Headers[PluginAuthDefaults.AuthorizationHeaderName].ToString();
		var prefix = PluginAuthDefaults.BearerScheme + " ";

		if (!string.IsNullOrEmpty(header) && header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
		{
			var rawToken = header[prefix.Length..].Trim();
			try
			{
				var sessionId = new JsonWebTokenHandler().ReadJsonWebToken(rawToken)
					.GetClaim(PluginClaimTypes.SessionId).Value;
				if (!string.IsNullOrEmpty(sessionId))
				{
					return $"plugin-ws|{sessionId}";
				}
			}
			catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
			{
			}
		}

		return "plugin-ws|anonymous";
	}

	private static async Task WriteErrorAsync(HttpContext context, ProtocolError error, int statusCode)
	{
		context.Response.StatusCode = statusCode;
		context.Response.ContentType = ProtocolConstants.JsonMediaType;
		await JsonSerializer.SerializeAsync(context.Response.Body,
			error,
			PluginProtocolJson.Options,
			context.RequestAborted);
	}

	private static string NewId() => Guid.CreateVersion7().ToString();

	private static async Task Quietly(Task task)
	{
		try
		{
			await task;
		}
		catch (Exception exception) when (exception is OperationCanceledException or WebSocketException)
		{
		}
	}
}
