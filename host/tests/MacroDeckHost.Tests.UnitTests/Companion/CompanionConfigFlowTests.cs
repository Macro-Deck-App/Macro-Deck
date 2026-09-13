using System.Text.Json;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Localization;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Ui.Sessions;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Tests.UnitTests.Companion;

[TestFixture]
internal sealed class CompanionConfigFlowTests
{
	private const string IntegrationId = CompanionHarness.IntegrationId;

	[Test]
	public async Task Continuing_the_flow_closes_the_dialog_and_creates_no_entry()
	{
		var harness = new CompanionHarness();
		var manager = Manager(harness);
		var started = await manager.StartAsync(IntegrationId, CancellationToken.None);

		var outcome = await SubmitThroughManagerAsync(manager, Task.FromResult(started));
		var again = await manager.SubmitAsync(started.FlowId,
			"connect",
			new Dictionary<string, JsonElement>(),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(started.Step!.Fields, Is.Empty);
			Assert.That(outcome.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(outcome.EntryId, Is.Null);
			Assert.That(harness.Entries, Is.Empty);
			Assert.That(again.FlowFound, Is.False);
		});
	}

	[Test]
	public async Task Submitting_while_off_is_stored_and_no_entry_exists_resumes_automatic_creation()
	{
		var harness = new CompanionHarness(storedOff: true);
		var manager = Manager(harness);
		var outcome = await SubmitThroughManagerAsync(manager,
			manager.StartAsync(IntegrationId, CancellationToken.None));
		var device = harness.AddDevice("Phone");

		await harness.ReportAsync("connection-1", device);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(harness.Registry.IsExplicitlyDisabled(IntegrationId), Is.False);
			Assert.That(harness.Entries.Select(entry => entry.Id), Is.EqualTo(new[] { device }));
		});
	}

	[Test]
	public async Task Resuming_automatic_creation_creates_entries_for_devices_already_connected()
	{
		var harness = new CompanionHarness(storedOff: true);
		var device = harness.AddDevice("Phone");
		await harness.ReportAsync("connection-1", device);
		var manager = Manager(harness);

		await SubmitThroughManagerAsync(manager, manager.StartAsync(IntegrationId, CancellationToken.None));
		await harness.DeviceRegistry.CreationFor(device);

		Assert.That(harness.Entries.Select(entry => entry.Id), Is.EqualTo(new[] { device }));
	}

	[TestCase("existing entry")]
	[TestCase("requested title")]
	[TestCase("new configuration")]
	public async Task Submitting_while_the_user_switched_off_a_usable_entry_keeps_the_integration_disabled(
		string startPath)
	{
		var harness = new CompanionHarness();
		var existing = harness.AddDevice("Phone");
		await harness.ReportAsync("connection-1", existing);
		harness.Registry.SetEnabled(IntegrationId, false);
		var manager = Manager(harness);
		var start = startPath switch
		{
			"existing entry" => manager.StartAsync(IntegrationId, null, existing, CancellationToken.None),
			"requested title" => manager.StartAsync(IntegrationId, "Tablet", null, CancellationToken.None),
			_ => manager.StartAsync(IntegrationId, CancellationToken.None)
		};

		var outcome = await SubmitThroughManagerAsync(manager, start);
		var newDevice = harness.AddDevice("Tablet");
		await harness.ReportAsync("connection-2", newDevice);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(harness.Registry.IsExplicitlyDisabled(IntegrationId), Is.True);
			Assert.That(harness.Entries.Select(entry => entry.Id), Is.EqualTo(new[] { existing }));
		});
	}

	[Test]
	public async Task After_a_restart_with_off_stored_the_flow_can_resume_automatic_creation_without_initializing()
	{
		var harness = new CompanionHarness(storedOff: true, register: false);
		var initializer = new IntegrationInitializer(harness.ScopeFactory,
			null!,
			null!,
			null!,
			null!,
			null!,
			null!,
			null!,
			null!,
			harness.DeviceRegistry,
			null!,
			harness.RefreshSignal,
			new FakeIntegrationHostIssueStore(),
			TestLayoutProviders.Host(),
			TestFolderViewProviders.Host(),
			TestWidgetTypeProviders.Host(),
			TestDeviceProviders.Host(),
			TimeProvider.System,
			CompanionHarness.Logger);
		var startup = new IntegrationStartupBackgroundService(new LifetimeStub(),
			harness.Registry,
			harness.ScopeFactory,
			new UserNotificationStore(),
			initializer,
			new RecordingMediator(),
			TimeProvider.System,
			CompanionHarness.Logger);

		var pending = await startup.RegisterAllAsync([harness.Integration]);
		var manager = Manager(harness);
		await SubmitThroughManagerAsync(manager, manager.StartAsync(IntegrationId, CancellationToken.None));

		Assert.Multiple(() =>
		{
			Assert.That(pending, Is.Empty);
			Assert.That(harness.Integration.IsInitialized, Is.False);
			Assert.That(harness.Registry.IsExplicitlyDisabled(IntegrationId), Is.False);
		});
	}

	private static async Task<ConfigFlowSubmitOutcome> SubmitThroughManagerAsync(
		ConfigFlowManager manager,
		Task<ConfigFlowStartOutcome> start)
	{
		var started = await start;
		Assert.That(started.FlowId, Is.Not.EqualTo(Guid.Empty), started.ErrorMessage);
		return await manager.SubmitAsync(started.FlowId,
			"connect",
			new Dictionary<string, JsonElement>(),
			CancellationToken.None);
	}

	private static ConfigFlowManager Manager(CompanionHarness harness)
	{
		var broker = new RecordingUiSessionBroker();
		return new ConfigFlowManager(harness.Registry,
			harness.ScopeFactory,
			null!,
			new NoOAuthCoordinator(),
			new FakeHostListenerState(),
			new EmptyRemotePluginSnapshotStore(),
			new ConfigFlowUiProviderRegistry(() => broker, CompanionHarness.Logger),
			new UiSessionRegistry(TimeProvider.System),
			broker,
			CompanionHarness.Logger,
			harness.Coordinator);
	}

	private sealed class NoOAuthCoordinator : IOAuthCallbackCoordinator
	{
		public string RedirectUri => "http://127.0.0.1/api/integrations/oauth/callback";

		public OAuthRegistration Register(Guid flowId) => new(RedirectUri, flowId.ToString("N"));

		public string? GetCode(string state) => null;

		public Task<bool> HandleCallbackAsync(string state,
			string? code,
			string? errorCode,
			CancellationToken cancellationToken)
			=> Task.FromResult(false);

		public void Release(string state)
		{
		}
	}

	private sealed class LifetimeStub : IHostApplicationLifetime
	{
		public CancellationToken ApplicationStarted => CancellationToken.None;
		public CancellationToken ApplicationStopping => CancellationToken.None;
		public CancellationToken ApplicationStopped => CancellationToken.None;

		public void StopApplication()
		{
		}
	}
}
