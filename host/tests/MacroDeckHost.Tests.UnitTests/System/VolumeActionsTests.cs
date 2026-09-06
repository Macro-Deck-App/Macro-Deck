using MacroDeckHost.Integrations.System.Actions;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.System;

public class VolumeActionsTests
{
	private static ActionExecutionContext Context(Dictionary<string, object> parameters)
		=> new() { Parameters = parameters };

	[Test]
	public async Task IncreaseVolume_adds_amount_and_clamps_to_one()
	{
		var volume = new FakeVolumeService { Volume = 0.95f };
		var action = new IncreaseVolumeActionDefinition(volume);

		await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object> { ["amount"] = 10 }));

		Assert.That(volume.Volume, Is.EqualTo(1f));
	}

	[Test]
	public async Task DecreaseVolume_subtracts_amount_and_clamps_to_zero()
	{
		var volume = new FakeVolumeService { Volume = 0.05f };
		var action = new DecreaseVolumeActionDefinition(volume);

		await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object> { ["amount"] = 10 }));

		Assert.That(volume.Volume, Is.EqualTo(0f));
	}

	[Test]
	public async Task SetVolume_maps_percentage_to_scalar()
	{
		var volume = new FakeVolumeService();
		var action = new SetVolumeActionDefinition(volume);

		await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object> { ["level"] = 30 }));

		Assert.That(volume.Volume, Is.EqualTo(0.3f).Within(0.0001));
	}

	[Test]
	public async Task MuteVolume_toggles_state()
	{
		var volume = new FakeVolumeService { Muted = false };
		var action = new MuteVolumeActionDefinition(volume);

		await action.CreateExecutor().ExecuteAsync(Context([]));
		Assert.That(volume.Muted, Is.True);

		await action.CreateExecutor().ExecuteAsync(Context([]));
		Assert.That(volume.Muted, Is.False);
	}

	[Test]
	public async Task MuteVolume_reports_the_state_it_set()
	{
		var volume = new FakeVolumeService { Muted = false };
		var result = await new MuteVolumeActionDefinition(volume).CreateExecutor().ExecuteAsync(Context([]));

		Assert.That(result.ExpectedStateId, Is.EqualTo("muted"));
	}

	[Test]
	public async Task IncreaseVolume_is_noop_when_volume_currently_unavailable()
	{
		var volume = new FakeVolumeService { Volume = null };
		var action = new IncreaseVolumeActionDefinition(volume);

		await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object> { ["amount"] = 10 }));

		Assert.That(volume.Volume, Is.Null);
	}

	[Test]
	public async Task DecreaseVolume_is_noop_when_volume_currently_unavailable()
	{
		var volume = new FakeVolumeService { Volume = null };
		var action = new DecreaseVolumeActionDefinition(volume);

		await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object> { ["amount"] = 10 }));

		Assert.That(volume.Volume, Is.Null);
	}

	[Test]
	public async Task MuteVolume_is_noop_when_mute_state_currently_unavailable()
	{
		var volume = new FakeVolumeService { Muted = null };
		var action = new MuteVolumeActionDefinition(volume);

		await action.CreateExecutor().ExecuteAsync(Context([]));

		Assert.That(volume.Muted, Is.Null);
	}
}
