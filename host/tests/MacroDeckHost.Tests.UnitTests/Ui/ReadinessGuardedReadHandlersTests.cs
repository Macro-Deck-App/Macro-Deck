using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Devices;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;
using MacroDeckHost.Application.Ui.Transport.Messages.Profiles;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.Profiles;

namespace MacroDeckHost.Tests.UnitTests.Ui;

[TestFixture]
public class ReadinessGuardedReadHandlersTests
{
	private static StartupReadiness Ready()
	{
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		return readiness;
	}

	[Test]
	public async Task GetProfiles_waits_for_startup_readiness()
	{
		var readiness = new StartupReadiness();
		var handler = new GetProfilesRequestMessageHandler(new StubProfileRegistry(), readiness);

		var pending = handler.Handle(new GetProfilesRequest(), CancellationToken.None);
		Assert.That(pending.IsCompleted, Is.False);

		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();

		var response = await pending;
		Assert.That(response.Profiles, Has.Count.EqualTo(1));
		Assert.That(response.Profiles[0].Name, Is.EqualTo("Default"));
	}

	[Test]
	public async Task GetProfiles_returns_immediately_when_ready()
	{
		var handler = new GetProfilesRequestMessageHandler(new StubProfileRegistry(), Ready());

		var response = await handler.Handle(new GetProfilesRequest(), CancellationToken.None);

		Assert.That(response.Profiles, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task GetFolders_waits_for_startup_readiness()
	{
		var readiness = new StartupReadiness();
		var handler = new GetFoldersRequestMessageHandler(new StubFolderCache(), new StubProfileRegistry(), readiness);

		var pending = handler.Handle(new GetFoldersRequest(), CancellationToken.None);
		Assert.That(pending.IsCompleted, Is.False);

		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();

		var response = await pending;
		Assert.That(response.Folders, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task GetDevices_waits_for_startup_readiness()
	{
		var readiness = new StartupReadiness();
		var handler = new GetDevicesRequestMessageHandler(new StubDeviceService(), readiness);

		var pending = handler.Handle(new GetDevicesRequest(), CancellationToken.None);
		Assert.That(pending.IsCompleted, Is.False);

		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();

		var response = await pending;
		Assert.That(response.Devices, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task GetVariables_waits_for_startup_readiness()
	{
		var readiness = new StartupReadiness();
		var handler = new GetVariablesRequestMessageHandler(new StubVariableService(),
			new VariableRegistry(),
			new FakeVariableBindingService(),
			readiness);

		var pending = handler.Handle(new GetVariablesRequest(), CancellationToken.None);
		Assert.That(pending.IsCompleted, Is.False);

		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();

		var response = await pending;
		Assert.That(response.Variables, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task GetIntegrationCapabilities_does_not_wait_for_readiness_when_the_integration_is_inactive()
	{
		var integration = new FakeVariableProviderIntegration { Id = "inactive", IsInitialized = false };
		var registry = new ConfigurableIntegrationRegistry([integration]);
		var readiness = new StartupReadiness();
		var handler = new GetIntegrationCapabilitiesRequestMessageHandler(registry,
			new FakeIntegrationConfigStore(),
			new VariableRegistry(),
			readiness,
			TestLocalization.Preferences,
			TestLocalization.Resolver);

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
		var response = await handler.Handle(new GetIntegrationCapabilitiesRequest { IntegrationId = integration.Id },
			cts.Token);

		Assert.That(response.Found, Is.True);
	}

	[Test]
	public async Task GetIntegrationCapabilities_waits_for_readiness_when_the_integration_is_active()
	{
		var integration = new FakeVariableProviderIntegration { Id = "active", IsInitialized = true };
		var registry = new ConfigurableIntegrationRegistry([integration]);
		var readiness = new StartupReadiness();
		var handler = new GetIntegrationCapabilitiesRequestMessageHandler(registry,
			new FakeIntegrationConfigStore(),
			new VariableRegistry(),
			readiness,
			TestLocalization.Preferences,
			TestLocalization.Resolver);

		var pending = handler.Handle(new GetIntegrationCapabilitiesRequest { IntegrationId = integration.Id },
			CancellationToken.None);
		Assert.That(pending.IsCompleted, Is.False);

		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();

		var response = await pending;
		Assert.That(response.Found, Is.True);
	}

	[Test]
	public void Guarded_read_is_cancellable_while_waiting()
	{
		var readiness = new StartupReadiness();
		var handler = new GetProfilesRequestMessageHandler(new StubProfileRegistry(), readiness);
		using var cancellation = new CancellationTokenSource();

		var pending = handler.Handle(new GetProfilesRequest(), cancellation.Token);
		cancellation.Cancel();

		Assert.ThrowsAsync<TaskCanceledException>(async () => await pending);
	}

	private sealed class StubProfileRegistry : IProfileRegistry
	{
		public IReadOnlyList<Profile> GetProfiles() => [new() { Id = "p1", Name = "Default" }];

		public IReadOnlyList<Folder> GetFoldersForProfile(string profileId) => [];

		public bool IsVirtual(string profileId) => false;

		public Task<bool> RouteWidgetInteraction(string folderId, string widgetId, WidgetInteraction interaction) =>
			Task.FromResult(false);
	}

	private sealed class StubDeviceService : IDeviceService
	{
		public Task<DeviceRegistrationResult> RegisterOrReuse(DeviceRegistration registration, DateTime now) =>
			throw new NotSupportedException();

		public Task PurgeStale(DateTime now) => throw new NotSupportedException();

		public Task<IReadOnlyList<Device>> GetAll() =>
			Task.FromResult<IReadOnlyList<Device>>([new Device { Id = Guid.NewGuid().ToString(), Name = "Device" }]);

		public Task<Device> ToDto(DeviceEntity device) => throw new NotSupportedException();

		public Task<Result<DeviceEntity, DeviceError>> Rename(Guid id, string name)
			=> throw new NotSupportedException();

		public Task<Result<DeviceError>> LogoutDevice(Guid id) => throw new NotSupportedException();

		public Task<Result<DeviceError>> RemoveDevice(Guid id) => throw new NotSupportedException();

		public Task<Result<DeviceEntity, DeviceError>> SetStartupProfile(Guid id, string? profileId) =>
			throw new NotSupportedException();

		public Task<string?> ResolveStartupProfileId(Guid deviceId) => throw new NotSupportedException();

		public Task<Result<DeviceError>> OpenProfileOnDevice(Guid id, string profileId) =>
			throw new NotSupportedException();

		public Task ClearStartupProfileAssignments(string profileId) => throw new NotSupportedException();
	}

	private sealed class StubFolderCache : IFolderCache
	{
		public Task InitializeCache() => Task.CompletedTask;

		public FolderEntity? GetFolderById(Guid id) => null;

		public List<FolderEntity> GetAllFolders() =>
			[new() { Id = Guid.NewGuid(), Name = "Main", Order = 0 }];

		public List<FolderEntity> GetFoldersByParentId(Guid? parentId) => [];

		public List<FolderEntity> GetFoldersByProfileId(Guid profileId) => [];

		public Task AddOrUpdate(FolderEntity folder) => Task.CompletedTask;
		public Task AddOrUpdateRange(IReadOnlyCollection<FolderEntity> folders) => Task.CompletedTask;

		public Task<FolderSubtreeRemoval> RemoveSubtree(Guid rootId)
			=> Task.FromResult(new FolderSubtreeRemoval(false, [], []));

		public void AddWidget(Guid folderId, WidgetEntity widget)
		{
		}

		public void AddWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets)
		{
		}

		public void UpdateWidget(Guid folderId, WidgetEntity widget)
		{
		}

		public void UpdateWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets)
		{
		}

		public void UpdateWidgetPositions(Guid folderId, IReadOnlyList<WidgetPlacement> placements)
		{
		}

		public void RemoveWidget(Guid folderId, Guid widgetId)
		{
		}

		public void RemoveWidgets(Guid folderId, IReadOnlyList<Guid> widgetIds)
		{
		}

		public void ReplaceWidgets(Guid folderId, IReadOnlyList<Guid> removeIds, IReadOnlyList<WidgetEntity> addWidgets)
		{
		}
	}

	private sealed class StubVariableService : IVariableService
	{
		private static VariableEntity Variable() => new()
		{
			Id = Guid.NewGuid(),
			Name = "cpu",
			Scope = VariableScope.Global,
			Type = VariableType.Numeric,
			Classification = VariableClassification.User,
			Value = "1"
		};

		public Task<IReadOnlyList<VariableEntity>> GetAll() =>
			Task.FromResult<IReadOnlyList<VariableEntity>>([Variable()]);

		public Task<IReadOnlyList<VariableEntity>> GetByScope(VariableScope scope, string? scopeRefId) =>
			Task.FromResult<IReadOnlyList<VariableEntity>>([]);

		public Task<VariableEntity?> GetById(Guid id) => Task.FromResult<VariableEntity?>(null);

		public Task<VariableEntity?> Resolve(string name, VariableScope contextScope, string? contextScopeRefId) =>
			Task.FromResult<VariableEntity?>(null);

		public Task<Result<VariableEntity, VariableError>> CreateUserVariable(string name,
			VariableScope scope,
			string? scopeRefId,
			VariableType type,
			object? initialValue,
			int? decimalPlaces) =>
			throw new NotSupportedException();

		public Task<Result<VariableEntity, VariableError>> SetValue(Guid id,
			object? value,
			CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<Result<VariableEntity, VariableError>> UpdateUserVariable(Guid id,
			string? name,
			int? decimalPlaces) =>
			throw new NotSupportedException();

		public Task<Result<VariableError>> DeleteUserVariable(Guid id) => throw new NotSupportedException();

		public Task<Result<VariableEntity, VariableError>> CreateIntegrationVariable(string integrationId,
			string name,
			VariableScope scope,
			string? scopeRefId,
			VariableType type,
			object? initialValue,
			int? decimalPlaces,
			string? definitionId = null,
			VariableDeclaration? declaration = null,
			VariableUpdateMode updateMode = VariableUpdateMode.Polled) =>
			throw new NotSupportedException();

		public Task<Result<VariableEntity, VariableError>> MaterializeCatalogVariable(string integrationId,
			string resourceId,
			string name,
			VariableType type,
			int? decimalPlaces,
			VariableDeclaration? declaration = null) =>
			throw new NotSupportedException();

		public Task<VariableEntity?> GetByDefinition(QualifiedId definitionId) =>
			Task.FromResult<VariableEntity?>(null);

		public Task<Result<VariableEntity, VariableError>> ReportIntegrationVariableValue(string integrationId,
			Guid id,
			object? value,
			VariableBounds? bounds = null) =>
			throw new NotSupportedException();

		public Task<Result<VariableError>> SetIntegrationVariableAvailability(string integrationId,
			Guid id,
			bool available) =>
			throw new NotSupportedException();

		public Task<Result<VariableError>> DeleteIntegrationVariable(string integrationId, Guid id) =>
			throw new NotSupportedException();

		public Task<IReadOnlyList<VariableEntity>> GetByOwnerIntegration(string integrationId) =>
			Task.FromResult<IReadOnlyList<VariableEntity>>([]);

		public Task DeleteByScopeInstance(VariableScope scope, string scopeRefId) => Task.CompletedTask;

		public Task UpsertWidgetVariable(VariableScope scope,
			string scopeRefId,
			string name,
			VariableType type,
			object? value) =>
			Task.CompletedTask;

		public Task RemoveWidgetVariable(VariableScope scope, string scopeRefId, string name) => Task.CompletedTask;
	}
}
