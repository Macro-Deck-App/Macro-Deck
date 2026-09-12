using System.Text.Json;
using MacroDeckHost.Application.Ui.Sessions;

namespace MacroDeckHost.Application.Devices.Surfaces;

public sealed class DevicePressClaim : IAsyncDisposable
{
	private readonly Lock _gate = new();
	private readonly IUiSessionBroker? _broker;
	private readonly string? _sessionId;
	private readonly JsonDocument? _tree;
	private readonly JsonElement? _claimant;
	private bool _disposed;

	private DevicePressClaim(
		IUiSessionBroker? broker,
		string? sessionId,
		JsonDocument? tree,
		JsonElement? claimant,
		bool absorbed)
	{
		_broker = broker;
		_sessionId = sessionId;
		_tree = tree;
		_claimant = claimant;
		Absorbed = absorbed;
	}

	public static DevicePressClaim None => new(null, null, null, null, absorbed: false);

	public bool Absorbed { get; }

	public bool TakesPress => Absorbed || _claimant is not null;

	public static DevicePressClaim Absorbing(IUiSessionBroker? broker, string? sessionId)
		=> new(broker, sessionId, null, null, absorbed: true);

	public static DevicePressClaim From(IUiSessionBroker broker, string sessionId, JsonDocument tree)
	{
		var claim = UiActivationClaim.Of(tree.RootElement);
		if (claim.Claimant is { } node &&
			(!node.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String))
		{
			tree.Dispose();
			return Absorbing(broker, sessionId);
		}

		return new DevicePressClaim(broker, sessionId, tree, claim.Claimant, claim.Absorbed);
	}

	public void Dispatch(string triggerType)
	{
		lock (_gate)
		{
			DispatchLocked(triggerType);
		}
	}

	private void DispatchLocked(string triggerType)
	{
		if (_disposed || _claimant is not { } node || _tree is null || _broker is null || _sessionId is null ||
			UiActivationClaim.EventFor(node, triggerType) is not { } uiEvent)
		{
			return;
		}

		_broker.DispatchHostEvent(_sessionId,
			new UiSessionEventCommand
			{
				NodeId = node.GetProperty("id").GetString()!,
				Name = uiEvent.Name,
				Data = uiEvent.Payload is null
					? default
					: new UiRawJson(JsonSerializer.SerializeToUtf8Bytes(uiEvent.Payload)),
				Revision = _tree.RootElement.TryGetProperty("revision", out var revision) &&
					revision.TryGetInt32(out var value)
						? value
						: null
			});
	}

	public async ValueTask DisposeAsync()
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			_tree?.Dispose();
		}

		if (_broker is not null && _sessionId is not null)
		{
			// Queued behind any dispatch on the provider pump, so the last event still reaches the session.
			await _broker.CloseAsync(_sessionId, "The device press ended.", CancellationToken.None);
		}
	}
}
