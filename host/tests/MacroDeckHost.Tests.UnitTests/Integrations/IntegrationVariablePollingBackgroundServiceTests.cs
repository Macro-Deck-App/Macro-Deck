using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using VariableReading = MacroDeck.Sdk.Variables.VariableReading;
using SdkVariableType = MacroDeck.Sdk.Variables.VariableType;
using VariableDefinition = MacroDeck.Sdk.Variables.VariableDefinition;

namespace MacroDeckHost.Tests.UnitTests.Integrations;

[TestFixture]
internal sealed class IntegrationVariablePollingBackgroundServiceTests
{
	[Test]
	public async Task A_state_changed_notification_makes_the_next_tick_re_register_the_integration()
	{
		var integration = new FakeVariableProviderIntegration
		{
			Id = "app.test.one", IsInitialized = true,
			Variables = [VariableDefinition.Eager("first", SdkVariableType.Text)]
		};
		var registry = new ConfigurableIntegrationRegistry([integration]);
		var variableService = new RecordingVariableService();
		var invalidation = new VariablePollingInvalidationSignal();
		var service = CreateService(registry, variableService, invalidation);

		await service.DispatchDue(CancellationToken.None);
		string[] first = ["first"];
		Assert.That(variableService.RegisteredNames("app.test.one"), Is.EqualTo(first));

		integration.Variables = [VariableDefinition.Eager("second", SdkVariableType.Text)];
		var handler = new IntegrationStateChangedNotificationHandler(new NoOpWeatherBroadcastTrigger(),
			new NoOpIssueBroadcastTrigger(),
			invalidation,
			new NoOpVariableSubscriptionCoordinator(),
			new NoOpWidgetVariableIndex(),
			new WidgetStateEvalChannel());
		await handler.Handle(new IntegrationStateChangedNotification("app.test.one"), CancellationToken.None);

		await service.DispatchDue(CancellationToken.None);

		Assert.That(variableService.RegisteredNames("app.test.one"), Does.Contain("second"));
	}

	[Test]
	public async Task A_providers_display_name_and_configuration_reach_the_registered_variable()
	{
		var integration = new FakeVariableProviderIntegration
		{
			Id = "app.test.one",
			IsInitialized = true,
			Variables =
			[
				VariableDefinition.Eager("current_scene", SdkVariableType.Text)
					with
					{
						DisplayName = "Current scene",
						Configuration = new MacroDeck.Sdk.Variables.VariableConfiguration("mac", "Mac")
					}
			]
		};
		var registry = new ConfigurableIntegrationRegistry([integration]);
		var variableService = new RecordingVariableService();
		var invalidation = new VariablePollingInvalidationSignal();
		var service = CreateService(registry, variableService, invalidation);

		await service.DispatchDue(CancellationToken.None);

		var presentation = variableService.PresentationByName["current_scene"];
		Assert.That(presentation, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(presentation!.DisplayName.Literal, Is.EqualTo("Current scene"));
			Assert.That(presentation.ConfigurationKey, Is.EqualTo("mac"));
			Assert.That(presentation.ConfigurationName.Literal, Is.EqualTo("Mac"));
		});
	}

	[Test]
	public async Task An_unrelated_integrations_state_change_does_not_evict_this_integrations_cache()
	{
		var integration = new FakeVariableProviderIntegration
		{
			Id = "app.test.one", IsInitialized = true,
			Variables = [VariableDefinition.Eager("first", SdkVariableType.Text)]
		};
		var registry = new ConfigurableIntegrationRegistry([integration]);
		var variableService = new RecordingVariableService();
		var invalidation = new VariablePollingInvalidationSignal();
		var service = CreateService(registry, variableService, invalidation);

		await service.DispatchDue(CancellationToken.None);

		integration.Variables = [VariableDefinition.Eager("second", SdkVariableType.Text)];
		invalidation.MarkStale("app.test.two");

		await service.DispatchDue(CancellationToken.None);

		string[] first = ["first"];
		Assert.That(variableService.RegisteredNames("app.test.one"),
			Is.EqualTo(first),
			"an unrelated integration's invalidation must not evict this one's cached runtime");
	}

	[Test]
	public async Task Re_registration_never_deletes_dynamic_variables_created_by_the_same_provider_integration()
	{
		var integration = new FakeVariableProviderIntegration
		{
			Id = "app.test.one",
			IsInitialized = true,
			Variables = [VariableDefinition.Eager("provided_status", SdkVariableType.Text)]
		};
		var registry = new ConfigurableIntegrationRegistry([integration]);
		var variableService = new RecordingVariableService();
		var dynamic = variableService.SeedDynamic("app.test.one", "pushed_notification");
		var invalidation = new VariablePollingInvalidationSignal();
		var service = CreateService(registry, variableService, invalidation);

		await service.DispatchDue(CancellationToken.None);
		invalidation.MarkStale("app.test.one");
		await service.DispatchDue(CancellationToken.None);

		Assert.That(variableService.Deleted, Does.Not.Contain(dynamic.Id));
	}

	[Test]
	public async Task An_eager_refresh_request_reads_before_the_declared_interval_elapses()
	{
		var integration = CountingIntegration("app.test.one");
		var refresh = new VariableRefreshSignal();
		var service = CreateService(new ConfigurableIntegrationRegistry([integration]),
			new RecordingVariableService(),
			new VariablePollingInvalidationSignal(),
			refresh);

		await DispatchUntilRead(service, integration);

		refresh.RequestEagerRefresh("app.test.one");

		await DispatchUntilRead(service, integration, "a requested refresh must not wait for the declared interval");
	}

	[Test]
	public async Task Without_a_request_a_variable_is_not_read_again_before_its_interval()
	{
		var integration = CountingIntegration("app.test.one");
		var refresh = new VariableRefreshSignal();
		var service = CreateService(new ConfigurableIntegrationRegistry([integration]),
			new RecordingVariableService(),
			new VariablePollingInvalidationSignal(),
			refresh);

		await DispatchUntilRead(service, integration);

		await service.DispatchDue(CancellationToken.None);

		refresh.RequestEagerRefresh("app.test.one");
		await DispatchUntilRead(service, integration);

		Assert.That(integration.ReadCount, Is.EqualTo(2));
	}

	[Test]
	public async Task A_refresh_request_is_one_shot()
	{
		var integration = CountingIntegration("app.test.one");
		var refresh = new VariableRefreshSignal();
		var service = CreateService(new ConfigurableIntegrationRegistry([integration]),
			new RecordingVariableService(),
			new VariablePollingInvalidationSignal(),
			refresh);

		await DispatchUntilRead(service, integration);

		refresh.RequestEagerRefresh("app.test.one");
		await DispatchUntilRead(service, integration);

		await service.DispatchDue(CancellationToken.None);

		refresh.RequestEagerRefresh("app.test.one");
		await DispatchUntilRead(service, integration);

		Assert.That(integration.ReadCount, Is.EqualTo(3));
	}

	[Test]
	public async Task A_request_only_brings_forward_the_integration_it_names()
	{
		var one = CountingIntegration("app.test.one");
		var two = CountingIntegration("app.test.two");
		var refresh = new VariableRefreshSignal();
		var service = CreateService(new ConfigurableIntegrationRegistry([one, two]),
			new RecordingVariableService(),
			new VariablePollingInvalidationSignal(),
			refresh);

		await DispatchUntilRead(service, one, two);

		refresh.RequestEagerRefresh("app.test.one");
		await DispatchUntilRead(service, one);

		Assert.That(two.ReadCount, Is.EqualTo(1));
	}

	private static Task DispatchUntilRead(
		IntegrationVariablePollingBackgroundService service,
		CountingVariableProviderIntegration integration,
		string? because = null)
		=> DispatchUntilRead(service, because, integration);

	private static async Task DispatchUntilRead(
		IntegrationVariablePollingBackgroundService service,
		params CountingVariableProviderIntegration[] integrations)
		=> await DispatchUntilRead(service, null, integrations);

	private static async Task DispatchUntilRead(
		IntegrationVariablePollingBackgroundService service,
		string? because,
		params CountingVariableProviderIntegration[] integrations)
	{
		var before = integrations.Select(integration => integration.ReadCount).ToArray();
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
		while (integrations.Where((integration, i) => integration.ReadCount == before[i]).Any())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail(because ?? "an integration was never read");
			}

			await service.DispatchDue(CancellationToken.None);
			await Task.Delay(5);
		}
	}

	private static CountingVariableProviderIntegration CountingIntegration(string id)
		=> new()
		{
			Id = id,
			IsInitialized = true,
			Variables =
			[
				VariableDefinition.Eager("first", SdkVariableType.Text, refreshInterval: TimeSpan.FromMinutes(5))
			]
		};

	private sealed class CountingVariableProviderIntegration : FakeVariableProviderIntegration
	{
		private int _readCount;

		public int ReadCount => Volatile.Read(ref _readCount);

		public override ValueTask<VariableReading> ReadAsync(
			string localId,
			CancellationToken cancellationToken = default)
		{
			Interlocked.Increment(ref _readCount);
			return ValueTask.FromResult(VariableReading.Of("value"));
		}
	}

	private static IntegrationVariablePollingBackgroundService CreateService(
		ConfigurableIntegrationRegistry registry,
		RecordingVariableService variableService,
		IVariablePollingInvalidationSignal invalidation,
		IVariableRefreshSignal? refresh = null)
	{
		var services = new ServiceCollection();
		services.AddScoped<IVariableService>(_ => variableService);
		var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

		return new IntegrationVariablePollingBackgroundService(new StartedHostLifetime(),
			registry,
			scopeFactory,
			invalidation,
			refresh ?? new VariableRefreshSignal(),
			Log.Logger);
	}

	private sealed class RecordingVariableService : IVariableService
	{
		private readonly Dictionary<string, List<string>> _registeredByIntegration = new(StringComparer.Ordinal);
		private readonly List<VariableEntity> _existing = [];

		public List<Guid> Deleted { get; } = [];

		public Dictionary<string, VariablePresentation?> PresentationByName { get; } = new(StringComparer.Ordinal);

		public List<string> RegisteredNames(string integrationId)
			=> _registeredByIntegration.TryGetValue(integrationId, out var names) ? names : [];

		public VariableEntity SeedDynamic(string integrationId, string name)
		{
			var variable = new VariableEntity
			{
				Id = Guid.NewGuid(),
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow,
				Name = name,
				Scope = VariableScope.Global,
				Type = VariableType.Text,
				Classification = VariableClassification.Integration,
				OwnerIntegrationId = integrationId,
				DefinitionId = "pushed-notification"
			};
			_existing.Add(variable);
			return variable;
		}

		public Task<Result<VariableEntity, VariableError>> CreateIntegrationVariable(
			string integrationId,
			string name,
			VariableScope scope,
			string? scopeRefId,
			VariableType type,
			object? initialValue,
			int? decimalPlaces,
			string? definitionId = null,
			VariableDeclaration? declaration = null,
			VariableUpdateMode updateMode = VariableUpdateMode.Polled)
		{
			if (!_registeredByIntegration.TryGetValue(integrationId, out var names))
			{
				names = [];
				_registeredByIntegration[integrationId] = names;
			}

			names.Add(name);
			PresentationByName[name] = declaration?.Presentation;

			var entity = new VariableEntity
			{
				Id = Guid.CreateVersion7(),
				CreatedAt = DateTime.UtcNow,
				Name = name,
				Scope = scope,
				Type = type,
				Classification = VariableClassification.Integration,
				OwnerIntegrationId = integrationId,
				Presentation = declaration?.Presentation
			};

			return Task.FromResult(Result.Ok<VariableEntity, VariableError>(entity));
		}

		public Task<Result<VariableEntity, VariableError>> ReportIntegrationVariableValue(
			string integrationId,
			Guid id,
			object? value,
			VariableBounds? bounds = null)
			=> throw new NotSupportedException("Not exercised by these tests.");

		public Task<Result<VariableError>> SetIntegrationVariableAvailability(string integrationId,
			Guid id,
			bool available)
			=> Task.FromResult(Result.Ok<VariableError>());

		public Task<IReadOnlyList<VariableEntity>> GetAll() => throw new NotSupportedException();

		public Task<IReadOnlyList<VariableEntity>> GetByScope(VariableScope scope, string? scopeRefId)
			=> throw new NotSupportedException();

		public Task<VariableEntity?> GetById(Guid id) => throw new NotSupportedException();

		public Task<VariableEntity?> Resolve(string name, VariableScope contextScope, string? contextScopeRefId)
			=> throw new NotSupportedException();

		public Task<Result<VariableEntity, VariableError>> CreateUserVariable(
			string name,
			VariableScope scope,
			string? scopeRefId,
			VariableType type,
			object? initialValue,
			int? decimalPlaces)
			=> throw new NotSupportedException();

		public Task<Result<VariableEntity, VariableError>> SetValue(Guid id,
			object? value,
			CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<Result<VariableEntity, VariableError>> UpdateUserVariable(
			Guid id,
			string? name,
			int? decimalPlaces)
			=> throw new NotSupportedException();

		public Task<Result<VariableError>> DeleteUserVariable(Guid id) => throw new NotSupportedException();

		public Task<Result<VariableEntity, VariableError>> MaterializeCatalogVariable(string integrationId,
			string resourceId,
			string name,
			VariableType type,
			int? decimalPlaces,
			VariableDeclaration? declaration = null) =>
			throw new NotSupportedException();

		public Task<VariableEntity?> GetByDefinition(QualifiedId definitionId) => throw new NotSupportedException();

		public Task<Result<VariableError>> DeleteIntegrationVariable(string integrationId, Guid id)
		{
			Deleted.Add(id);
			return Task.FromResult(Result.Ok<VariableError>());
		}

		public Task<IReadOnlyList<VariableEntity>> GetByOwnerIntegration(string integrationId)
			=> Task.FromResult<IReadOnlyList<VariableEntity>>(_existing
				.Where(variable => variable.OwnerIntegrationId == integrationId)
				.ToList());

		public Task DeleteByScopeInstance(VariableScope scope, string scopeRefId) => throw new NotSupportedException();

		public Task UpsertWidgetVariable(VariableScope scope,
			string scopeRefId,
			string name,
			VariableType type,
			object? value)
			=> throw new NotSupportedException();

		public Task RemoveWidgetVariable(VariableScope scope, string scopeRefId, string name)
			=> throw new NotSupportedException();
	}

	private sealed class StartedHostLifetime : IHostApplicationLifetime
	{
		public CancellationToken ApplicationStarted => CancellationToken.None;
		public CancellationToken ApplicationStopping => CancellationToken.None;
		public CancellationToken ApplicationStopped => CancellationToken.None;

		public void StopApplication()
		{
		}
	}

	private sealed class NoOpWeatherBroadcastTrigger : MacroDeckHost.Application.Weather.IWeatherBroadcastTrigger
	{
		public void RequestRefresh()
		{
		}

		public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(false);
	}

	private sealed class
		NoOpIssueBroadcastTrigger : MacroDeckHost.Application.Integrations.IIntegrationIssueBroadcastTrigger
	{
		public void RequestRefresh()
		{
		}

		public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(false);
	}

	private sealed class NoOpVariableSubscriptionCoordinator : IVariableSubscriptionCoordinator
	{
		public Task ReconcileAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public bool IsSubscribed(string integrationId, string localResourceId) => false;
	}

	private sealed class NoOpWidgetVariableIndex : IWidgetVariableIndex
	{
		public IReadOnlyList<Guid> FindLabelReferences(string variableName) => [];

		public IReadOnlyList<Guid> FindStateMappingReferences(string variableName) => [];

		public IReadOnlyList<Guid> FindProviderReferences(string integrationId, string? actionId = null) => [];

		public IReadOnlyList<Guid> FindIconProviderReferences(string integrationId, string? actionId = null) => [];

		public bool LabelReferences(Guid widgetId, string variableName) => false;

		public bool StateMappingReferences(Guid widgetId, string variableName) => false;

		public void Rebuild()
		{
		}

		public void ReindexWidget(Guid widgetId, string type, string? data)
		{
		}

		public void Remove(Guid widgetId)
		{
		}
	}
}
