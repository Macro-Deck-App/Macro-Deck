using MacroDeckHost.Integrations;
using MacroDeckHost.Integrations.Voicemeeter;
using MacroDeckHost.Tests.UnitTests.System;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Voicemeeter;

[TestFixture]
internal sealed class VoicemeeterIntegrationTests
{
	private static readonly string[] _bananaBusSends = ["A1", "A2", "A3", "B1", "B2"];

	private FakeVoicemeeterRemote _remote = null!;
	private VoicemeeterIntegration _integration = null!;

	[SetUp]
	public void SetUp()
	{
		_remote = new FakeVoicemeeterRemote();
		_integration = new VoicemeeterIntegration(() => _remote);
	}

	[TearDown]
	public void TearDown()
	{
		_integration.Dispose();
		_remote.Dispose();
	}

	[Test]
	public void The_integration_is_identified()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_integration.Id, Is.EqualTo("app.macro-deck.voicemeeter"));
			Assert.That(TestLocalization.Resolve(_integration.Name), Is.EqualTo("Voicemeeter"));
			Assert.That(
				typeof(VoicemeeterIntegration).GetCustomAttributes(typeof(MacroDeckIntegrationAttribute), false),
				Is.Not.Empty);
		});
	}

	[Test]
	[Platform("Win")]
	public void The_host_discovery_finds_the_integration_on_Windows()
	{
		var discovered = IntegrationDiscovery.DiscoverIntegrations(Serilog.Log.Logger);

		var voicemeeter = discovered.OfType<VoicemeeterIntegration>().SingleOrDefault();
		Assert.That(voicemeeter, Is.Not.Null, "the Voicemeeter integration was not discovered");
		Assert.Multiple(() =>
		{
			Assert.That(voicemeeter!.Actions, Is.Not.Empty);
			Assert.That(voicemeeter.EventDefinitions, Is.Not.Empty);
		});
	}

	[Test]
	[Platform(Exclude = "Win")]
	public void The_host_discovery_skips_the_integration_off_Windows()
	{
		var discovered = IntegrationDiscovery.DiscoverIntegrations(Serilog.Log.Logger);

		Assert.That(discovered.OfType<VoicemeeterIntegration>(), Is.Empty);
	}

	[Test]
	public void Every_action_parameter_uses_a_defined_parameter_type()
	{
		Assert.Multiple(() =>
		{
			foreach (var action in _integration.Actions)
			{
				foreach (var parameter in action.Parameters)
				{
					Assert.That(Enum.IsDefined(parameter.Type), Is.True, $"{action.Id}.{parameter.Name}");
				}
			}
		});
	}

	[Test]
	public void Action_and_event_ids_are_unique()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_integration.Actions.Select(action => action.Id), Is.Unique);
			Assert.That(_integration.EventDefinitions.Select(definition => definition.Id), Is.Unique);
			Assert.That(VoicemeeterVariables.All.Select(variable => variable.Name), Is.Unique);
		});
	}

	[Test]
	public void Every_event_filter_matches_a_payload_parameter()
	{
		Assert.Multiple(() =>
		{
			foreach (var definition in _integration.EventDefinitions)
			{
				var payload = definition.PayloadParameters.Select(parameter => parameter.Name).ToList();
				foreach (var configuration in definition.ConfigurationParameters)
				{
					Assert.That(payload,
						Does.Contain(configuration.Name),
						$"{definition.Id}.{configuration.Name} filters nothing");
				}
			}
		});
	}

	[Test]
	public void Every_variable_declares_a_refresh_interval()
	{
		Assert.Multiple(() =>
		{
			foreach (var variable in VoicemeeterVariables.All)
			{
				Assert.That(variable.Name, Does.StartWith("voicemeeter_"));
				Assert.That(variable.RefreshInterval, Is.Not.Null, variable.Name);
			}
		});
	}

	[Test]
	public void The_variables_cover_every_channel_of_the_widest_edition()
	{
		var names = VoicemeeterVariables.All.Select(variable => variable.Name).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(names, Does.Contain("voicemeeter_strip7_gain"));
			Assert.That(names, Does.Contain("voicemeeter_bus7_muted"));
			Assert.That(names, Does.Contain("voicemeeter_strip0_name"));
			Assert.That(names, Does.Not.Contain("voicemeeter_strip8_gain"));
		});
	}

	[Test]
	public async Task No_variables_are_declared_when_the_remote_api_is_unavailable()
	{
		_remote.IsAvailable = false;
		await _integration.InitializeAsync(new FakeContext());

		Assert.That(_integration.Variables, Is.Empty);
	}

	[Test]
	public async Task DeclaredVariables_is_the_full_set_even_when_the_remote_api_is_unavailable()
	{
		_remote.IsAvailable = false;
		await _integration.InitializeAsync(new FakeContext());

		Assert.Multiple(() =>
		{
			Assert.That(_integration.DeclaredVariables, Is.EqualTo(VoicemeeterVariables.All));
			Assert.That(_integration.Variables, Is.Empty, "provided still stays empty");
		});
	}

	[Test]
	public async Task Every_channel_is_declared_once_the_remote_api_is_there()
	{
		await _integration.InitializeAsync(new FakeContext());

		Assert.That(_integration.Variables, Is.EqualTo(VoicemeeterVariables.All));
	}

	[Test]
	public async Task Everything_but_the_connection_flag_is_unavailable_while_disconnected()
	{
		await _integration.InitializeAsync(new FakeContext());

		Assert.Multiple(async () =>
		{
			Assert.That((await _integration.ReadAsync(VoicemeeterVariables.Connected, CancellationToken.None)).Value,
				Is.False);

			foreach (var variable in VoicemeeterVariables.All
				.Where(candidate => candidate.Name != VoicemeeterVariables.Connected))
			{
				Assert.That((await _integration.ReadAsync(variable.ResolvedId!, CancellationToken.None)).Value,
					Is.Null,
					variable.Name);
			}
		});
	}

	[Test]
	public async Task A_connected_Voicemeeter_answers_with_its_channels()
	{
		_remote.Run(VoicemeeterEdition.Banana);
		_remote.UserSets("Strip[0].Label", "Mic");
		_remote.UserSets("Strip[0].Gain", -4.3f);
		_remote.UserSets("Bus[1].Mute", 1f);
		await Connected();

		Assert.Multiple(async () =>
		{
			Assert.That((await _integration.ReadAsync(VoicemeeterVariables.Connected, CancellationToken.None)).Value,
				Is.True);
			Assert.That((await _integration.ReadAsync(VoicemeeterVariables.Edition, CancellationToken.None)).Value,
				Is.EqualTo("Voicemeeter Banana"));
			Assert.That((await _integration.ReadAsync("voicemeeter-strip0-name", CancellationToken.None)).Value,
				Is.EqualTo("Mic"));
			var gain = await _integration.ReadAsync("voicemeeter-strip0-gain", CancellationToken.None);
			Assert.That(gain.Value, Is.EqualTo(-4.3d).Within(0.001d));

			// The dB range travels with the reading, so whatever writes the gain gets the same bounds the
			// retired slider action used to report separately.
			Assert.That(gain.Min, Is.EqualTo(VoicemeeterVariables.MinimumGain));
			Assert.That(gain.Max, Is.EqualTo(VoicemeeterVariables.MaximumGain));
			Assert.That((await _integration.ReadAsync("voicemeeter-bus1-muted", CancellationToken.None)).Value,
				Is.True);
		});
	}

	[Test]
	public async Task Channels_a_smaller_edition_lacks_read_as_unavailable()
	{
		_remote.Run(VoicemeeterEdition.Banana);
		await Connected();

		Assert.Multiple(async () =>
		{
			Assert.That((await _integration.ReadAsync("voicemeeter-strip4-gain", CancellationToken.None)).Value,
				Is.Not.Null);
			Assert.That((await _integration.ReadAsync("voicemeeter-strip5-gain", CancellationToken.None)).Value,
				Is.Null);
			Assert.That((await _integration.ReadAsync("voicemeeter-bus7-muted", CancellationToken.None)).Value,
				Is.Null);
		});
	}

	[Test]
	public async Task An_unknown_variable_name_is_unavailable()
	{
		_remote.Run(VoicemeeterEdition.Potato);
		await Connected();

		Assert.Multiple(async () =>
		{
			Assert.That((await _integration.ReadAsync("voicemeeter-nonsense", CancellationToken.None)).Value, Is.Null);
			Assert.That((await _integration.ReadAsync("voicemeeter-strip0-nonsense", CancellationToken.None)).Value,
				Is.Null);
			Assert.That((await _integration.ReadAsync("something-else", CancellationToken.None)).Value, Is.Null);
		});
	}

	[Test]
	public async Task A_closed_Voicemeeter_is_not_reported_as_a_problem()
	{
		await _integration.InitializeAsync(new FakeContext());

		Assert.That(await _integration.GetIssuesAsync(), Is.Empty);
	}

	[Test]
	public async Task An_unreachable_remote_api_is_reported_as_an_issue()
	{
		_remote.IsAvailable = false;
		_remote.UnavailableReason = "Voicemeeter is a Windows application.";
		await _integration.InitializeAsync(new FakeContext());

		var issues = await _integration.GetIssuesAsync();

		Assert.Multiple(() =>
		{
			Assert.That(issues, Has.Count.EqualTo(1));
			Assert.That(issues[0].Id, Is.EqualTo(VoicemeeterIntegration.UnavailableIssueId));
			Assert.That(TestLocalization.Resolve(issues[0].Description),
				Is.EqualTo("Voicemeeter is a Windows application."));
		});
	}

	[Test]
	public async Task An_unreachable_remote_api_still_leaves_a_usable_integration()
	{
		_remote.IsAvailable = false;
		await _integration.InitializeAsync(new FakeContext());

		Assert.Multiple(async () =>
		{
			Assert.That(_integration.IsInitialized, Is.True);
			Assert.That(_integration.Actions, Is.Not.Empty);
			Assert.That((await _integration.ReadAsync(VoicemeeterVariables.Connected, CancellationToken.None)).Value,
				Is.False);
		});
	}

	[Test]
	public async Task The_routing_event_filter_offers_bus_sends_rather_than_bus_indexes()
	{
		_remote.Run(VoicemeeterEdition.Banana);
		await Connected();

		var options = await _integration.GetEventOptionsAsync(new EventOptionsContext
			{
				EventId = VoicemeeterEventIds.StripRoutingChanged,
				ParameterName = "bus"
			},
			CancellationToken.None);

		Assert.That(options.Options.Select(option => option.Value), Is.EqualTo(_bananaBusSends));
	}

	[Test]
	public async Task The_mute_event_filter_offers_channels_by_name()
	{
		_remote.Run(VoicemeeterEdition.Banana);
		_remote.UserSets("Bus[0].Label", "Speakers");
		await Connected();

		var options = await _integration.GetEventOptionsAsync(new EventOptionsContext
			{
				EventId = VoicemeeterEventIds.BusMuteChanged,
				ParameterName = "bus"
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(TestLocalization.Resolve(options.Options[0].Label), Is.EqualTo("Speakers"));
			Assert.That(options.Options[0].Value, Is.EqualTo("0"));
		});
	}

	[Test]
	public async Task An_unrelated_event_parameter_offers_nothing_rather_than_guessing()
	{
		await _integration.InitializeAsync(new FakeContext());

		var options = await _integration.GetEventOptionsAsync(new EventOptionsContext
			{
				EventId = VoicemeeterEventIds.MacroButtonChanged,
				ParameterName = "button"
			},
			CancellationToken.None);

		Assert.That(options.Options, Is.Empty);
	}

	[Test]
	public async Task Shutting_down_logs_out_and_releases_the_library()
	{
		_remote.Run(VoicemeeterEdition.Banana);
		await _integration.InitializeAsync(new FakeContext());

		await _integration.ShutdownAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_remote.LogoutCount, Is.EqualTo(1));
			Assert.That(_remote.IsDisposed, Is.True);
		});
	}

	[Test]
	public void The_integration_ships_an_icon()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_integration.IconMimeType, Is.EqualTo("image/svg+xml"));
			Assert.That(_integration.GetIcon(), Is.Not.Empty);
		});
	}

	private async Task Connected()
	{
		await _integration.InitializeAsync(new FakeContext());
		_integration.Connection!.Poll();
	}

	private sealed class FakeContext : IIntegrationContext
	{
		public IVariableApi Variables { get; } = new RecordingVariableApi();

		public IUserVariableApi UserVariables => throw new NotSupportedException();

		public IIntegrationConfig Config => throw new NotSupportedException();

		public IDeckNavigator Deck => throw new NotSupportedException();

		public IScriptApi Scripts => throw new NotSupportedException();

		public IWidgetApi Widgets => throw new NotSupportedException();

		public IEventPublisher Events { get; } = new SilentPublisher();

		public IUserNotifier Notifications => throw new NotSupportedException();

		private sealed class SilentPublisher : IEventPublisher
		{
			public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
			{
			}
		}
	}
}
