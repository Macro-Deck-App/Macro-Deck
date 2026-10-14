using MacroDeck.Sdk.Batteries;

namespace MacroDeckHost.Tests.UnitTests.Adb;

internal sealed class RecordingBatteryProviderContext : IBatteryProviderContext
{
	private readonly Lock _sync = new();
	private readonly List<IReadOnlyList<BatteryDevice>> _publishes = [];
	private readonly List<string> _removed = [];

	public IReadOnlyList<IReadOnlyList<BatteryDevice>> Publishes
	{
		get
		{
			lock (_sync)
			{
				return [.. _publishes];
			}
		}
	}

	public IReadOnlyList<string> Removed
	{
		get
		{
			lock (_sync)
			{
				return [.. _removed];
			}
		}
	}

	public BatteryDevice? Latest(string id)
	{
		return Publishes.SelectMany(batch => batch).LastOrDefault(device => device.Id == id);
	}

	public Task PublishAsync(BatteryDevice device, CancellationToken cancellationToken = default)
	{
		return PublishAsync([device], cancellationToken);
	}

	public Task PublishAsync(IReadOnlyList<BatteryDevice> devices, CancellationToken cancellationToken = default)
	{
		lock (_sync)
		{
			_publishes.Add([.. devices]);
		}

		return Task.CompletedTask;
	}

	public Task RemoveAsync(string deviceId, CancellationToken cancellationToken = default)
	{
		lock (_sync)
		{
			_removed.Add(deviceId);
		}

		return Task.CompletedTask;
	}
}
