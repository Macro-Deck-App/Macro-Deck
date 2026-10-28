using System.Text.Json;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Secrets.Approvals;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Ui.Sessions;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using MacroDeck.Localization;

namespace MacroDeckHost.Tests.UnitTests;

public class ConfigFlowManagerTests
{
	private static readonly ILogger _logger = Log.Logger;

	private FakeSecretService _secretService = null!;
	private FakeConfigFlowStore _store = null!;
	private FakeIntegrationLifecycle _lifecycle = null!;
	private FakeOAuthCallbackCoordinator _oauth = null!;
	private FakeHostListenerState _listenerState = null!;
	private ConfigFlowIntegration _integration = null!;
	private ConfigFlowManager _manager = null!;

	[SetUp]
	public void SetUp()
	{
		_secretService = new FakeSecretService();
		_store = new FakeConfigFlowStore();
		_lifecycle = new FakeIntegrationLifecycle();
		_oauth = new FakeOAuthCallbackCoordinator();
		_listenerState = new FakeHostListenerState();
		_integration = new ConfigFlowIntegration();

		var registry = new FakeIntegrationRegistry();
		registry.Add(_integration);

		var services = new ServiceCollection();
		services.AddSingleton<ISecretService>(_secretService);
		services.AddSingleton<IIntegrationConfigStore>(_store);
		services.AddSingleton(TestLocalization.Resolver);
		services.AddSingleton(TestLocalization.Preferences);
		var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

		var uiSessionBroker = new RecordingUiSessionBroker();
		_manager = new ConfigFlowManager(registry,
			scopeFactory,
			_lifecycle,
			_oauth,
			_listenerState,
			new EmptyRemotePluginSnapshotStore(),
			new ConfigFlowUiProviderRegistry(() => uiSessionBroker, _logger),
			new UiSessionRegistry(TimeProvider.System),
			uiSessionBroker,
			_logger);
	}

	private ConfigFlowManager CreateManager(
		IIntegration integration,
		IHostListenerState listenerState,
		IOAuthCallbackCoordinator? oauth = null)
	{
		var registry = new FakeIntegrationRegistry();
		registry.Add(integration);

		var services = new ServiceCollection();
		services.AddSingleton<ISecretService>(_secretService);
		services.AddSingleton<IIntegrationConfigStore>(_store);
		services.AddSingleton(TestLocalization.Resolver);
		services.AddSingleton(TestLocalization.Preferences);
		var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

		var uiSessionBroker = new RecordingUiSessionBroker();
		return new ConfigFlowManager(registry,
			scopeFactory,
			_lifecycle,
			oauth ?? _oauth,
			listenerState,
			new EmptyRemotePluginSnapshotStore(),
			new ConfigFlowUiProviderRegistry(() => uiSessionBroker, _logger),
			new UiSessionRegistry(TimeProvider.System),
			uiSessionBroker,
			_logger);
	}

	[Test]
	public async Task StartAsync_returns_first_step_for_supported_integration()
	{
		var outcome = await _manager.StartAsync("cf", CancellationToken.None);

		var context = (IConfigFlowEntryContext)_integration.Flow.LastContext!;
		Assert.Multiple(() =>
		{
			Assert.That(outcome.Supported, Is.True);
			Assert.That(outcome.FlowId, Is.Not.EqualTo(Guid.Empty));
			Assert.That(outcome.Step!.StepId, Is.EqualTo("connection"));
			Assert.That(context.EntryTitle, Is.Null);
		});
	}

	[Test]
	public async Task StartAsync_exposes_existing_entry_metadata_to_the_config_flow()
	{
		var entryId = SeedExistingEntry();

		await _manager.StartAsync("cf", null, entryId, CancellationToken.None);

		var context = (IConfigFlowEntryContext)_integration.Flow.LastContext!;
		Assert.That(context.EntryTitle, Is.EqualTo("Existing"));
	}

	[Test]
	public async Task StartAsync_reports_unsupported_integration()
	{
		var registry = new FakeIntegrationRegistry();
		registry.Add(new FakeIntegration { Id = "plain" });
		var services = new ServiceCollection();
		services.AddSingleton<ISecretService>(_secretService);
		services.AddSingleton<IIntegrationConfigStore>(_store);
		services.AddSingleton(TestLocalization.Resolver);
		services.AddSingleton(TestLocalization.Preferences);
		var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
		var uiSessionBroker = new RecordingUiSessionBroker();
		var manager = new ConfigFlowManager(registry,
			scopeFactory,
			_lifecycle,
			_oauth,
			_listenerState,
			new EmptyRemotePluginSnapshotStore(),
			new ConfigFlowUiProviderRegistry(() => uiSessionBroker, _logger),
			new UiSessionRegistry(TimeProvider.System),
			uiSessionBroker,
			_logger);

		var outcome = await manager.StartAsync("plain", CancellationToken.None);

		Assert.That(outcome.Supported, Is.False);
	}

	[Test]
	public async Task SubmitAsync_hands_a_typed_secret_to_the_flow_then_persists_only_a_reference()
	{
		var start = await _manager.StartAsync("cf", CancellationToken.None);

		var invalid = await _manager.SubmitAsync(start.FlowId,
			"connection",
			Values(("apiKey", JsonValue("nope"))),
			CancellationToken.None);
		var advanced = await _manager.SubmitAsync(start.FlowId,
			"connection",
			Values(("apiKey", JsonValue("super-secret-key"))),
			CancellationToken.None);
		var flowInput = _integration.Flow.LastInput!["apiKey"];
		var done = await _manager.SubmitAsync(start.FlowId,
			"workspace",
			Values(("name", JsonValue("Prod"))),
			CancellationToken.None);

		var created = _store.Created.Single();
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(invalid.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(advanced.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
			Assert.That(flowInput, Is.EqualTo("super-secret-key"));
			Assert.That(done.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(SecretReferenceJson.TryGet(created.Values["apiKey"], out var secretId), Is.True);
			Assert.That(await _secretService.Resolve(secretId), Is.EqualTo("super-secret-key"));
			Assert.That(created.Values["name"].GetString(), Is.EqualTo("Prod"));
			Assert.That(_lifecycle.Reinitialized, Does.Contain("cf"));
		});
	}

	[Test]
	public async Task A_new_entry_flow_refuses_a_submitted_secret_reference_and_never_decrypts_it()
	{
		var foreign = _secretService.Store("someone-elses-token");
		var start = await _manager.StartAsync("cf", CancellationToken.None);

		var outcome = await _manager.SubmitAsync(start.FlowId,
			"connection",
			Values(("apiKey", SecretRef(foreign))),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(outcome.Message.IsEmpty, Is.False);
			Assert.That(_integration.Flow.LastInput, Is.Null);
			Assert.That(_store.Created, Is.Empty);
		});
	}

	[Test]
	public async Task A_reconfigure_refuses_a_secret_reference_the_entry_does_not_already_hold()
	{
		SeedExistingSecretEntry();
		var foreign = _secretService.Store("someone-elses-token");
		var entryId = _store.Existing.Single().Id;
		var start = await _manager.StartAsync("cf", null, entryId, CancellationToken.None);

		var outcome = await _manager.SubmitAsync(start.FlowId,
			"connection",
			Values(("apiKey", SecretRef(foreign))),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(_integration.Flow.LastInput, Is.Null);
			Assert.That(_store.Replaced, Is.Empty);
		});
	}

	[Test]
	public async Task A_reconfigure_accepts_the_reference_the_entry_already_holds_for_that_key()
	{
		var retained = SeedExistingSecretEntry();
		var entryId = _store.Existing.Single().Id;
		var start = await _manager.StartAsync("cf", null, entryId, CancellationToken.None);

		var outcome = await _manager.SubmitAsync(start.FlowId,
			"connection",
			Values(("apiKey", SecretRef(retained))),
			CancellationToken.None);

		Assert.That(outcome.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
	}

	[Test]
	public async Task An_untrusted_writer_cannot_reconfigure_an_entry_that_holds_secrets()
	{
		SeedExistingSecretEntry();
		var entryId = _store.Existing.Single().Id;

		var outcome = await _manager.StartAsync("cf",
			null,
			entryId,
			SecretApprovalMode.Withhold,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.FlowId, Is.EqualTo(Guid.Empty));
			Assert.That(outcome.ErrorMessage, Is.Not.Empty);
			Assert.That(_integration.Flow.LastInput, Is.Null);
		});
	}

	[Test]
	public async Task An_untrusted_writer_can_still_create_a_new_entry()
	{
		var start = await _manager.StartAsync("cf", null, null, SecretApprovalMode.Withhold, CancellationToken.None);
		var advanced = await _manager.SubmitAsync(start.FlowId,
			"connection",
			Values(("apiKey", JsonValue("super-secret-key"))),
			[],
			null,
			SecretApprovalMode.Withhold,
			CancellationToken.None);

		Assert.That(advanced.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
	}

	[Test]
	public async Task SubmitAsync_encrypts_a_secret_field_the_step_declares_in_AdvancedFields()
	{
		var manager = CreateManager(new AdvancedSecretConfigFlowIntegration(), _listenerState);
		var start = await manager.StartAsync("cf-advanced", CancellationToken.None);

		var done = await manager.SubmitAsync(start.FlowId,
			"connection",
			Values(("name", JsonValue("Prod")), ("ownClientSecret", JsonValue("s3cr3t-client-secret"))),
			CancellationToken.None);

		Assert.That(done.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));

		var created = _store.Created.Single();
		Assert.That(SecretReferenceJson.TryGet(created.Values["ownClientSecret"], out var secretId),
			Is.True,
			"a Secret field in AdvancedFields was persisted without being encrypted");
		Assert.That(await _secretService.Resolve(secretId), Is.EqualTo("s3cr3t-client-secret"));
		Assert.That(JsonSerializer.Serialize(created.Values), Does.Not.Contain("s3cr3t-client-secret"));
	}

	[Test]
	public async Task StartAsync_is_supported_for_a_single_configuration_integration_that_is_already_configured()
	{
		_integration.AllowsMultipleConfigurations = false;
		SeedExistingEntry();

		var outcome = await _manager.StartAsync("cf", CancellationToken.None);

		Assert.That(outcome.Supported, Is.True);
		Assert.That(outcome.Step!.StepId, Is.EqualTo("connection"));
	}

	[Test]
	public async Task SubmitAsync_reconfigures_the_existing_entry_of_a_single_configuration_integration()
	{
		_integration.AllowsMultipleConfigurations = false;
		var existingId = SeedExistingEntry();

		var outcome = await CompleteFlow();

		Assert.That(outcome.EntryId, Is.EqualTo(existingId));
		Assert.That(_store.Created, Is.Empty);
		Assert.That(_store.Replaced, Has.Count.EqualTo(1));
		Assert.That(_store.Replaced[0].Values["name"].GetString(), Is.EqualTo("Prod"));
		Assert.That(_lifecycle.Reinitialized, Does.Contain("cf"));
	}

	[Test]
	public async Task Editing_with_a_blank_secret_retains_the_existing_reference_without_exposing_it()
	{
		var secretId = _secretService.Store("super-secret-key");
		var entryId = Guid.NewGuid();
		_store.Existing.Add(new ConfigEntryRecord(entryId,
			"cf",
			"Existing",
			DateTime.UtcNow,
			Values(("apiKey", SecretRef(secretId)))));

		var start = await _manager.StartAsync("cf", null, entryId, CancellationToken.None);
		var advanced = await _manager.SubmitAsync(start.FlowId,
			"connection",
			Values(("apiKey", JsonValue(string.Empty))),
			CancellationToken.None);
		var done = await _manager.SubmitAsync(start.FlowId,
			"workspace",
			Values(("name", JsonValue("Prod"))),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(start.InitialValues, Does.Not.ContainKey("apiKey"));
			Assert.That(start.StoredSecretFields, Does.Contain("apiKey"));
			Assert.That(advanced.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
			Assert.That(done.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(SecretReferenceJson.TryGet(_store.Replaced.Single().Values["apiKey"], out var retained),
				Is.True);
			Assert.That(retained, Is.EqualTo(secretId));
		});
	}

	[Test]
	public async Task Editing_an_entry_offers_a_stored_switch_as_it_was_saved()
	{
		var entryId = Guid.NewGuid();
		_store.Existing.Add(new ConfigEntryRecord(entryId,
			"cf",
			"Existing",
			DateTime.UtcNow,
			Values(("importSharedVariables", JsonSerializer.SerializeToElement(true)))));

		var start = await _manager.StartAsync("cf", null, entryId, CancellationToken.None);

		Assert.That(start.InitialValues!["importSharedVariables"].ValueKind, Is.EqualTo(JsonValueKind.True));
	}

	[Test]
	public async Task A_failed_secret_replacement_rolls_back_to_the_retained_reference_for_retry()
	{
		var secretId = SeedExistingSecretEntry();
		var entryId = _store.Existing.Single().Id;
		var start = await _manager.StartAsync("cf", null, entryId, CancellationToken.None);

		var failed = await _manager.SubmitAsync(start.FlowId,
			"connection",
			Values(("apiKey", JsonValue("incorrect-secret"))),
			CancellationToken.None);
		Assert.That(_store.Replaced, Is.Empty, "the failed attempt persisted configuration state");
		var retried = await _manager.SubmitAsync(start.FlowId,
			"connection",
			Values(("apiKey", JsonValue(string.Empty))),
			CancellationToken.None);
		var completed = await _manager.SubmitAsync(start.FlowId,
			"workspace",
			Values(("name", JsonValue("Prod"))),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(failed.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(retried.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
			Assert.That(completed.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(SecretReferenceJson.TryGet(_store.Replaced.Single().Values["apiKey"], out var retained),
				Is.True);
			Assert.That(retained, Is.EqualTo(secretId));
		});
	}

	[Test]
	public async Task A_failed_explicit_secret_clear_rolls_back_to_the_retained_reference_for_retry()
	{
		var secretId = SeedExistingSecretEntry();
		var entryId = _store.Existing.Single().Id;
		var start = await _manager.StartAsync("cf", null, entryId, CancellationToken.None);

		var failed = await _manager.SubmitAsync(start.FlowId,
			"connection",
			Values(),
			["apiKey"],
			CancellationToken.None);
		Assert.That(_store.Replaced, Is.Empty, "the failed attempt persisted configuration state");
		var retried = await _manager.SubmitAsync(start.FlowId,
			"connection",
			Values(("apiKey", JsonValue(string.Empty))),
			CancellationToken.None);
		await _manager.SubmitAsync(start.FlowId,
			"workspace",
			Values(("name", JsonValue("Prod"))),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(failed.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(retried.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
			Assert.That(SecretReferenceJson.TryGet(_store.Replaced.Single().Values["apiKey"], out var retained),
				Is.True);
			Assert.That(retained, Is.EqualTo(secretId));
		});
	}

	[Test]
	public async Task SubmitAsync_stops_the_integration_before_rewriting_its_entry()
	{
		_integration.AllowsMultipleConfigurations = false;
		SeedExistingEntry();
		var replacedWhenStopped = -1;
		_lifecycle.OnShutdown = () => replacedWhenStopped = _store.Replaced.Count;

		await CompleteFlow();

		Assert.That(_lifecycle.ShutDown, Does.Contain("cf"));
		Assert.That(replacedWhenStopped, Is.Zero);
	}

	[Test]
	public async Task SubmitAsync_creates_an_entry_when_the_adopted_one_was_removed_mid_flow()
	{
		_integration.AllowsMultipleConfigurations = false;
		var existingId = SeedExistingEntry();

		var start = await _manager.StartAsync("cf", CancellationToken.None);
		_store.Existing.Clear();

		var outcome = await CompleteFlow(start.FlowId);

		Assert.That(_store.Created, Has.Count.EqualTo(1));
		Assert.That(outcome.EntryId, Is.Not.EqualTo(existingId));
	}

	[Test]
	public async Task SubmitAsync_creates_a_second_entry_for_a_multi_configuration_integration()
	{
		var existingId = SeedExistingEntry();

		var outcome = await CompleteFlow();

		Assert.That(_store.Replaced, Is.Empty);
		Assert.That(_store.Created, Has.Count.EqualTo(1));
		Assert.That(outcome.EntryId, Is.Not.EqualTo(existingId));
	}

	[Test]
	public async Task SubmitAsync_reports_expired_flow()
	{
		var outcome = await _manager.SubmitAsync(Guid.NewGuid(), "connection", Values(), CancellationToken.None);

		Assert.That(outcome.FlowFound, Is.False);
	}

	[Test]
	public async Task AbandonAsync_releases_the_flow_and_its_oauth_registration()
	{
		var start = await _manager.StartAsync("cf", CancellationToken.None);
		var flowId = Guid.Parse(start.FlowId.ToString());

		await _manager.AbandonAsync(flowId, CancellationToken.None);

		var outcome = await _manager.SubmitAsync(flowId,
			"connection",
			Values(("apiKey", JsonValue("x"))),
			CancellationToken.None);
		Assert.Multiple(() =>
		{
			Assert.That(outcome.FlowFound, Is.False);
			Assert.That(_oauth.Released, Does.Contain(start.FlowId.ToString("N")));
		});
	}

	// The redirect URI a browser would be sent to points at the dead public port, so completing the
	// login can never happen; refusing at the point the flow reads it is what turns a hang into a
	// visible error (issue #515).
	[Test]
	public async Task SubmitAsync_refuses_a_flow_that_reads_OAuth_while_the_public_listener_is_unavailable()
	{
		var listenerState = new FakeHostListenerState { PublicListenerAvailable = false };
		var manager = CreateManager(new OAuthConfigFlowIntegration(), listenerState);

		var start = await manager.StartAsync("cf-oauth", CancellationToken.None);
		Assert.That(start.Supported, Is.True, "starting must still succeed: the flow has not touched OAuth yet");

		var outcome = await manager.SubmitAsync(start.FlowId,
			"connect",
			Values(),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(outcome.Message), Is.Not.Null.And.Not.Empty);
			Assert.That(outcome.ExternalUrl, Is.Null);
		});
	}

	[Test]
	public async Task Signing_in_again_within_one_flow_hands_the_flow_the_new_code()
	{
		using var logger = new LoggerConfiguration().CreateLogger();
		var oauth = new OAuthCallbackCoordinator(new RecordingUiTransport(), logger);
		var integration = new OAuthConfigFlowIntegration();
		var manager = CreateManager(integration, new FakeHostListenerState(), oauth);
		var start = await manager.StartAsync("cf-oauth", CancellationToken.None);

		await manager.SubmitAsync(start.FlowId, "connect", Values(), CancellationToken.None);
		var state = integration.LastFlow!.Session!.State;
		await oauth.HandleCallbackAsync(state, "first-code", null, CancellationToken.None);
		await manager.SubmitAsync(start.FlowId, "connect", Values(), CancellationToken.None);
		var accepted = await oauth.HandleCallbackAsync(state, "second-code", null, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(accepted, Is.True);
			Assert.That(integration.LastFlow.Session.AuthorizationCode, Is.EqualTo("second-code"));
		});
	}

	[Test]
	public async Task A_flow_that_never_touches_OAuth_is_unaffected_by_the_public_listener_being_unavailable()
	{
		var listenerState = new FakeHostListenerState { PublicListenerAvailable = false };
		var integration = new ConfigFlowIntegration();
		var manager = CreateManager(integration, listenerState);

		var start = await manager.StartAsync("cf", CancellationToken.None);
		var advanced = await manager.SubmitAsync(start.FlowId,
			"connection",
			Values(("apiKey", JsonValue("super-secret-key"))),
			CancellationToken.None);
		var done = await manager.SubmitAsync(start.FlowId,
			"workspace",
			Values(("name", JsonValue("Prod"))),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(start.Supported, Is.True);
			Assert.That(start.Step!.StepId, Is.EqualTo("connection"));
			Assert.That(advanced.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
			Assert.That(done.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(done.EntryId, Is.Not.Null);
		});
	}

	[Test]
	public async Task AbandonAsync_disposes_a_disposable_flow()
	{
		var start = await _manager.StartAsync("cf", CancellationToken.None);
		var flowId = Guid.Parse(start.FlowId.ToString());

		await _manager.AbandonAsync(flowId, CancellationToken.None);

		Assert.That(_integration.Flow.Disposed, Is.True);
	}

	[Test]
	public async Task AbandonAsync_is_a_no_op_for_an_unknown_flow()
	{
		await _manager.AbandonAsync(Guid.NewGuid(), CancellationToken.None);
	}

	private Guid SeedExistingEntry()
	{
		var id = Guid.NewGuid();
		_store.Existing.Add(new ConfigEntryRecord(id,
			"cf",
			"Existing",
			DateTime.UtcNow,
			new Dictionary<string, JsonElement>()));
		return id;
	}

	private Guid SeedExistingSecretEntry()
	{
		var secretId = _secretService.Store("super-secret-key");
		_store.Existing.Add(new ConfigEntryRecord(Guid.NewGuid(),
			"cf",
			"Existing",
			DateTime.UtcNow,
			Values(("apiKey", SecretRef(secretId)))));
		return secretId;
	}

	[Test]
	public async Task Going_back_drops_the_values_and_secrets_of_the_abandoned_branch()
	{
		var manager = CreateManager(new BranchingConfigFlowIntegration(), _listenerState);
		var flowId = (await manager.StartAsync("cf-branch", CancellationToken.None)).FlowId;

		await manager.SubmitAsync(flowId, "brand", Values(("brand", JsonValue("a"))), [], 0, CancellationToken.None);
		await manager.SubmitAsync(flowId, "a", Values(("aKey", JsonValue("a-secret"))), [], 1, CancellationToken.None);
		var failed = await manager.SubmitAsync(flowId,
			"brand",
			Values(("brand", JsonValue("unknown"))),
			[],
			0,
			CancellationToken.None);
		await manager.SubmitAsync(flowId, "brand", Values(("brand", JsonValue("b"))), [], 0, CancellationToken.None);
		await manager.SubmitAsync(flowId, "b", Values(("bName", JsonValue("studio"))), [], 1, CancellationToken.None);
		var done = await manager.SubmitAsync(flowId, "confirm", Values(), [], 2, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(failed.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(done.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(_store.Created.Single().Values.Keys, Is.EquivalentTo(new[] { "brand", "bName" }));
			Assert.That(_secretService.Count, Is.Zero);
		});
	}

	[Test]
	public async Task A_client_that_sends_no_step_index_keeps_every_submitted_value()
	{
		var manager = CreateManager(new BranchingConfigFlowIntegration(), _listenerState);
		var flowId = (await manager.StartAsync("cf-branch", CancellationToken.None)).FlowId;

		await manager.SubmitAsync(flowId, "brand", Values(("brand", JsonValue("a"))), CancellationToken.None);
		await manager.SubmitAsync(flowId, "a", Values(("aKey", JsonValue("a-secret"))), CancellationToken.None);
		await manager.SubmitAsync(flowId, "brand", Values(("brand", JsonValue("b"))), CancellationToken.None);
		await manager.SubmitAsync(flowId, "b", Values(("bName", JsonValue("studio"))), CancellationToken.None);
		await manager.SubmitAsync(flowId, "confirm", Values(), CancellationToken.None);

		Assert.That(_store.Created.Single().Values.Keys, Is.EquivalentTo(new[] { "brand", "aKey", "bName" }));
	}

	private async Task<ConfigFlowSubmitOutcome> CompleteFlow(Guid? flowId = null)
	{
		if (flowId is null)
		{
			var start = await _manager.StartAsync("cf", CancellationToken.None);
			flowId = start.FlowId;
		}

		await _manager.SubmitAsync(flowId.Value,
			"connection",
			Values(("apiKey", JsonValue("super-secret-key"))),
			CancellationToken.None);

		return await _manager.SubmitAsync(flowId.Value,
			"workspace",
			Values(("name", JsonValue("Prod"))),
			CancellationToken.None);
	}

	[TestCase(false)]
	[TestCase(true)]
	public async Task A_flow_completed_without_an_entry_stores_nothing_and_lifts_an_off_choice_only_without_an_entry(
		bool hasEntry)
	{
		var services = new ServiceCollection();
		services.AddSingleton<ISecretService>(_secretService);
		services.AddSingleton<IIntegrationConfigStore>(_store);
		services.AddSingleton(TestLocalization.Resolver);
		services.AddSingleton(TestLocalization.Preferences);
		var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
		var registry = new IntegrationRegistry(scopeFactory,
			new Companion.CompanionHarness.MemoryStateStore(new Dictionary<string, bool>
				{ ["com.example.no-entry"] = false }),
			_logger);
		Assert.That(await registry.RegisterAsync(new NoEntryConfigFlowIntegration()),
			Is.EqualTo(IntegrationRegistrationResult.Success));
		if (hasEntry)
		{
			_store.Existing.Add(new ConfigEntryRecord(Guid.NewGuid(),
				"com.example.no-entry",
				"Studio",
				DateTime.UtcNow,
				new Dictionary<string, JsonElement>()));
		}

		var availability = new List<IntegrationAvailabilityChangedEventArgs>();
		registry.AvailabilityChanged += (_, change) => availability.Add(change);
		var uiSessionBroker = new RecordingUiSessionBroker();
		var manager = new ConfigFlowManager(registry,
			scopeFactory,
			_lifecycle,
			_oauth,
			_listenerState,
			new EmptyRemotePluginSnapshotStore(),
			new ConfigFlowUiProviderRegistry(() => uiSessionBroker, _logger),
			new UiSessionRegistry(TimeProvider.System),
			uiSessionBroker,
			_logger);

		var start = await manager.StartAsync("com.example.no-entry", CancellationToken.None);
		var outcome = await manager.SubmitAsync(start.FlowId,
			"name",
			Values(("name", JsonValue("Studio"))),
			CancellationToken.None);
		var again = await manager.SubmitAsync(start.FlowId, "name", Values(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(outcome.EntryId, Is.Null);
			Assert.That(_store.Created, Is.Empty);
			Assert.That(_lifecycle.Reinitialized, Is.Empty);
			Assert.That(again.FlowFound, Is.False);
			Assert.That(availability.Select(change => (change.IntegrationId, change.IsAvailable)),
				Is.EqualTo(hasEntry ? Array.Empty<(string, bool)>() : new[] { ("com.example.no-entry", true) }));
			Assert.That(registry.IsExplicitlyDisabled("com.example.no-entry"), Is.EqualTo(hasEntry));
		});
	}

	private sealed class NoEntryConfigFlowIntegration : IIntegration, IConfigFlowProvider
	{
		public string Id => "com.example.no-entry";
		public LocalizedText Name => "No Entry Config Flow Integration";
		public string Version => "1.0.0";
		public IReadOnlyList<IActionDefinition> Actions => [];

		public Task InitializeAsync(IIntegrationContext context)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}

		public IConfigFlow CreateConfigFlow()
		{
			return new NoEntryConfigFlow();
		}

		public bool IsInitialized => true;
	}

	private sealed class NoEntryConfigFlow : IConfigFlow
	{
		public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
		{
			return Task.FromResult(ConfigFlowResult.Step(new ConfigFlowStep
			{
				StepId = "name", Fields = [ActionParameter.Text("name")]
			}));
		}

		public Task<ConfigFlowResult> SubmitAsync(
			string stepId,
			IReadOnlyDictionary<string, object?> input,
			IConfigFlowContext context,
			CancellationToken cancellationToken)
		{
			return Task.FromResult(ConfigFlowResult.CompleteWithoutEntry());
		}
	}

	private static Dictionary<string, JsonElement> Values(params (string Key, JsonElement Value)[] pairs)
	{
		return pairs.ToDictionary(p => p.Key, p => p.Value);
	}

	private static JsonElement SecretRef(Guid id)
	{
		return JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["$secret"] = id.ToString() });
	}

	private static JsonElement JsonValue(string value)
	{
		return JsonSerializer.SerializeToElement(value);
	}

	private sealed class ConfigFlowIntegration : IIntegration, IConfigFlowProvider
	{
		public RecordingConfigFlow Flow { get; } = new();

		public string Id => "cf";
		public LocalizedText Name => "Config Flow Integration";
		public string Version => "1.0.0";
		public IReadOnlyList<IActionDefinition> Actions => [];

		public Task InitializeAsync(IIntegrationContext context)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}

		public IConfigFlow CreateConfigFlow()
		{
			return Flow;
		}

		public bool IsInitialized => true;

		public bool AllowsMultipleConfigurations { get; set; } = true;
	}

	private sealed class RecordingConfigFlow : IConfigFlow, IAsyncDisposable
	{
		public bool Disposed { get; private set; }

		public IReadOnlyDictionary<string, object?>? LastInput { get; private set; }

		public IConfigFlowContext? LastContext { get; private set; }

		public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
		{
			LastContext = context;
			return Task.FromResult(ConfigFlowResult.Step(ConnectionStep()));
		}

		public Task<ConfigFlowResult> SubmitAsync(
			string stepId,
			IReadOnlyDictionary<string, object?> input,
			IConfigFlowContext context,
			CancellationToken cancellationToken)
		{
			LastInput = input;

			if (stepId == "connection")
			{
				return Task.FromResult(input.GetValueOrDefault("apiKey") as string == "super-secret-key"
					? ConfigFlowResult.Step(WorkspaceStep())
					: ConfigFlowResult.Error(ConnectionStep(), "Invalid credentials"));
			}

			return Task.FromResult(ConfigFlowResult.Complete("Prod"));
		}

		private static ConfigFlowStep ConnectionStep()
		{
			return new ConfigFlowStep { StepId = "connection", Fields = [ActionParameter.Password("apiKey")] };
		}

		private static ConfigFlowStep WorkspaceStep()
		{
			return new ConfigFlowStep { StepId = "workspace", Fields = [ActionParameter.Text("name")] };
		}

		public ValueTask DisposeAsync()
		{
			Disposed = true;
			return ValueTask.CompletedTask;
		}
	}

	private sealed class BranchingConfigFlowIntegration : IIntegration, IConfigFlowProvider
	{
		public string Id => "cf-branch";
		public LocalizedText Name => "Branching Config Flow Integration";
		public string Version => "1.0.0";
		public IReadOnlyList<IActionDefinition> Actions => [];

		public Task InitializeAsync(IIntegrationContext context)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}

		public IConfigFlow CreateConfigFlow()
		{
			return new BranchingConfigFlow();
		}

		public bool IsInitialized => true;
	}

	private sealed class BranchingConfigFlow : IConfigFlow
	{
		public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
		{
			return Task.FromResult(ConfigFlowResult.Step(BrandStep()));
		}

		public Task<ConfigFlowResult> SubmitAsync(
			string stepId,
			IReadOnlyDictionary<string, object?> input,
			IConfigFlowContext context,
			CancellationToken cancellationToken)
		{
			return Task.FromResult(stepId switch
			{
				"brand" => (input.GetValueOrDefault("brand") as string) switch
				{
					"a" => ConfigFlowResult.Step(new ConfigFlowStep
						{ StepId = "a", Fields = [ActionParameter.Password("aKey")] }),
					"b" => ConfigFlowResult.Step(new ConfigFlowStep
						{ StepId = "b", Fields = [ActionParameter.Text("bName")] }),
					_ => ConfigFlowResult.Error(BrandStep(), "Unknown brand")
				},
				"a" or "b" => ConfigFlowResult.Step(new ConfigFlowStep { StepId = "confirm", Fields = [] }),
				_ => ConfigFlowResult.Complete("Branch")
			});
		}

		private static ConfigFlowStep BrandStep()
		{
			return new ConfigFlowStep { StepId = "brand", Fields = [ActionParameter.Text("brand")] };
		}
	}

	private sealed class OAuthConfigFlowIntegration : IIntegration, IConfigFlowProvider
	{
		public string Id => "cf-oauth";
		public LocalizedText Name => "OAuth Config Flow Integration";
		public string Version => "1.0.0";
		public IReadOnlyList<IActionDefinition> Actions => [];

		public Task InitializeAsync(IIntegrationContext context)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}

		public OAuthTouchingConfigFlow? LastFlow { get; private set; }

		public IConfigFlow CreateConfigFlow()
		{
			return LastFlow = new OAuthTouchingConfigFlow();
		}

		public bool IsInitialized => true;
	}

	private sealed class OAuthTouchingConfigFlow : IConfigFlow
	{
		public IOAuthSession? Session { get; private set; }

		public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
		{
			return Task.FromResult(ConfigFlowResult.Step(new ConfigFlowStep { StepId = "connect", Fields = [] }));
		}

		public Task<ConfigFlowResult> SubmitAsync(
			string stepId,
			IReadOnlyDictionary<string, object?> input,
			IConfigFlowContext context,
			CancellationToken cancellationToken)
		{
			Session = context.OAuth;
			return Task.FromResult(ConfigFlowResult.External(context.OAuth.RedirectUri, "resume"));
		}
	}

	private sealed class AdvancedSecretConfigFlowIntegration : IIntegration, IConfigFlowProvider
	{
		public string Id => "cf-advanced";
		public LocalizedText Name => "Advanced Secret Config Flow Integration";
		public string Version => "1.0.0";
		public IReadOnlyList<IActionDefinition> Actions => [];

		public Task InitializeAsync(IIntegrationContext context)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}

		public IConfigFlow CreateConfigFlow()
		{
			return new AdvancedSecretConfigFlow();
		}

		public bool IsInitialized => true;
	}

	private sealed class AdvancedSecretConfigFlow : IConfigFlow
	{
		public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
		{
			return Task.FromResult(ConfigFlowResult.Step(new ConfigFlowStep
			{
				StepId = "connection",
				Fields = [ActionParameter.Text("name")],
				AdvancedFields = [ActionParameter.Secret("ownClientSecret", "Own client secret")]
			}));
		}

		public Task<ConfigFlowResult> SubmitAsync(
			string stepId,
			IReadOnlyDictionary<string, object?> input,
			IConfigFlowContext context,
			CancellationToken cancellationToken)
		{
			return Task.FromResult(ConfigFlowResult.Complete("Prod"));
		}
	}

	private sealed class FakeConfigFlowStore : IIntegrationConfigStore
	{
		public List<ConfigEntryRecord> Created { get; } = [];

		public List<ConfigEntryRecord> Existing { get; } = [];

		public List<ConfigEntryRecord> Replaced { get; } = [];

		public Task<IReadOnlyList<ConfigEntrySummary>> List(string integrationId)
		{
			return Task.FromResult<IReadOnlyList<ConfigEntrySummary>>(Existing
				.Where(e => e.IntegrationId == integrationId)
				.Select(e => new ConfigEntrySummary(e.Id, e.IntegrationId, e.Title, e.CreatedAt))
				.ToList());
		}

		public Task<ConfigEntryRecord?> Find(Guid entryId)
		{
			return Task.FromResult(Existing.FirstOrDefault(e => e.Id == entryId));
		}

		public Task<Guid> Create(string integrationId, string title, IReadOnlyDictionary<string, JsonElement> values)
		{
			var id = Guid.NewGuid();
			var record = new ConfigEntryRecord(id, integrationId, title, DateTime.UtcNow, values);
			Created.Add(record);
			Existing.Add(record);
			return Task.FromResult(id);
		}

		public Task<bool> UpdateValues(Guid entryId, IReadOnlyDictionary<string, JsonElement> values)
		{
			return Task.FromResult(false);
		}

		public Task<bool> Replace(Guid entryId, string title, IReadOnlyDictionary<string, JsonElement> values)
		{
			var index = Existing.FindIndex(e => e.Id == entryId);
			if (index < 0)
			{
				return Task.FromResult(false);
			}

			var replaced = Existing[index] with { Title = title, Values = values };
			Existing[index] = replaced;
			Replaced.Add(replaced);
			return Task.FromResult(true);
		}

		public Task Delete(Guid entryId)
		{
			return Task.CompletedTask;
		}
	}

	private sealed class FakeOAuthCallbackCoordinator : IOAuthCallbackCoordinator
	{
		public List<string> Released { get; } = [];

		public string RedirectUri => $"http://127.0.0.1:{BuildConfig.PublicPort}/api/integrations/oauth/callback";

		public OAuthRegistration Register(Guid flowId)
		{
			return new OAuthRegistration(RedirectUri, flowId.ToString("N"));
		}

		public string? GetCode(string state)
		{
			return null;
		}

		public void ExpectCallback(string state)
		{
		}

		public Task<bool> HandleCallbackAsync(
			string state,
			string? code,
			string? error,
			CancellationToken cancellationToken)
		{
			return Task.FromResult(true);
		}

		public void Release(string state)
		{
			Released.Add(state);
		}
	}

	private sealed class FakeIntegrationLifecycle : IIntegrationLifecycle
	{
		public List<string> Reinitialized { get; } = [];

		public List<string> ShutDown { get; } = [];

		public Action? OnShutdown { get; set; }

		public Task ReinitializeAsync(string integrationId, CancellationToken cancellationToken = default)
		{
			Reinitialized.Add(integrationId);
			return Task.CompletedTask;
		}

		public Task ShutdownAsync(string integrationId, CancellationToken cancellationToken = default)
		{
			ShutDown.Add(integrationId);
			OnShutdown?.Invoke();
			return Task.CompletedTask;
		}
	}
}
