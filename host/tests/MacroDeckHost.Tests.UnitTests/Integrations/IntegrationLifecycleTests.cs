using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Tests.UnitTests.Plugins;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Triggers;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using Microsoft.Extensions.DependencyInjection;
using DomainVariableType = MacroDeckHost.Domain.Enums.VariableType;
using MacroDeck.Localization;

namespace MacroDeckHost.Tests.UnitTests.Integrations;

[TestFixture]
internal sealed class IntegrationLifecycleTests
{
	private IntegrationRegistry _registry = null!;
	private PluginSessionRegistry _pluginSessionRegistry = null!;
	private FakePluginConnection _connection = null!;
	private IntegrationLifecycle _lifecycle = null!;

	[SetUp]
	public async Task SetUp()
	{
		var services = new ServiceCollection();
		services.AddSingleton<IVariableService>(new ThrowingVariableService());
		services.AddSingleton(TestLocalization.Resolver);
		services.AddSingleton(TestLocalization.Preferences);
		var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

		_registry = new IntegrationRegistry(scopeFactory, new FakeIntegrationStateStore(), Serilog.Log.Logger);
		_pluginSessionRegistry = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);

		var initializer = new IntegrationInitializer(scopeFactory,
			new FakeDeckNavigator(),
			new FakeScriptApi(),
			new FakeWidgetApi(),
			new FakeWidgetIconInvalidator(),
			new FakeUserVariableApi(),
			new RecordingEventBus(),
			new FakeNotificationStore(),
			null!,
			null!,
			new VariableRefreshSignal(),
			new FakeIntegrationHostIssueStore(),
			TestLayoutProviders.Host(),
			TestFolderViewProviders.Host(),
			TestWidgetTypeProviders.Host(),
			TestDeviceProviders.Host(),
			TimeProvider.System,
			Serilog.Log.Logger);

		_lifecycle = new IntegrationLifecycle(_registry,
			scopeFactory,
			new RecordingMediator(),
			_pluginSessionRegistry,
			initializer,
			TestLayoutProviders.Host(),
			TestFolderViewProviders.Host(),
			TestWidgetTypeProviders.Host(),
			TestDeviceProviders.Host(),
			TimeProvider.System,
			Serilog.Log.Logger);

		_connection = new FakePluginConnection();
	}

	private async Task<TrackingIntegration> RegisterAsync(string integrationId, IntegrationOrigin origin)
	{
		var integration = new TrackingIntegration { Id = integrationId };
		await _registry.RegisterAsync(integration, origin);

		var record = new PluginSessionRecord
		{
			SessionId = Guid.CreateVersion7().ToString("D"),
			PluginId = integrationId,
			DisplayName = "Test Plugin",
			Origin = PluginSessionOrigin.Managed,
			NegotiatedVersion = 1,
			Capabilities = new Dictionary<string, CapabilityNegotiationResult>(),
			DeclaredCapabilities = [],
			State = PluginSessionState.Awaiting,
			CreatedAt = DateTimeOffset.UtcNow
		};
		await _pluginSessionRegistry.Create(record);
		_pluginSessionRegistry.TryAttach(record.SessionId, _connection, null);

		return integration;
	}

	[Test]
	public async Task A_plugin_origin_integration_is_pushed_host_state_config_instead_of_reinitializing_in_process()
	{
		var integration = await RegisterAsync("com.example.plugin", IntegrationOrigin.Plugin);

		await _lifecycle.ReinitializeAsync(integration.Id);

		Assert.Multiple(() =>
		{
			Assert.That(integration.InitializeCount, Is.Zero);

			var push = _connection.Sent.SingleOrDefault(envelope => envelope.Type == MessageTypes.HostState);
			Assert.That(push, Is.Not.Null);

			var payload = push!.Payload!.Value.Deserialize<HostStatePayload>(PluginProtocolJson.Options);
			Assert.That(payload!.Api, Is.EqualTo(HostApis.Config));
		});
	}

	[Test]
	public async Task A_built_in_integration_still_reinitializes_in_process_and_gets_no_host_state_push()
	{
		var integration = await RegisterAsync("com.example.builtin", IntegrationOrigin.BuiltIn);

		await _lifecycle.ReinitializeAsync(integration.Id);

		Assert.Multiple(() =>
		{
			Assert.That(integration.ShutdownCount, Is.EqualTo(1));
			Assert.That(integration.InitializeCount, Is.EqualTo(1));
			Assert.That(_connection.Sent.Any(envelope => envelope.Type == MessageTypes.HostState), Is.False);
		});
	}

	private sealed class TrackingIntegration : IIntegration
	{
		public string Id { get; init; } = "test.integration";

		public LocalizedText Name => "Test";

		public string Version => "1.0.0";

		public IReadOnlyList<IActionDefinition> Actions { get; } = [];

		public bool IsInitialized { get; private set; }

		public int InitializeCount { get; private set; }

		public int ShutdownCount { get; private set; }

		public Task InitializeAsync(IIntegrationContext context)
		{
			InitializeCount++;
			IsInitialized = true;
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			ShutdownCount++;
			IsInitialized = false;
			return Task.CompletedTask;
		}
	}

	private sealed class FakeIntegrationStateStore : IIntegrationStateStore
	{
		private Dictionary<string, bool> _states = new();

		public IReadOnlyDictionary<string, bool> Load() => _states;

		public void Save(IReadOnlyDictionary<string, bool> states) => _states = new Dictionary<string, bool>(states);
	}

	private sealed class FakeNotificationStore : IUserNotificationStore
	{
		public int Capacity => 100;

		public UserNotification? Raise(UserNotificationDraft draft) => null;

		public UserNotification? RaiseIfAbsent(UserNotificationDraft draft) => null;

		public IReadOnlyList<UserNotification> Snapshot() => [];

		public bool UpdateProgress(string dedupeKey, UserNotificationProgress progress) => false;

		public bool DismissByKey(string dedupeKey) => false;

		public bool Dismiss(string id) => false;

		public bool Retire(string dedupeKey) => false;

		public bool DismissAll() => false;

		public event Action? Changed
		{
			add { }
			remove { }
		}
	}

	private sealed class FakeDeckNavigator : IDeckNavigator
	{
		public Task ChangeFolderAsync(string folderId,
			string? originClientId = null,
			CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task ChangeProfileAsync(string profileId,
			string? originClientId = null,
			CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task GoToParentAsync(string? originClientId = null, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task GoBackAsync(string? originClientId = null, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public IReadOnlyList<DeckFolder> GetFolders() => [];

		public IReadOnlyList<DeckProfile> GetProfiles() => [];
	}

	private sealed class FakeScriptApi : IScriptApi
	{
		public IReadOnlyList<Script> GetScripts() => [];

		public Task<ActionResult> RunAsync(string scriptId,
			IReadOnlyDictionary<string, object?>? inputs = null,
			string? originClientId = null,
			string? ownerWidgetId = null,
			CancellationToken cancellationToken = default) => ActionResult.SucceededTask;
	}

	private sealed class FakeWidgetApi : IWidgetApi
	{
		public IReadOnlyList<WidgetTargetInfo> GetWidgets() => [];

		public bool Exists(string widgetId) => false;

		public Task<bool> ApplyAsync(WidgetAppearanceRequest request, CancellationToken cancellationToken = default)
			=> Task.FromResult(false);

		public Task<WidgetStateWriteResult> SetStateAsync(
			string widgetId,
			string stateId,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound));

		public Task<WidgetStateWriteResult> AdvanceStateAsync(string widgetId,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound));
	}

	private sealed class FakeWidgetIconInvalidator : IWidgetIconInvalidator
	{
		public void Invalidate(string integrationId, string actionId)
		{
		}
	}

	private sealed class FakeUserVariableApi : IUserVariableApi
	{
		public Task<UserVariableWriteResult> ApplyAsync(string name,
			string? ownerWidgetId,
			UserVariableOperation operation,
			string? value,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(UserVariableWriteResult.Applied());
	}

	private sealed class ThrowingVariableService : IVariableService
	{
		public Task<IReadOnlyList<VariableEntity>> GetAll() => throw new NotSupportedException();

		public Task<IReadOnlyList<VariableEntity>> GetByScope(VariableScope scope, string? scopeRefId)
			=> throw new NotSupportedException();

		public Task<VariableEntity?> GetById(Guid id) => throw new NotSupportedException();

		public Task<VariableEntity?> Resolve(string name, VariableScope contextScope, string? contextScopeRefId)
			=> throw new NotSupportedException();

		public Task<Result<VariableEntity, VariableError>> CreateUserVariable(string name,
			VariableScope scope,
			string? scopeRefId,
			DomainVariableType type,
			object? initialValue,
			int? decimalPlaces)
			=> throw new NotSupportedException();

		public Task<Result<VariableEntity, VariableError>> SetValue(Guid id,
			object? value,
			CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<Result<VariableEntity, VariableError>> UpdateUserVariable(Guid id,
			string? name,
			int? decimalPlaces) => throw new NotSupportedException();

		public Task<Result<VariableError>> DeleteUserVariable(Guid id) => throw new NotSupportedException();

		public Task<Result<VariableEntity, VariableError>> CreateIntegrationVariable(string integrationId,
			string name,
			VariableScope scope,
			string? scopeRefId,
			DomainVariableType type,
			object? initialValue,
			int? decimalPlaces,
			string? definitionId = null,
			VariableDeclaration? declaration = null,
			VariableUpdateMode updateMode = VariableUpdateMode.Polled) => throw new NotSupportedException();

		public Task<Result<VariableEntity, VariableError>> MaterializeCatalogVariable(string integrationId,
			string resourceId,
			string name,
			Domain.Enums.VariableType type,
			int? decimalPlaces,
			VariableDeclaration? declaration = null) =>
			throw new NotSupportedException();

		public Task<VariableEntity?> GetByDefinition(QualifiedId definitionId) => throw new NotSupportedException();

		public Task<Result<VariableEntity, VariableError>> ReportIntegrationVariableValue(string integrationId,
			Guid id,
			object? value,
			VariableBounds? bounds = null) => throw new NotSupportedException();

		public Task<Result<VariableError>> SetIntegrationVariableAvailability(string integrationId,
			Guid id,
			bool available) => throw new NotSupportedException();

		public Task<Result<VariableError>> DeleteIntegrationVariable(string integrationId, Guid id)
			=> throw new NotSupportedException();

		public Task<IReadOnlyList<VariableEntity>> GetByOwnerIntegration(string integrationId)
			=> throw new NotSupportedException();

		public Task DeleteByScopeInstance(VariableScope scope, string scopeRefId) => throw new NotSupportedException();

		public Task UpsertWidgetVariable(VariableScope scope,
			string scopeRefId,
			string name,
			DomainVariableType type,
			object? value) => throw new NotSupportedException();

		public Task RemoveWidgetVariable(VariableScope scope, string scopeRefId, string name)
			=> throw new NotSupportedException();
	}
}
