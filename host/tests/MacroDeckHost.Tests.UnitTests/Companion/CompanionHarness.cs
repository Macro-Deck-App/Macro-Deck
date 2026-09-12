using System.Text.Json;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Devices;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Integrations;
using MacroDeckHost.Integrations.Companion;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.Companion;

internal sealed class CompanionHarness
{
	public const string IntegrationId = CompanionIntegration.IntegrationId;

	public CompanionHarness(bool storedOff = false, Func<int>? suffixFactory = null, bool register = true)
	{
		var services = new ServiceCollection();
		services.AddSingleton<IIntegrationConfigStore>(Store);
		services.AddSingleton<IDeviceRepository>(Devices);
		services.AddSingleton<ISecretService>(new FakeSecretService());
		services.AddScoped<IVariableService>(_ => Variables!);
		services.AddSingleton(TestLocalization.Resolver);
		services.AddSingleton(TestLocalization.Preferences);
		services.AddScoped<IUiTransportMessageHandler<SetIntegrationEnabledRequest, SetIntegrationEnabledResponse>>(_ =>
			new SetIntegrationEnabledRequestMessageHandler(Registry!,
				new InitializingLifecycle(this),
				new RecordingMediator()));
		ScopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

		StateStore = new MemoryStateStore(storedOff ? new Dictionary<string, bool> { [IntegrationId] = false } : []);
		Registry = new IntegrationRegistry(ScopeFactory, StateStore, Logger);
		Variables = new VariableService(VariableRegistry,
			new NullUserVariableStore(),
			new RecordingMediator(),
			new VariableCatalogProviders(Registry),
			RefreshSignal,
			new MusicPlayerPollNudge(Registry));
		Adapter = new CompanionConfigurationMutationAdapter(Registry, VariableRegistry, ScopeFactory, suffixFactory);
		Coordinator = new GatedCoordinator(new IntegrationConfigMutationCoordinator(ScopeFactory,
			new InitializingLifecycle(this),
			Registry,
			new RecordingMediator(),
			new VariablePollingInvalidationSignal(),
			[Adapter],
			Logger));
		DeviceRegistry = new CompanionDeviceRegistry(ScopeFactory, () => Coordinator, Transport, Registry, Logger);
		Context = new IntegrationContext(null!,
			null!,
			new IntegrationConfig(IntegrationId, ScopeFactory),
			null!,
			null!,
			null!,
			null!,
			null!);

		if (register)
		{
			Registry.RegisterAsync(Integration).GetAwaiter().GetResult();
			Integration.UseGateway(DeviceRegistry);
			Integration.UseVariableRefreshSignal(RefreshSignal);
		}
	}

	public static ILogger Logger { get; } = new LoggerConfiguration().CreateLogger();

	public MemoryConfigStore Store { get; } = new();
	public InMemoryDeviceRepository Devices { get; } = new();
	public MemoryStateStore StateStore { get; }
	public IServiceScopeFactory ScopeFactory { get; }
	public IntegrationRegistry Registry { get; }
	public CompanionIntegration Integration { get; } = new();
	public VariableRegistry VariableRegistry { get; } = new();
	public VariableRefreshSignal RefreshSignal { get; } = new();
	public VariableService Variables { get; }
	public CompanionConfigurationMutationAdapter Adapter { get; }
	public GatedCoordinator Coordinator { get; }
	public RecordingUiTransport Transport { get; } = new();
	public CompanionDeviceRegistry DeviceRegistry { get; }
	public IntegrationContext Context { get; }

	public IReadOnlyList<ConfigEntryRecord> Entries
		=> Store.Entries.Where(entry => entry.IntegrationId == IntegrationId).ToList();

	public Guid AddDevice(string name, DeviceClientType clientType = DeviceClientType.Native)
	{
		var id = Guid.NewGuid();
		Devices.Devices.Add(new DeviceEntity { Id = id, SecretHash = "hash", Name = name, ClientType = clientType });
		return id;
	}

	public void RemoveDeviceRow(Guid deviceId) => Devices.Devices.RemoveAll(device => device.Id == deviceId);

	public async Task ReportAsync(string connectionId, Guid deviceId, ReportCompanionStateRequest? report = null)
	{
		DeviceRegistry.Report(connectionId, deviceId, report ?? Report());
		await DeviceRegistry.CreationFor(deviceId).WaitAsync(TimeSpan.FromSeconds(5));
	}

	public async Task<VariableReading> ReadAsync(Guid entryId, string slot)
		=> await Integration.ReadAsync($"entry-{entryId:N}-{slot.Replace('_', '-')}");

	public static ReportCompanionStateRequest Report(string appVersion = "1.0.0")
		=> new()
		{
			BatteryLevelPercent = 80,
			Charging = true,
			Orientation = "portrait",
			ScreenBrightnessPercent = 50,
			Model = "Pixel 8",
			Platform = "Android 14",
			AppVersion = appVersion
		};

	internal sealed class GatedCoordinator : IIntegrationConfigMutationCoordinator
	{
		public GatedCoordinator(IIntegrationConfigMutationCoordinator inner)
		{
			Inner = inner;
		}

		public IIntegrationConfigMutationCoordinator Inner { get; }

		public Func<Task>? BeforeComplete { get; set; }

		public Func<Task>? AfterDelete { get; set; }

		public Exception? DeleteFailure { get; set; }

		public async Task<IntegrationConfigMutationOutcome> CompleteAsync(string integrationId,
			Guid entryId,
			string title,
			IReadOnlyDictionary<string, JsonElement> values,
			CancellationToken cancellationToken)
		{
			if (BeforeComplete is { } hook)
			{
				await hook();
			}

			return await Inner.CompleteAsync(integrationId, entryId, title, values, cancellationToken);
		}

		public Func<Task>? AfterComplete { get; set; }

		public bool CreatesEntriesFromFlow(string integrationId) => Inner.CreatesEntriesFromFlow(integrationId);

		public async Task<IntegrationConfigMutationOutcome> CompleteUnlessDisabledAsync(string integrationId,
			Guid entryId,
			string title,
			IReadOnlyDictionary<string, JsonElement> values,
			CancellationToken cancellationToken)
		{
			if (BeforeComplete is { } before)
			{
				await before();
			}

			var outcome = await Inner.CompleteUnlessDisabledAsync(integrationId,
				entryId,
				title,
				values,
				cancellationToken);
			if (AfterComplete is { } after)
			{
				await after();
			}

			return outcome;
		}

		public Task<IntegrationConfigMutationOutcome> RenameAsync(string integrationId,
			Guid entryId,
			string title,
			CancellationToken cancellationToken)
			=> Inner.RenameAsync(integrationId, entryId, title, cancellationToken);

		public Task<IntegrationConfigMutationOutcome> DeleteAsync(string integrationId,
			Guid entryId,
			bool confirmed,
			CancellationToken cancellationToken)
			=> Hooked(() => Inner.DeleteAsync(integrationId, entryId, confirmed, cancellationToken));

		public Task<IntegrationConfigMutationOutcome> DeleteWithoutStoringDisabledAsync(string integrationId,
			Guid entryId,
			CancellationToken cancellationToken)
			=> Hooked(() => Inner.DeleteWithoutStoringDisabledAsync(integrationId, entryId, cancellationToken));

		private async Task<IntegrationConfigMutationOutcome> Hooked(Func<Task<IntegrationConfigMutationOutcome>> delete)
		{
			if (DeleteFailure is { } failure)
			{
				throw failure;
			}

			var outcome = await delete();
			if (AfterDelete is { } hook)
			{
				await hook();
			}

			return outcome;
		}

		public Task<IReadOnlyList<IntegrationConfigEntryDescription>> DescribeAsync(string integrationId,
			CancellationToken cancellationToken)
			=> Inner.DescribeAsync(integrationId, cancellationToken);
	}

	internal sealed class MemoryStateStore : IIntegrationStateStore
	{
		public MemoryStateStore(IReadOnlyDictionary<string, bool> initial)
		{
			States = new Dictionary<string, bool>(initial);
		}

		public Dictionary<string, bool> States { get; private set; }

		public IReadOnlyDictionary<string, bool> Load() => States;

		public void Save(IReadOnlyDictionary<string, bool> states) => States = new Dictionary<string, bool>(states);
	}

	internal sealed class MemoryConfigStore : IIntegrationConfigStore
	{
		private readonly Lock _gate = new();

		public List<ConfigEntryRecord> Entries { get; } = [];

		public Task<IReadOnlyList<ConfigEntrySummary>> List(string integrationId)
		{
			lock (_gate)
			{
				return Task.FromResult<IReadOnlyList<ConfigEntrySummary>>(Entries
					.Where(entry => entry.IntegrationId == integrationId)
					.Select(entry =>
						new ConfigEntrySummary(entry.Id, entry.IntegrationId, entry.Title, entry.CreatedAt))
					.ToList());
			}
		}

		public Task<ConfigEntryRecord?> Find(Guid entryId)
		{
			lock (_gate)
			{
				return Task.FromResult(Entries.FirstOrDefault(entry => entry.Id == entryId));
			}
		}

		public Task<Guid> Create(string integrationId, string title, IReadOnlyDictionary<string, JsonElement> values)
			=> throw new NotSupportedException();

		public Task<bool> Create(Guid entryId,
			string integrationId,
			string title,
			IReadOnlyDictionary<string, JsonElement> values)
		{
			lock (_gate)
			{
				if (Entries.Any(entry => entry.Id == entryId))
				{
					return Task.FromResult(false);
				}

				Entries.Add(new ConfigEntryRecord(entryId, integrationId, title, DateTime.UtcNow, values));
				return Task.FromResult(true);
			}
		}

		public Task<bool> UpdateValues(Guid entryId, IReadOnlyDictionary<string, JsonElement> values)
			=> throw new NotSupportedException();

		public Task<bool> Replace(Guid entryId, string title, IReadOnlyDictionary<string, JsonElement> values)
		{
			lock (_gate)
			{
				var index = Entries.FindIndex(entry => entry.Id == entryId);
				if (index < 0)
				{
					return Task.FromResult(false);
				}

				Entries[index] = Entries[index] with { Title = title, Values = values };
				return Task.FromResult(true);
			}
		}

		public Task Delete(Guid entryId)
		{
			lock (_gate)
			{
				Entries.RemoveAll(entry => entry.Id == entryId);
				return Task.CompletedTask;
			}
		}
	}

	internal sealed class CapturingSink : ILogEventSink
	{
		public List<LogEvent> Events { get; } = [];

		public void Emit(LogEvent logEvent) => Events.Add(logEvent);
	}

	private sealed class InitializingLifecycle : IIntegrationLifecycle
	{
		private readonly CompanionHarness _harness;

		public InitializingLifecycle(CompanionHarness harness)
		{
			_harness = harness;
		}

		public async Task ReinitializeAsync(string integrationId, CancellationToken cancellationToken = default)
		{
			await _harness.Integration.ShutdownAsync();
			await _harness.Integration.InitializeAsync(_harness.Context);
		}

		public Task ShutdownAsync(string integrationId, CancellationToken cancellationToken = default)
			=> _harness.Integration.ShutdownAsync();
	}

	private sealed class NullUserVariableStore : IUserVariableStore
	{
		public IReadOnlyList<VariableEntity> Load() => [];

		public void Save(IEnumerable<VariableEntity> userVariables)
		{
		}
	}
}
