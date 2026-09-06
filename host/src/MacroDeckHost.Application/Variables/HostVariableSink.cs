using MacroDeck.Sdk.Variables;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Variables;

// The host-side IVariableSink handed to an in-process catalog provider when it attaches. One instance per
// integration.
//
// Drops ids that are not currently subscribed for this integration before writing to the update channel:
// the two ends resubscribe asynchronously, so a value for an id the host just unbound is a race, not an
// error the provider should hear about.
public sealed class HostVariableSink : IVariableSink
{
	private readonly string _integrationId;
	private readonly VariableUpdateChannel _channel;
	private readonly IVariableSubscriptionCoordinator _subscriptions;
	private readonly VariableCatalogInvalidationSignal _invalidation;

	public HostVariableSink(
		string integrationId,
		VariableUpdateChannel channel,
		IVariableSubscriptionCoordinator subscriptions,
		VariableCatalogInvalidationSignal invalidation)
	{
		_integrationId = integrationId;
		_channel = channel;
		_subscriptions = subscriptions;
		_invalidation = invalidation;
	}

	public Task PublishAsync(IReadOnlyCollection<VariableValue> values, CancellationToken cancellationToken = default)
	{
		foreach (var value in values)
		{
			if (_subscriptions.IsSubscribed(_integrationId, value.Id))
			{
				_channel.Write(_integrationId, value.Id, value.Reading.Value, BoundsOf(value.Reading));
			}
		}

		return Task.CompletedTask;
	}

	public Task InvalidateCatalogAsync(CancellationToken cancellationToken = default)
	{
		_invalidation.RaiseInvalidated(_integrationId);
		return Task.CompletedTask;
	}

	internal static VariableBounds BoundsOf(VariableReading reading)
		=> new(reading.Min, reading.Max, reading.Step);
}
