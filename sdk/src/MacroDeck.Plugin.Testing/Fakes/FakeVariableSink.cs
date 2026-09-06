using MacroDeck.Sdk.Variables;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>
/// In-memory <see cref="IVariableSink" />, standing in for the host's push endpoint. Records every batch
/// a provider publishes and every catalogue invalidation, with no transport and no host on the other end
/// - the same role <see cref="FakeDeviceProviderContext" /> plays for <c>IDeviceProviderContext</c>.
/// </summary>
public sealed class FakeVariableSink : IVariableSink
{
	private readonly List<IReadOnlyList<VariableValue>> _batches = [];
	private readonly Lock _sync = new();
	private int _invalidations;
	private HashSet<string> _subscribed = new(StringComparer.Ordinal);

	/// <summary>Every batch published so far, in order. Each entry is exactly one <see cref="PublishAsync" /> call.</summary>
	public IReadOnlyList<IReadOnlyList<VariableValue>> Batches
	{
		get
		{
			lock (_sync)
			{
				return [.. _batches];
			}
		}
	}

	/// <summary>Every published value across every batch, flattened, in order.</summary>
	public IReadOnlyList<VariableValue> Values => [.. Batches.SelectMany(batch => batch)];

	/// <summary>How many times <see cref="InvalidateCatalogAsync" /> was called.</summary>
	public int Invalidations
	{
		get
		{
			lock (_sync)
			{
				return _invalidations;
			}
		}
	}

	public Task PublishAsync(
		IReadOnlyCollection<VariableValue> values,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(values);

		lock (_sync)
		{
			// Mirrors both real sinks (the in-process host sink and the remote plugin sink): a value for
			// an id outside the working set the host most recently subscribed to is dropped silently, not
			// recorded. A plugin test that never subscribes therefore never sees a batch here, the same
			// way it would never reach the host in production - a test built against this fake without
			// subscribing first would otherwise pass while the identical behaviour in production drops
			// the push.
			var subscribed = values.Where(value => _subscribed.Contains(value.Id)).ToList();
			if (subscribed.Count > 0)
			{
				_batches.Add(subscribed);
			}
		}

		return Task.CompletedTask;
	}

	/// <summary>Sets the working set this fake treats as subscribed, replacing whatever was set before.
	/// Called by <see cref="PluginTestHarness" /> whenever a test invokes the <c>variables</c>
	/// <c>subscribe</c> operation, so this fake's drop behaviour matches what the real capability handler
	/// just told the provider to watch.</summary>
	public void SetSubscribed(IReadOnlyCollection<string> ids)
	{
		ArgumentNullException.ThrowIfNull(ids);

		lock (_sync)
		{
			_subscribed = new HashSet<string>(ids, StringComparer.Ordinal);
		}
	}

	public Task InvalidateCatalogAsync(CancellationToken cancellationToken = default)
	{
		lock (_sync)
		{
			_invalidations++;
		}

		return Task.CompletedTask;
	}
}
