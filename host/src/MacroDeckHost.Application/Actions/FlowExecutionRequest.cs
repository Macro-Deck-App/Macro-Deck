using MacroDeckHost.Application.Deck;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Actions;

public sealed class FlowExecutionRequest
{
	private readonly string? _clientOrigin;

	public Guid ExecutionId { get; init; } = Guid.NewGuid();

	public string? FlowsSource { get; init; }

	public required TriggerSelector Trigger { get; init; }

	public VariableScope Scope { get; init; } = VariableScope.Global;

	public string? ScopeRefId { get; init; }

	/// <summary>
	/// The origin every action in this run sees, and what a deck navigation is routed back to. This is
	/// the one place a <see cref="DeviceOrigin" /> can enter the pipeline: a value assigned here that
	/// already looks like one is dropped, because it can only have come from a caller-supplied client id
	/// (an HTTP body, a WebSocket message, a plugin callback) and honouring it would let that caller
	/// drive a device session it does not own. Only <see cref="OriginDeviceId" /> mints one.
	/// </summary>
	public string? OriginClientId
	{
		get => OriginDeviceId is { } deviceId ? DeviceOrigin.For(deviceId) : _clientOrigin;
		init => _clientOrigin = DeviceOrigin.TryParse(value, out _) ? null : value;
	}

	/// <summary>
	/// Set only by the host itself, for a device session acting on its own behalf. No transport payload
	/// carries it, which is what keeps <see cref="OriginClientId" />'s device form unforgeable.
	/// </summary>
	public Guid? OriginDeviceId { get; init; }

	public Guid? OwnerWidgetId { get; init; }

	public ExecutionOrigin Origin { get; init; } = ExecutionOrigin.Client;

	public IReadOnlyDictionary<string, object?>? EventParameters { get; init; }

	public IReadOnlyDictionary<string, object?>? ScriptInputs { get; init; }
}
