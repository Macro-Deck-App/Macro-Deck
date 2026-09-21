using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Actions;

[TestFixture]
public class ActionProviderProbeTests
{
	private static readonly Dictionary<string, object?> _noParameters = new();

	[Test]
	public void A_plugin_action_that_declares_it_provides_an_icon_is_an_icon_provider()
	{
		var registry = new FakeIntegrationRegistry();
		registry.Add(RemoteIconProviderFixture.Integration());

		var probe = Probe(registry);

		Assert.Multiple(() =>
		{
			Assert.That(probe.IsIconProvider(RemoteIconProviderFixture.PluginId, RemoteIconProviderFixture.ActionId),
				Is.True);
			Assert.That(probe.IsStateProvider(RemoteIconProviderFixture.PluginId, RemoteIconProviderFixture.ActionId),
				Is.False);
		});
	}

	[Test]
	public void An_action_of_a_disabled_integration_provides_nothing()
	{
		var registry = new FakeIntegrationRegistry();
		registry.Add(new FakeIntegration
		{
			Id = "test.states", Actions = [new ControllableStateProviderAction(), new ControllableIconProviderAction()]
		});
		registry.SetEnabled("test.states", false);

		var probe = Probe(registry);

		Assert.Multiple(() =>
		{
			Assert.That(probe.IsStateProvider("test.states", "mute"), Is.False);
			Assert.That(probe.IsIconProvider("test.states", "cover"), Is.False);
		});
	}

	[Test]
	public async Task A_provider_that_does_not_answer_in_time_is_reported_as_a_timeout()
	{
		var time = new FakeTimeProvider();
		var action = new ControllableStateProviderAction
		{
			Answer = async (_, token) =>
			{
				await Task.Delay(Timeout.InfiniteTimeSpan, token);

				return null;
			},
		};
		var probe = Probe(Registry(action), time);

		var pending = probe.ProbeStatesAsync("test.states", "mute", _noParameters, CancellationToken.None);
		time.Advance(TimeSpan.FromSeconds(11));
		var result = await pending;

		Assert.That(result.Outcome, Is.EqualTo(ActionProbeOutcome.Timeout));
	}

	[Test]
	public async Task A_provider_that_throws_is_reported_as_failed_instead_of_escaping()
	{
		var action = new ControllableStateProviderAction
		{
			Answer = (_, _) => throw new InvalidOperationException("device went away"),
		};

		var result = await Probe(Registry(action))
			.ProbeStatesAsync("test.states", "mute", _noParameters, CancellationToken.None);

		Assert.That(result.Outcome, Is.EqualTo(ActionProbeOutcome.Failed));
	}

	[Test]
	public async Task A_state_set_whose_ids_could_not_survive_a_save_is_not_usable()
	{
		var action = new ControllableStateProviderAction
		{
			Answer = (_, _) => Task.FromResult<ActionStateSnapshot?>(
				ControllableStateProviderAction.Snapshot(new ActionStateDefinition("not a valid id!", "Broken"))),
		};

		var result = await Probe(Registry(action))
			.ProbeStatesAsync("test.states", "mute", _noParameters, CancellationToken.None);

		Assert.That(result.Outcome, Is.EqualTo(ActionProbeOutcome.Failed));
	}

	private static FakeIntegrationRegistry Registry(ControllableStateProviderAction action)
	{
		var registry = new FakeIntegrationRegistry();
		registry.Add(new FakeIntegration { Id = "test.states", Actions = [action] });

		return registry;
	}

	private static ActionProviderProbe Probe(FakeIntegrationRegistry registry, TimeProvider? time = null)
		=> new(registry,
			new RemoteIconProviderActionRegistry(null!, null!, null!),
			time ?? TimeProvider.System,
			Serilog.Core.Logger.None);
}
