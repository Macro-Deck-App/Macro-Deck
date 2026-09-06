using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.MusicPlayer;
using Serilog;
using MacroDeck.Localization;

namespace MacroDeckHost.Tests.UnitTests.MusicPlayer;

[TestFixture]
internal sealed class MusicPlayerRegistryTests
{
	private static readonly string[] _multiInstanceIds = ["music.multi::a", "music.multi::b"];

	private static readonly string[] _onlyGoodInstanceId = ["music.multi::good"];

	[Test]
	public void A_single_configuration_provider_announces_the_stable_default_id()
	{
		var registry = Registry(new SingleConfigMusicIntegration("guid-1"));

		var instances = registry.GetInstances();

		Assert.That(instances.Single().InstanceId, Is.EqualTo("music.single::default"));
	}

	[Test]
	public void A_provider_that_states_a_name_keeps_it_and_one_that_does_not_is_named_by_its_integration()
	{
		var stated = Registry(new SingleConfigMusicIntegration("guid-1")).GetInstances().Single();
		var inherited = Registry(new MultiConfigMusicIntegration("a")).GetInstances().Single();

		Assert.Multiple(() =>
		{
			Assert.That(TestLocalization.Resolve(stated.ProviderName), Is.EqualTo("Single"));
			Assert.That(TestLocalization.Resolve(inherited.ProviderName), Is.EqualTo("Multi"));
		});
	}

	[Test]
	public void The_default_id_resolves_to_the_providers_first_instance()
	{
		var integration = new SingleConfigMusicIntegration("guid-1");
		var registry = Registry(integration);

		Assert.That(registry.GetPlayer("music.single::default"), Is.SameAs(integration.Player));
	}

	[Test]
	public void A_legacy_guid_id_keeps_resolving()
	{
		var integration = new SingleConfigMusicIntegration("guid-1");
		var registry = Registry(integration);

		Assert.That(registry.GetPlayer("music.single::guid-1"), Is.SameAs(integration.Player));
	}

	[Test]
	public void A_real_guid_shaped_id_and_the_default_alias_both_resolve_to_the_same_player()
	{
		const string entryGuid = "0f8fad5b-d9cb-469f-a165-70867728950e";
		var integration = new SingleConfigMusicIntegration(entryGuid);
		var registry = Registry(integration);

		Assert.Multiple(() =>
		{
			Assert.That(registry.GetPlayer($"music.single::{entryGuid}"), Is.SameAs(integration.Player));
			Assert.That(registry.GetPlayer("music.single::default"), Is.SameAs(integration.Player));
		});
	}

	[Test]
	public void A_multi_configuration_provider_keeps_its_per_instance_ids()
	{
		var registry = Registry(new MultiConfigMusicIntegration("a", "b"));

		Assert.That(registry.GetInstances().Select(i => i.InstanceId), Is.EqualTo(_multiInstanceIds));
	}

	[Test]
	public void An_instance_id_containing_the_separator_is_skipped()
	{
		var registry = Registry(new MultiConfigMusicIntegration("good", "bad::id"));

		Assert.That(registry.GetInstances().Select(i => i.InstanceId), Is.EqualTo(_onlyGoodInstanceId));
	}

	[Test]
	public void A_repeated_instance_id_is_announced_once()
	{
		var registry = Registry(new MultiConfigMusicIntegration("a", "a", "b"));

		Assert.That(registry.GetInstances().Select(i => i.InstanceId), Is.EqualTo(_multiInstanceIds));
	}

	private static MusicPlayerRegistry Registry(IIntegration integration)
		=> new(new FakeIntegrationRegistry(integration), new LoggerConfiguration().CreateLogger());

	private sealed class FakeIntegrationRegistry : IIntegrationRegistry
	{
		public event EventHandler<IntegrationAvailabilityChangedEventArgs>? AvailabilityChanged
		{
			add { }
			remove { }
		}

		public FakeIntegrationRegistry(IIntegration integration) => Integrations = [integration];

		public IReadOnlyList<IIntegration> Integrations { get; }

		public IActionDefinition? FindAction(string integrationId, string actionId) => null;

		public IActionDefinition? FindAction(QualifiedId id) => null;

		public IReadOnlyList<ActionDescriptor> GetActions(bool enabledOnly = true) => [];

		public bool IsEnabled(string integrationId) => true;

		public void SetEnabled(string integrationId, bool enabled)
		{
		}

		public IntegrationOrigin GetOrigin(string integrationId) => IntegrationOrigin.BuiltIn;

		public Task<IntegrationRegistrationResult> RegisterAsync(
			IIntegration integration,
			IntegrationOrigin origin = IntegrationOrigin.BuiltIn,
			IntegrationMetadata? metadata = null)
			=> Task.FromResult(IntegrationRegistrationResult.Success);

		public Task<bool> UnregisterAsync(string integrationId) => Task.FromResult(false);
	}

	private sealed class FakePlayer : IMusicPlayer
	{
		public Task<MusicPlayerState> GetStateAsync(CancellationToken cancellationToken = default)
			=> Task.FromResult(MusicPlayerState.Disconnected);

		public Task<MusicPlayerArtwork?> GetArtworkAsync(string artworkId,
			CancellationToken cancellationToken = default)
			=> Task.FromResult<MusicPlayerArtwork?>(null);

		public Task PlayAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task TogglePlayPauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task NextAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task PreviousAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;
	}

	private sealed class SingleConfigMusicIntegration : IIntegration, IMusicPlayerProvider, IConfigFlowProvider
	{
		private readonly string _localId;

		public SingleConfigMusicIntegration(string localId) => _localId = localId;

		public FakePlayer Player { get; } = new();

		public string Id => "music.single";
		public LocalizedText Name => "Single";
		public string Version => "1.0.0";
		public IReadOnlyList<IActionDefinition> Actions => [];
		public bool IsInitialized => true;
		public string ProviderName => "Single";
		public bool AllowsMultipleConfigurations => false;

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;

		public IConfigFlow CreateConfigFlow() => throw new NotSupportedException();

		public IReadOnlyList<MusicPlayerInstance> GetInstances() => [new(_localId, "Account")];

		public IMusicPlayer? GetPlayer(string instanceId) => instanceId == _localId ? Player : null;
	}

	private sealed class MultiConfigMusicIntegration : IIntegration, IMusicPlayerProvider
	{
		private readonly string[] _localIds;

		public MultiConfigMusicIntegration(params string[] localIds) => _localIds = localIds;

		public string Id => "music.multi";
		public LocalizedText Name => "Multi";
		public string Version => "1.0.0";
		public IReadOnlyList<IActionDefinition> Actions => [];
		public bool IsInitialized => true;

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;

		public IReadOnlyList<MusicPlayerInstance> GetInstances()
			=> _localIds.Select(id => new MusicPlayerInstance(id, id)).ToList();

		public IMusicPlayer? GetPlayer(string instanceId) => null;
	}
}
