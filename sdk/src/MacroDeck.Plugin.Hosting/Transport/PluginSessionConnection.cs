using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Logging;
using MacroDeck.Plugin.Protocol.Correlation;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using Serilog;

namespace MacroDeck.Plugin.Hosting.Transport;

/// <summary>
/// One connected session: the handshake over the socket, the send and receive loops, keepalive,
/// backpressure and orderly teardown. Created per connection attempt and discarded when it ends.
/// </summary>
internal sealed class PluginSessionConnection(
	IPluginSocket socket,
	PluginSession session,
	CapabilityDispatcher dispatcher,
	PluginConnectionState state,
	TimeProvider timeProvider,
	ILogger logger,
	IHostInvoker? hostInvoker = null,
	HostStateCache? hostStateCache = null,
	IPluginAssetUploader? assetUploader = null,
	IPluginHostAssetReceiver? hostAssets = null) : IAsyncDisposable
{
	private readonly Channel<ProtocolEnvelope> _inbound = Channel.CreateBounded<ProtocolEnvelope>(
		new BoundedChannelOptions(session.Limits.MaxInboundQueueDepth)
		{
			FullMode = BoundedChannelFullMode.Wait,
			SingleReader = true,
			SingleWriter = true
		});

	// Two outbound queues, because the pause-exempt types must not queue behind the ones a flow.pause
	// holds back. Sharing one would leave a capability.result stuck behind an event.publish the pause
	// has stopped: the host waits for a result that cannot move, which is the deadlock the exemption
	// exists to prevent. Each stays bounded, so backpressure is still a full queue and not a leak.
	private readonly Channel<ProtocolEnvelope> _outbound = OutboundQueue(session);
	private readonly Channel<ProtocolEnvelope> _exemptOutbound = OutboundQueue(session);

	// One writer at a time. ClientWebSocket permits a single outstanding SendAsync and throws on an
	// overlap, and five loops legitimately send: the receive loop answers pings, the process loop
	// adjusts flow control, the keepalive loop pings, and the two send loops drain their queues.
	private readonly SemaphoreSlim _sending = new(1, 1);

	// Bounds how many capability.invoke dispatches ProcessLoopAsync has launched but not yet awaited.
	// Without this, a burst deeper than the dispatcher's own MaxConcurrentInvocations gate would launch
	// them all anyway, and every one past the 32nd would come straight back as an immediate RateLimited
	// reply instead of waiting its turn the way a real concurrency limit implies.
	private readonly SemaphoreSlim _dispatchConcurrency = new(ProtocolLimits.MaxConcurrentInvocations);

	// Completed when this connection is over, whatever ended it. Set rather than reasoned about,
	// because "is this connection still usable" is asked from code that holds no other part of it -
	// see Ended.
	private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

	private volatile TaskCompletionSource? _paused;
	private int _inboundDepth;
	private int _pauseRequested;
	private long _lastInboundTicks;

	/// <summary>
	/// Completes once this connection has ended. <see cref="HostInvoker" /> races it against the
	/// invocations it sent through this connection: a <c>host.result</c> can no longer arrive on a
	/// connection that is gone, so a caller that kept waiting would only wait out the request timeout.
	/// </summary>
	public Task Ended => _ended.Task;

	/// <summary>True when the host reported this connection as resuming a prior session.</summary>
	public bool Resumed { get; private set; }

	/// <summary>
	/// True when the host ended the session voluntarily. A session torn down that way is not
	/// resumable, so the next attempt must open a new one rather than present a dead id.
	/// </summary>
	public bool HostSaidGoodbye { get; private set; }

	/// <summary>
	/// Opens the session over the socket and runs it until it ends. Never throws for a connection
	/// problem - the reason comes back as an outcome, because "why did it end" is a decision the
	/// reconnect loop makes and not an exception the caller has to classify.
	/// </summary>
	public async Task<ConnectionOutcome> RunAsync(
		string? resumeSessionId,
		string? instanceId,
		CancellationToken cancellationToken)
	{
		try
		{
			var handshake = await HandshakeAsync(resumeSessionId, instanceId, cancellationToken);
			if (handshake is not null)
			{
				return handshake;
			}

			using var loops = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

			var send = SendLoopAsync(_outbound.Reader, exemptWhilePaused: false, loops.Token);
			var sendExempt = SendLoopAsync(_exemptOutbound.Reader, exemptWhilePaused: true, loops.Token);
			var process = ProcessLoopAsync(loops.Token);
			var keepAlive = KeepAliveLoopAsync(loops.Token);

			try
			{
				return await ReceiveLoopAsync(loops.Token);
			}
			finally
			{
				// Before the loops are waited on, not after: a dispatch still awaiting a host.invoke
				// reply is one of the things being waited for here, and this signal is what lets it stop
				// waiting. Signalling afterwards would make each wait for the other.
				_ended.TrySetResult();

				// In a finally, because the receive loop can also throw: leaving four loops running
				// against a socket the caller is about to dispose is how a healthy shutdown turns into
				// an unobserved ObjectDisposedException.
				await loops.CancelAsync();
				await Task.WhenAll(Quietly(send), Quietly(sendExempt), Quietly(process), Quietly(keepAlive));
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return ConnectionOutcome.Stopped;
		}
		catch (WebSocketException exception)
		{
			return ConnectionOutcome.Retry(exception.Message, socket.CloseCode);
		}
		finally
		{
			// Repeated here for the paths that never reached the loops - a refused handshake, a socket
			// that faulted - so no caller is left waiting on a connection that is over.
			_ended.TrySetResult();
			dispatcher.AbortInFlight();
		}
	}

	/// <summary>
	/// Queues an envelope. Waits when the outbound queue is full rather than dropping. Never waits on a
	/// flow.pause: the queue is where paused traffic waits, so a caller holding a result is not made to
	/// wait for the pause that result would lift.
	/// </summary>
	public ValueTask SendAsync(ProtocolEnvelope envelope, CancellationToken cancellationToken)
		=> QueueFor(envelope.Type).Writer.WriteAsync(Stamp(envelope), cancellationToken);

	/// <summary>
	/// Says goodbye and waits for the socket to close, bounded by the protocol's graceful-close budget.
	/// A session torn down this way is deliberately not resumable, so the host releases the slot at
	/// once instead of holding it for a peer that has said it is leaving.
	/// </summary>
	public async Task GoodbyeAsync(string reason, CancellationToken cancellationToken)
	{
		try
		{
			await SendDirectAsync(new ProtocolEnvelope
				{
					Type = MessageTypes.SessionGoodbye,
					Id = NewId(),
					Payload = Serialize(new SessionGoodbyePayload { Reason = reason })
				},
				cancellationToken);

			using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			budget.CancelAfter(session.Timeouts.GracefulClose);

			await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, reason, budget.Token);
		}
		catch (Exception exception) when (exception is WebSocketException
			or OperationCanceledException
			or ObjectDisposedException)
		{
			// The point of saying goodbye is politeness towards a host that is still there. One that
			// is not cannot be told, and shutdown carries on either way.
			logger.GracefulCloseFailed(exception);
		}
	}

	public ValueTask DisposeAsync()
	{
		_sending.Dispose();
		_dispatchConcurrency.Dispose();
		return socket.DisposeAsync();
	}

	private async Task<ConnectionOutcome?> HandshakeAsync(
		string? resumeSessionId,
		string? instanceId,
		CancellationToken cancellationToken)
	{
		await SendDirectAsync(new ProtocolEnvelope
			{
				Type = MessageTypes.SessionHello,
				Id = NewId(),
				ProtocolVersion = session.NegotiatedVersion,
				Payload = Serialize(new SessionHelloPayload
				{
					ProtocolVersion = session.NegotiatedVersion,
					SessionId = session.SessionId,
					ResumeSessionId = resumeSessionId,
					InstanceId = instanceId
				})
			},
			cancellationToken);

		using var handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		handshake.CancelAfter(session.Timeouts.Handshake);

		byte[]? message;
		try
		{
			message = await socket.ReceiveAsync(handshake.Token);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return ConnectionOutcome.Retry("The host did not answer session.hello in time.");
		}

		if (message is null)
		{
			return Classify("The host closed the socket during the handshake.");
		}

		var read = ProtocolEnvelopeReader.Read(message);

		if (!read.Succeeded || read.Envelope is not { } envelope)
		{
			return ConnectionOutcome.Retry("The host's handshake reply could not be read.");
		}

		if (!string.Equals(envelope.Type, MessageTypes.SessionWelcome, StringComparison.Ordinal))
		{
			if (envelope.Error is not { } error)
			{
				return ConnectionOutcome.Retry($"Expected session.welcome, got '{envelope.Type}'.");
			}

			// A refused resume arrives as a protocol error rather than a close code, so it has to be
			// read here: retrying without dropping the session id would present the same dead id
			// until the resume window expired.
			if (string.Equals(error.Code, ProtocolErrorCodes.SessionNotResumable, StringComparison.Ordinal))
			{
				HostSaidGoodbye = true;
				return ConnectionOutcome.Retry("The session could not be resumed.");
			}

			return Classify($"The host refused the session: {error.Code}.");
		}

		var welcome = Deserialize<SessionWelcomePayload>(envelope.Payload);
		Resumed = welcome?.Resumed ?? false;

		// Ready is "the host has welcomed us", not "the session has ended" - so it is set here rather
		// than by whatever eventually unwinds the connection.
		state.Status = PluginConnectionStatus.Connected;

		// Also the backoff reset, not just the reported one: reaching a welcome is what says the trouble
		// is over, so the next drop starts the schedule again from the initial delay. The reconnect loop
		// reads this counter back rather than keeping its own.
		state.SetReconnectAttempt(0);
		state.RaiseConnected(Resumed);

		Touch();
		return null;
	}

	private async Task<ConnectionOutcome> ReceiveLoopAsync(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			byte[]? message;

			try
			{
				message = await socket.ReceiveAsync(cancellationToken);
			}
			catch (PluginMessageTooLargeException exception)
			{
				// Reported and survived, not fatal: the peer sent one thing too big, which says
				// nothing about the next message.
				logger.MessageTooLarge(exception.Message);
				await SendDirectAsync(ProtocolErrorEnvelope(ProtocolErrorCodes.PayloadTooLarge, null),
					cancellationToken);
				continue;
			}

			if (message is null)
			{
				return Classify("The host closed the socket.");
			}

			Touch();

			var read = ProtocolEnvelopeReader.Read(message);

			if (!read.Succeeded)
			{
				// Neither a malformed envelope nor an unrecognised message type closes the socket:
				// that tolerance is what lets a newer host talk to an older plugin at all. The
				// preserved id, when there is one, is what makes the complaint attributable.
				await SendDirectAsync(ProtocolErrorEnvelope(read.Error?.Code ?? ProtocolErrorCodes.MalformedEnvelope,
						read.Envelope?.Id),
					cancellationToken);
				continue;
			}

			var envelope = read.Envelope!;

			if (HandleImmediately(envelope, cancellationToken) is { } immediate)
			{
				await immediate;
				continue;
			}

			if (!TryEnqueue(envelope))
			{
				await SendDirectAsync(ProtocolErrorEnvelope(ProtocolErrorCodes.QueueOverflow, envelope.Id),
					cancellationToken);
				await socket.CloseOutputAsync((WebSocketCloseStatus)ProtocolCloseCodes.QueueOverflow,
					"Inbound queue overflow.",
					CancellationToken.None);

				return ConnectionOutcome.Retry("The inbound queue overflowed.", ProtocolCloseCodes.QueueOverflow);
			}

			await ApplyBackpressureAsync(cancellationToken);
		}

		return ConnectionOutcome.Stopped;
	}

	/// <summary>
	/// The messages that must be answered from the receive loop rather than queued behind work:
	/// keepalive, flow control and cancellation. Queuing a cancel behind the invocation it cancels
	/// would make it useless, and queuing a ping behind a full queue would look like a dead peer.
	/// </summary>
	private Task? HandleImmediately(ProtocolEnvelope envelope, CancellationToken cancellationToken)
	{
		switch (envelope.Type)
		{
			case MessageTypes.SessionPing:
				return SendDirectAsync(new ProtocolEnvelope
					{
						Type = MessageTypes.SessionPong,
						Id = NewId(),
						CorrelationId = envelope.Id
					},
					cancellationToken);

			case MessageTypes.SessionPong:
				return Task.CompletedTask;

			case MessageTypes.FlowPause:
				Pause(Deserialize<BackpressurePayload>(envelope.Payload));
				return Task.CompletedTask;

			case MessageTypes.FlowResume:
				Resume();
				return Task.CompletedTask;

			case MessageTypes.CapabilityCancel:
				var result = dispatcher.Cancel(envelope);
				return result is null ? Task.CompletedTask : SendDirectAsync(result, cancellationToken);

			// Handled inline rather than queued behind ProcessLoopAsync's dispatch work: a host.result
			// completes a HostInvoker.InvokeAsync call that may itself be awaited from inside a
			// capability handler ProcessLoopAsync is running concurrently. Queuing it here would make
			// that handler wait on its own receive loop - the deadlock issue #413's concurrent dispatch
			// (step 2) and the host's inline/queued split both exist to avoid.
			case MessageTypes.HostResult:
				return HandleHostResultAsync(envelope, cancellationToken);

			// A push, not a reply - just a cache write, so there is no reason to make it wait behind
			// queued work either.
			case MessageTypes.HostState:
				hostStateCache?.Apply(envelope);
				return Task.CompletedTask;

			// A reply, not a push: completes the pending upload step in PluginAssetUploader.SendAndAwaitAckAsync,
			// the same inline-from-the-receive-loop treatment host.result gets and for the same reason -
			// an upload step may itself be awaited from code the process loop is running concurrently.
			case MessageTypes.AssetAck:
				assetUploader?.TryComplete(envelope);
				return Task.CompletedTask;

			// The opposite direction's steps. Answered inline for the same reason host.result is: the
			// transfer they carry is normally being awaited by a capability handler the process loop is
			// running, so queuing them behind that loop would make the handler wait on its own bytes.
			case MessageTypes.HostAssetBegin:
			case MessageTypes.HostAssetChunk:
			case MessageTypes.HostAssetCommit:
				return hostAssets is null
					? Task.CompletedTask
					: SendDirectAsync(hostAssets.Handle(envelope), cancellationToken);

			case MessageTypes.ProtocolError:
				logger.HostProtocolError(envelope.Error?.Code);
				return Task.CompletedTask;

			default:
				return null;
		}
	}

	/// <summary>Routes an inbound <c>host.result</c> to the invoker, reporting an unrecognised
	/// correlation the same way the host's own <c>HandleCapabilityResultAsync</c> does for the opposite
	/// direction.</summary>
	private Task HandleHostResultAsync(ProtocolEnvelope envelope, CancellationToken cancellationToken)
	{
		if (hostInvoker is null || hostInvoker.TryComplete(envelope))
		{
			return Task.CompletedTask;
		}

		var outcome = CorrelationRules.Resolve(envelope.Type,
			hasCorrelationId: envelope.CorrelationId is not null,
			isKnownCorrelation: false,
			wasTimedOut: false);

		return outcome switch
		{
			CorrelationOutcome.MalformedEnvelope
				=> SendDirectAsync(ProtocolErrorEnvelope(ProtocolErrorCodes.MalformedEnvelope, envelope.Id),
					cancellationToken),
			CorrelationOutcome.CorrelationUnknown
				=> SendDirectAsync(ProtocolErrorEnvelope(ProtocolErrorCodes.CorrelationUnknown, envelope.Id),
					cancellationToken),
			_ => Task.CompletedTask
		};
	}

	private async Task ProcessLoopAsync(CancellationToken cancellationToken)
	{
		// Tracks dispatches launched but not yet awaited, so a graceful close can wait for them instead
		// of abandoning whatever a handler was still doing.
		var inFlight = new List<Task>();

		try
		{
			await foreach (var envelope in _inbound.Reader.ReadAllAsync(cancellationToken))
			{
				switch (envelope.Type)
				{
					case MessageTypes.CapabilityInvoke:
						// Dispatched rather than awaited inline: a handler may call back into the host and
						// await its reply, and a loop that only ever runs one dispatch at a time would be the
						// thing standing between that handler and the very reply it is waiting for.
						await _dispatchConcurrency.WaitAsync(cancellationToken);
						inFlight.RemoveAll(task => task.IsCompleted);
						inFlight.Add(DispatchInvocationAsync(envelope, cancellationToken));
						break;

					case MessageTypes.SessionGoodbye:
						logger.HostEndedSession();
						HostSaidGoodbye = true;
						Interlocked.Decrement(ref _inboundDepth);
						await ApplyBackpressureAsync(cancellationToken);
						break;

					default:
						logger.UnhandledMessageType(envelope.Type);
						Interlocked.Decrement(ref _inboundDepth);
						await ApplyBackpressureAsync(cancellationToken);
						break;
				}
			}
		}
		finally
		{
			// Whatever ended the read loop - cancellation or the channel completing - the work already
			// handed out is not: awaiting it here is what keeps shutdown from abandoning a dispatch that
			// is still running.
			await Task.WhenAll(inFlight.Select(Quietly));
		}
	}

	/// <summary>
	/// Runs one capability.invoke dispatch outside the read loop. <see cref="CapabilityDispatcher.DispatchAsync" />
	/// already turns a handler's own exception into a failed result, so reaching the catch here means
	/// something outside the handler broke - most likely replying to a socket that is on its way down -
	/// and this is the only place left to report it, since nothing here awaits this task synchronously.
	/// </summary>
	private async Task DispatchInvocationAsync(ProtocolEnvelope envelope, CancellationToken cancellationToken)
	{
		try
		{
			try
			{
				await dispatcher.DispatchAsync(envelope, SendAsync, cancellationToken);
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				logger.CapabilityDispatchFailed(envelope.Id, exception);
			}
		}
		finally
		{
			_dispatchConcurrency.Release();
			Interlocked.Decrement(ref _inboundDepth);
			await ApplyBackpressureAsync(cancellationToken);
		}
	}

	/// <summary>
	/// Drains one outbound queue. The exempt queue gets its own loop and never waits on a flow.pause:
	/// its messages are the ones that unblock the host - results it is waiting on, and the control
	/// messages that let either side get out of the state - so they have to keep moving while the rest
	/// is held. Waiting here rather than at the queue is what keeps the held traffic in order and the
	/// memory it occupies bounded.
	/// </summary>
	private async Task SendLoopAsync(
		ChannelReader<ProtocolEnvelope> queue,
		bool exemptWhilePaused,
		CancellationToken cancellationToken)
	{
		await foreach (var envelope in queue.ReadAllAsync(cancellationToken))
		{
			// Re-checked after every wait, because the host may pause again while this one is draining.
			while (!exemptWhilePaused && _paused is { } paused)
			{
				await paused.Task.WaitAsync(cancellationToken);
			}

			await SendDirectAsync(envelope, cancellationToken);
		}
	}

	private async Task KeepAliveLoopAsync(CancellationToken cancellationToken)
	{
		using var timer = new PeriodicTimer(session.Timeouts.KeepAliveInterval, timeProvider);

		while (await timer.WaitForNextTickAsync(cancellationToken))
		{
			// Silence is measured against every inbound frame, not just pongs: a host that is
			// answering invocations is demonstrably alive whether or not it replies to a ping.
			var silence = timeProvider.GetUtcNow() -
				new DateTimeOffset(Volatile.Read(ref _lastInboundTicks), TimeSpan.Zero);

			if (silence > session.Timeouts.KeepAliveTimeout)
			{
				logger.KeepAliveTimedOut(silence);
				await socket.AbortAsync();
				return;
			}

			await SendDirectAsync(new ProtocolEnvelope { Type = MessageTypes.SessionPing, Id = NewId() },
				cancellationToken);
		}
	}

	/// <summary>
	/// Takes a slot in the inbound queue, or reports that there is none.
	///
	/// <para>
	/// The depth is reserved before the write rather than inferred from it. A bounded channel in a
	/// dropping mode reports every write as successful, and one in waiting mode would block the
	/// receive loop - which is the one loop that must keep running, because it is what answers the
	/// pings and drains the queue it is waiting on.
	/// </para>
	/// </summary>
	private bool TryEnqueue(ProtocolEnvelope envelope)
	{
		if (Interlocked.Increment(ref _inboundDepth) > session.Limits.MaxInboundQueueDepth)
		{
			Interlocked.Decrement(ref _inboundDepth);
			return false;
		}

		if (_inbound.Writer.TryWrite(envelope))
		{
			return true;
		}

		Interlocked.Decrement(ref _inboundDepth);
		return false;
	}

	/// <summary>
	/// Asks the host to pause once the inbound queue is filling, and to resume once it has drained
	/// well below that. The gap between the two watermarks is what stops a queue hovering at the limit
	/// from producing a stream of alternating control messages.
	/// </summary>
	private async Task ApplyBackpressureAsync(CancellationToken cancellationToken)
	{
		var depth = Volatile.Read(ref _inboundDepth);

		if (depth >= session.Limits.QueueHighWatermark && Interlocked.CompareExchange(ref _pauseRequested, 1, 0) == 0)
		{
			await SendDirectAsync(new ProtocolEnvelope
				{
					Type = MessageTypes.FlowPause,
					Id = NewId(),
					Payload = Serialize(new BackpressurePayload { Reason = "The plugin's inbound queue is filling." })
				},
				cancellationToken);
		}
		else if (depth <= session.Limits.QueueLowWatermark &&
			Interlocked.CompareExchange(ref _pauseRequested, 0, 1) == 1)
		{
			await SendDirectAsync(new ProtocolEnvelope
				{
					Type = MessageTypes.FlowResume,
					Id = NewId(),
					Payload = Serialize(new BackpressurePayload { Reason = "The plugin's inbound queue has drained." })
				},
				cancellationToken);
		}
	}

	private void Pause(BackpressurePayload? payload)
	{
		_paused ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		if (payload?.ResumeAfterMs is int after and > 0)
		{
			// The host may name a duration; honouring it means a lost flow.resume costs a delay rather
			// than a permanently muted plugin.
			_ = ResumeAfterAsync(TimeSpan.FromMilliseconds(after));
		}
	}

	private async Task ResumeAfterAsync(TimeSpan delay)
	{
		await Task.Delay(delay, timeProvider, CancellationToken.None);
		Resume();
	}

	private void Resume()
	{
		var paused = Interlocked.Exchange(ref _paused, null);
		paused?.TrySetResult();
	}

	private async Task SendDirectAsync(ProtocolEnvelope envelope, CancellationToken cancellationToken)
	{
		var bytes = ProtocolEnvelopeWriter.WriteToUtf8Bytes(Stamp(envelope));

		await _sending.WaitAsync(cancellationToken);

		try
		{
			// Bounded independently of the caller's own token, mirroring the host's
			// PluginWebSocketConnection.Send (issue #413 finding 1): a host that stops draining its
			// socket would otherwise park this call forever, wedging every loop that sends on this
			// connection - the receive loop's pongs and flow control, the keepalive loop, both send
			// loops - since all five share this one outstanding-send slot. Deliberately the real clock,
			// not timeProvider: this is a transport-health backstop of last resort, not a piece of
			// protocol timing a caller should be able to virtualize away - a test that substitutes a
			// ManualTimeProvider must not thereby also disable the one guard against a send that never
			// returns at all.
			var sendTask = socket.SendAsync(bytes, cancellationToken);
			var timeoutTask = Task.Delay(session.Timeouts.DefaultRequest, cancellationToken: CancellationToken.None);

			if (ReferenceEquals(await Task.WhenAny(sendTask, timeoutTask).ConfigureAwait(false), timeoutTask))
			{
				ObserveAbandonedSend(sendTask);
				await socket.AbortAsync().ConfigureAwait(false);
				throw new OperationCanceledException("The send to the host did not complete within the timeout.");
			}

			await sendTask.ConfigureAwait(false);
		}
		finally
		{
			_sending.Release();
		}
	}

	/// <summary>The socket send we gave up waiting for is still running in the background; observed here
	/// so its eventual fault (the abort above will make it throw) never surfaces as an unobserved task
	/// exception.</summary>
	private static void ObserveAbandonedSend(Task task)
		=> _ = task.ContinueWith(static t => _ = t.Exception,
			CancellationToken.None,
			TaskContinuationOptions.ExecuteSynchronously,
			TaskScheduler.Default);

	private Channel<ProtocolEnvelope> QueueFor(string messageType)
		=> ProtocolBackpressure.IsExemptWhilePaused(messageType) ? _exemptOutbound : _outbound;

	private static Channel<ProtocolEnvelope> OutboundQueue(PluginSession session)
		=> Channel.CreateBounded<ProtocolEnvelope>(new BoundedChannelOptions(session.Limits.MaxOutboundQueueDepth)
		{
			FullMode = BoundedChannelFullMode.Wait,
			SingleReader = true
		});

	private ProtocolEnvelope Stamp(ProtocolEnvelope envelope)
		=> envelope with
		{
			SentAt = envelope.SentAt ?? timeProvider.GetUtcNow(),
			ProtocolVersion = envelope.ProtocolVersion ?? session.NegotiatedVersion
		};

	private static ProtocolEnvelope ProtocolErrorEnvelope(string code, string? correlationId)
		=> new()
		{
			Type = MessageTypes.ProtocolError,
			Id = NewId(),
			CorrelationId = correlationId,
			Error = new ProtocolError
			{
				Code = code,
				Message = ProtocolErrorMessages.For(code),
				Retryable = false
			}
		};

	/// <summary>
	/// Reads the close code and decides whether another attempt is worth making. The codes the
	/// protocol defines are the whole vocabulary; anything else is a transport hiccup and retryable.
	/// </summary>
	private ConnectionOutcome Classify(string reason)
	{
		var code = socket.CloseCode;

		return code switch
		{
			ProtocolCloseCodes.ProtocolVersionUnsupported => ConnectionOutcome.Fail(
				"The host does not support this plugin's protocol version.",
				code),
			ProtocolCloseCodes.SessionReplaced => ConnectionOutcome.Fail(
				"Another instance of this plugin replaced the session.",
				code),
			ProtocolCloseCodes.AuthenticationFailed => ConnectionOutcome.Fail("The host rejected the session token.",
				code),
			ProtocolCloseCodes.SupervisorShutdown => ConnectionOutcome.Fail("The host supervisor stopped this plugin.",
				code),
			// Terminal by contract (see ProtocolCloseCodes.RegistrationRejected's remarks): the plugin's
			// declaration is deterministically bad, so retrying would just redeclare the same thing and be
			// rejected again, forever. Falling through to the default Retry here was the actual bug - a
			// bad declaration would loop reconnect/re-declare/reject indefinitely.
			ProtocolCloseCodes.RegistrationRejected => ConnectionOutcome.Fail(
				"The host rejected this plugin's declared capabilities.",
				code),
			_ => ConnectionOutcome.Retry(reason, code)
		};
	}

	private void Touch() => Volatile.Write(ref _lastInboundTicks, timeProvider.GetUtcNow().UtcTicks);

	private static string NewId() => Guid.CreateVersion7().ToString();

	private static JsonElement Serialize<T>(T value)
		=> JsonSerializer.SerializeToElement(value, PluginProtocolJson.Options);

	private static T? Deserialize<T>(JsonElement? payload)
	{
		if (payload is not { } element)
		{
			return default;
		}

		try
		{
			return element.Deserialize<T>(PluginProtocolJson.Options);
		}
		catch (JsonException)
		{
			return default;
		}
	}

	private static async Task Quietly(Task task)
	{
		try
		{
			await task;
		}
		catch (Exception exception) when (exception is OperationCanceledException or WebSocketException)
		{
			// These loops are cancelled as a matter of course when the connection ends; the reason the
			// connection ended is already known and is what gets reported.
		}
	}
}
