using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Variables.Files;
using MacroDeckHost.Application.Variables.Templates;
using System.Text.Json;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Application.Deck;
using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Network.Discovery;
using MacroDeckHost.Application.Network.Tls;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins;
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
using MacroDeckHost.Infrastructure.Variables;
using MacroDeckHost.Integrations;
using MacroDeckHost.Integrations.Companion;
using MacroDeckHost.Tests.UnitTests.Adb;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Tests.UnitTests.Triggers;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.Companion;

internal sealed class CompanionHarness
{
	public const string IntegrationId = CompanionIntegration.IntegrationId;

	public CompanionHarness(
		bool storedOff = false,
		Func<int>? suffixFactory = null,
		bool register = true,
		IPluginCompanionDeviceAccessPolicy? pluginAccess = null,
		bool builtInsRegistered = true)
	{
		if (builtInsRegistered)
		{
			BuiltInRegistration.Complete();
		}

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
			new MusicPlayerPollNudge(Registry),
			new FileVariableSynchronizer(VariableRegistry,
				new RecordingMediator(),
				new FakeVariableFileSystem(),
				Serilog.Core.Logger.None),
			new TemplateVariableSynchronizer(VariableRegistry, new RecordingMediator()),
			Serilog.Core.Logger.None);
		Integration = new CompanionIntegration(suffixFactory);
		Coordinator = new GatedCoordinator(new IntegrationConfigMutationCoordinator(ScopeFactory,
			new InitializingLifecycle(this),
			Registry,
			new RecordingMediator(),
			new VariablePollingInvalidationSignal(),
			Logger,
			VariableRegistry,
			new IntegrationVariableApiFactory()));
		Requests = new CompanionCommandRequests(Time);
		DeviceRegistry = new CompanionDeviceRegistry(ScopeFactory,
			() => Coordinator,
			Transport,
			Registry,
			BuiltInRegistration,
			Requests,
			Interfaces,
			HostNames,
			Connections,
			Time,
			new LoggerConfiguration().WriteTo.Sink(Sink).CreateLogger(),
			pluginAccess);
		UserVariables = new UserVariableWriter(ScopeFactory);
		Context = new IntegrationContext(VariableApi,
			UserVariables,
			new IntegrationConfig(IntegrationId, ScopeFactory),
			null!,
			null!,
			null!,
			Events,
			null!,
			null!,
			null!,
			UnavailableNotificationService.Instance,
			companionDevices: DeviceRegistry);

		if (register)
		{
			Registry.RegisterAsync(Integration).GetAwaiter().GetResult();
		}
	}

	public static ILogger Logger { get; } = new LoggerConfiguration().CreateLogger();

	public MemoryConfigStore Store { get; } = new();
	public InMemoryDeviceRepository Devices { get; } = new();
	public MemoryStateStore StateStore { get; }
	public IServiceScopeFactory ScopeFactory { get; }
	public IntegrationRegistry Registry { get; }
	public BuiltInIntegrationRegistration BuiltInRegistration { get; } = new();
	public CompanionIntegration Integration { get; }
	public VariableRegistry VariableRegistry { get; } = new();
	public VariableRefreshSignal RefreshSignal { get; } = new();
	public VariableService Variables { get; }
	public GatedCoordinator Coordinator { get; }
	public RecordingUiTransport Transport { get; } = new();
	public FixedInterfaces Interfaces { get; } = new();
	public FixedHostNames HostNames { get; } = new();
	public CapturingSink Sink { get; } = new();
	public FakeTimeProvider Time { get; } = new();
	public CompanionCommandRequests Requests { get; }
	public RecordingPublisher Events { get; } = new();

	public DeviceConnectionTracker Connections { get; } = new(new RecordingEventBus(),
		TimeProvider.System,
		new DeckClientTracker(Serilog.Core.Logger.None));

	public RecordingVariableApi VariableApi { get; } = new();
	public UserVariableWriter UserVariables { get; }
	public CompanionDeviceRegistry DeviceRegistry { get; }
	public IntegrationContext Context { get; }

	public IReadOnlyList<ConfigEntryRecord> Entries
		=> Store.Entries.Where(entry => entry.IntegrationId == IntegrationId).ToList();

	public Guid AddDevice(
		string name,
		DeviceClientType clientType = DeviceClientType.Native,
		DeviceFormFactor formFactor = DeviceFormFactor.Unknown)
	{
		var id = Guid.NewGuid();
		Devices.Devices.Add(new DeviceEntity
			{ Id = id, SecretHash = "hash", Name = name, ClientType = clientType, FormFactor = formFactor });
		return id;
	}

	public void RemoveDeviceRow(Guid deviceId)
	{
		Devices.Devices.RemoveAll(device => device.Id == deviceId);
	}

	public async Task ReportAsync(string connectionId, Guid deviceId, ReportCompanionStateRequest? report = null)
	{
		await DeviceRegistry.ReportAsync(connectionId, deviceId, report ?? Report());
		await DeviceRegistry.CreationFor(deviceId).WaitAsync(TimeSpan.FromSeconds(5));
		await DeviceRegistry.WakeOnLanSent.WaitAsync(TimeSpan.FromSeconds(5));
	}

	public async Task<VariableReading> ReadAsync(Guid entryId, string slot)
	{
		return await Integration.ReadAsync($"entry-{entryId:N}-{slot.Replace('_', '-')}");
	}

	public static ReportCompanionStateRequest Report(string appVersion = "1.0.0", params string[] capabilities)
	{
		return new ReportCompanionStateRequest
		{
			Capabilities = [.. capabilities],
			BatteryLevelPercent = 80,
			Charging = true,
			Orientation = "portrait",
			ScreenBrightnessPercent = 50,
			Model = "Pixel 8",
			Platform = "Android 14",
			AppVersion = appVersion
		};
	}

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

		public async Task<IntegrationConfigMutationOutcome> CompleteAsync(
			string integrationId,
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

		public async Task<IntegrationConfigMutationOutcome> CompleteUnlessDisabledAsync(
			string integrationId,
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

		public Task<IntegrationConfigMutationOutcome> RenameAsync(
			string integrationId,
			Guid entryId,
			string title,
			CancellationToken cancellationToken)
		{
			return Inner.RenameAsync(integrationId, entryId, title, cancellationToken);
		}

		public Task<IntegrationConfigMutationOutcome> DeleteAsync(
			string integrationId,
			Guid entryId,
			bool confirmed,
			CancellationToken cancellationToken)
		{
			return Hooked(() => Inner.DeleteAsync(integrationId, entryId, confirmed, cancellationToken));
		}

		public Task<IntegrationConfigMutationOutcome> DeleteWithoutStoringDisabledAsync(
			string integrationId,
			Guid entryId,
			CancellationToken cancellationToken)
		{
			return Hooked(() => Inner.DeleteWithoutStoringDisabledAsync(integrationId, entryId, cancellationToken));
		}

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

		public Task<IReadOnlyList<IntegrationConfigEntryDescription>> DescribeAsync(
			string integrationId,
			CancellationToken cancellationToken)
		{
			return Inner.DescribeAsync(integrationId, cancellationToken);
		}
	}

	internal sealed class FixedInterfaces : INetworkInterfaceSnapshotProvider
	{
		public List<NetworkInterfaceSnapshot> Interfaces { get; } = [];

		public IReadOnlyList<NetworkInterfaceSnapshot> GetInterfaces()
		{
			return Interfaces;
		}
	}

	internal sealed class FixedHostNames : IHostNameProvider
	{
		public string MachineName => "studio-pc";

		public IReadOnlyList<string> GetHostNames()
		{
			return [MachineName];
		}
	}

	internal sealed class MemoryStateStore : IIntegrationStateStore
	{
		public MemoryStateStore(IReadOnlyDictionary<string, bool> initial)
		{
			States = new Dictionary<string, bool>(initial);
		}

		public Dictionary<string, bool> States { get; private set; }

		public IReadOnlyDictionary<string, bool> Load()
		{
			return States;
		}

		public void Save(IReadOnlyDictionary<string, bool> states)
		{
			States = new Dictionary<string, bool>(states);
		}
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
		{
			throw new NotSupportedException();
		}

		public Task<bool> Create(
			Guid entryId,
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
		{
			throw new NotSupportedException();
		}

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

		public void Emit(LogEvent logEvent)
		{
			Events.Add(logEvent);
		}
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
		{
			return _harness.Integration.ShutdownAsync();
		}
	}

	private sealed class NullUserVariableStore : IUserVariableStore
	{
		public IReadOnlyList<VariableEntity> Load()
		{
			return [];
		}

		public void Save(IEnumerable<VariableEntity> userVariables)
		{
		}
	}
}
