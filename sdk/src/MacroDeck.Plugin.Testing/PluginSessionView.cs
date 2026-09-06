using System.Diagnostics;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Plugin.Testing.Internal;

namespace MacroDeck.Plugin.Testing;

/// <summary>
/// One connected, handshake-completed plugin session on a <see cref="MacroDeckTestHost" />: what the
/// plugin declared, what the host accepted, and the ability to invoke a capability and see exactly what
/// came back.
/// </summary>
public sealed class PluginSessionView : ICapabilityInvoker
{
	private readonly PluginConnection _connection;

	internal PluginSessionView(PluginConnection connection)
	{
		_connection = connection;
		Actions = new ActionsTestClient(this);
		Variables = new VariablesTestClient(this);
		Events = new EventsTestClient(this);
		Icons = new IconsTestClient(this);
		ConfigFlow = new ConfigFlowTestClient(this);
		MusicPlayer = new MusicPlayerTestClient(this);
		Weather = new WeatherTestClient(this);
		VirtualProfiles = new VirtualProfilesTestClient(this);
		DeviceProvider = new DeviceProviderTestClient(this);
		LayoutProvider = new LayoutProviderTestClient(this);
		FolderViewProvider = new FolderViewProviderTestClient(this);
		WidgetTypeProvider = new WidgetTypeProviderTestClient(this);
		Issues = new IssuesTestClient(this);
	}

	/// <summary>The session id the host issued, stable across a resume.</summary>
	public string SessionId => _connection.SessionId;

	/// <summary>The protocol version this session negotiated.</summary>
	public int NegotiatedVersion => _connection.NegotiatedVersion;

	/// <summary>True when this connection resumed a prior session rather than starting a new one.</summary>
	public bool Resumed => _connection.Resumed;

	/// <summary>Every capability the plugin has declared, as of the most recent handshake or re-declaration.</summary>
	public IReadOnlyList<DeclaredCapability> Declared => _connection.Declared;

	/// <summary>What the host accepted of <see cref="Declared" />, one result per declared capability.</summary>
	public IReadOnlyList<CapabilityNegotiationResult> Accepted => _connection.Accepted;

	/// <summary>The <c>actions</c> capability.</summary>
	public ActionsTestClient Actions { get; }

	/// <summary>The <c>variables</c> capability.</summary>
	public VariablesTestClient Variables { get; }

	/// <summary>The <c>events</c> capability.</summary>
	public EventsTestClient Events { get; }

	/// <summary>The <c>icons</c> capability.</summary>
	public IconsTestClient Icons { get; }

	/// <summary>The <c>config-flow</c> capability.</summary>
	public ConfigFlowTestClient ConfigFlow { get; }

	/// <summary>The <c>music-player</c> capability.</summary>
	public MusicPlayerTestClient MusicPlayer { get; }

	/// <summary>The <c>weather</c> capability.</summary>
	public WeatherTestClient Weather { get; }

	/// <summary>The <c>virtual-profiles</c> capability.</summary>
	public VirtualProfilesTestClient VirtualProfiles { get; }

	/// <summary>The <c>device-provider</c> capability.</summary>
	public DeviceProviderTestClient DeviceProvider { get; }

	/// <summary>The <c>layout-provider</c> capability.</summary>
	public LayoutProviderTestClient LayoutProvider { get; }

	/// <summary>The <c>folder-view-provider</c> capability.</summary>
	public FolderViewProviderTestClient FolderViewProvider { get; }

	/// <summary>The <c>widget-type-provider</c> capability.</summary>
	public WidgetTypeProviderTestClient WidgetTypeProvider { get; }

	/// <summary>The <c>issues</c> capability.</summary>
	public IssuesTestClient Issues { get; }

	/// <summary>
	/// Sends a <c>capability.invoke</c> and returns exactly what the plugin replied with - a real
	/// invocation, dispatched by the plugin's own <c>CapabilityDispatcher</c>, never a value this
	/// package synthesises locally.
	/// </summary>
	/// <param name="kind">One of <see cref="CapabilityKinds" />.</param>
	/// <param name="localId">The declared local id being invoked, unqualified.</param>
	/// <param name="operation">What to do with it. Defined per capability kind by <see cref="CapabilityOperations" />.</param>
	/// <param name="arguments">Operation arguments, serialized through <c>PluginProtocolJson.Options</c>. Null when there are none.</param>
	/// <param name="options">Deadline, idempotency key and cancellation for this call.</param>
	/// <exception cref="ArgumentException">
	/// <paramref name="options" />'s idempotency key exceeds <see cref="ProtocolLimits.MaxIdempotencyKeyLength" />.
	/// </exception>
	public async Task<CapabilityInvocationOutcome> InvokeAsync(
		string kind,
		string localId,
		string operation,
		object? arguments = null,
		CapabilityInvokeOptions? options = null)
	{
		ArgumentException.ThrowIfNullOrEmpty(kind);
		ArgumentException.ThrowIfNullOrEmpty(localId);
		ArgumentException.ThrowIfNullOrEmpty(operation);

		if (options?.IdempotencyKey is { Length: > ProtocolLimits.MaxIdempotencyKeyLength })
		{
			throw new ArgumentException(
				$"The idempotency key exceeds {ProtocolLimits.MaxIdempotencyKeyLength} characters.",
				nameof(options));
		}

		var envelope = new ProtocolEnvelope
		{
			Type = MessageTypes.CapabilityInvoke,
			Id = Guid.CreateVersion7().ToString(),
			DeadlineMs = DeadlineMsOf(options),
			IdempotencyKey = options?.IdempotencyKey,
			Payload = JsonSerializer.SerializeToElement(new CapabilityInvokePayload
				{
					Kind = kind,
					LocalId = localId,
					Operation = operation,
					Arguments = arguments is null
						? null
						: JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options)
				},
				PluginProtocolJson.Options)
		};

		var stopwatch = Stopwatch.StartNew();
		var reply = await _connection.InvokeAndWaitAsync(envelope, options?.CancellationToken ?? default)
			.ConfigureAwait(false);
		stopwatch.Stop();

		if (reply.Error is { } error)
		{
			return CapabilityInvocationOutcome.Failure(envelope.Id, error, stopwatch.Elapsed);
		}

		var data = reply.Payload?.Deserialize<CapabilityResultPayload>(PluginProtocolJson.Options)?.Data;
		return CapabilityInvocationOutcome.Success(envelope.Id, data, stopwatch.Elapsed);
	}

	/// <summary>
	/// Sends <c>capability.cancel</c> for a <c>capability.invoke</c> this session sent earlier.
	/// Best-effort by contract: withdrawing an unknown or already-answered correlation is a no-op, not
	/// an error - see <c>CancellationRules</c>.
	/// </summary>
	public Task CancelAsync(string correlationId, string? reason = null)
	{
		ArgumentException.ThrowIfNullOrEmpty(correlationId);

		return _connection.SendAsync(new ProtocolEnvelope
		{
			Type = MessageTypes.CapabilityCancel,
			Id = Guid.CreateVersion7().ToString(),
			CorrelationId = correlationId,
			Payload = reason is null
				? null
				: JsonSerializer.SerializeToElement(new CapabilityCancelPayload { Reason = reason },
					PluginProtocolJson.Options)
		});
	}

	private static int? DeadlineMsOf(CapabilityInvokeOptions? options)
	{
		if (options?.Deadline is { } deadline)
		{
			return (int)Math.Max(0, (deadline - DateTimeOffset.UtcNow).TotalMilliseconds);
		}

		return options?.Timeout is { } timeout ? (int)timeout.TotalMilliseconds : null;
	}
}
