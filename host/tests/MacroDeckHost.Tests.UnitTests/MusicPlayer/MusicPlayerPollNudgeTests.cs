using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Localization;

namespace MacroDeckHost.Tests.UnitTests.MusicPlayer;

[TestFixture]
internal sealed class MusicPlayerPollNudgeTests
{
	[Test]
	public async Task An_action_of_a_music_player_integration_schedules_a_poll()
	{
		var nudge = new MusicPlayerPollNudge(RegistryWith(new FakeMusicIntegration()),
			TimeSpan.FromMilliseconds(10));

		nudge.NoteActionExecuted("music-integration");

		await nudge.Due.WaitAsync(TimeSpan.FromSeconds(5));
	}

	[Test]
	public async Task An_action_of_any_other_integration_schedules_nothing()
	{
		var nudge = new MusicPlayerPollNudge(RegistryWith(new FakeIntegration { Id = "other" }),
			TimeSpan.FromMilliseconds(10));

		nudge.NoteActionExecuted("other");
		await Task.Delay(100);

		Assert.That(nudge.Due.IsCompleted, Is.False);
	}

	[Test]
	public async Task Rearm_resets_the_nudge_for_the_next_action()
	{
		var nudge = new MusicPlayerPollNudge(RegistryWith(new FakeMusicIntegration()),
			TimeSpan.FromMilliseconds(10));
		nudge.NoteActionExecuted("music-integration");
		await nudge.Due.WaitAsync(TimeSpan.FromSeconds(5));

		nudge.Rearm();

		Assert.That(nudge.Due.IsCompleted, Is.False);

		nudge.NoteActionExecuted("music-integration");
		await nudge.Due.WaitAsync(TimeSpan.FromSeconds(5));
	}

	private static FakeRegistry RegistryWith(IIntegration integration) => new(integration);

	private sealed class FakeRegistry : IIntegrationRegistry
	{
		public event EventHandler<IntegrationAvailabilityChangedEventArgs>? AvailabilityChanged
		{
			add { }
			remove { }
		}

		public FakeRegistry(IIntegration integration) => Integrations = [integration];

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

	private sealed class FakeMusicIntegration : IIntegration, IMusicPlayerProvider
	{
		public string Id => "music-integration";
		public LocalizedText Name => "Music";
		public string Version => "1.0.0";
		public IReadOnlyList<IActionDefinition> Actions => [];
		public bool IsInitialized => true;
		public string ProviderName => "Music";

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;

		public IReadOnlyList<MusicPlayerInstance> GetInstances() => [];

		public IMusicPlayer? GetPlayer(string instanceId) => null;
	}
}
