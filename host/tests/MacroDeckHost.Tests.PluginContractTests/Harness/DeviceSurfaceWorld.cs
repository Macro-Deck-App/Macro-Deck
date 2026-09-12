using MacroDeck.Localization;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Deck;
using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.Devices.Surfaces;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.PluginContractTests.Harness;

/// <summary>
/// The real host surface pipeline behind whichever <see cref="IDeviceSurfaceProvider" /> a test hands
/// it. Both hosting models are served by exactly this object, which is what makes an in-process and an
/// out-of-process walkthrough comparable at all: nothing above the provider adapter differs.
/// </summary>
internal sealed class DeviceSurfaceWorld : IDisposable
{
	private readonly ServiceProvider _services;

	public DeviceSurfaceWorld(
		IDeviceSurfaceProvider provider,
		string providerId,
		Action<IServiceCollection>? configure = null)
	{
		Provider = provider;
		ProviderId = providerId;
		WidgetStates = new ContractWidgetStates();

		var services = new ServiceCollection();
		services.AddSingleton<IDeviceRepository>(Devices);
		services.AddSingleton<IProfileRegistry>(Profiles);
		services.AddSingleton<IWidgetStateService>(WidgetStates);
		services.AddSingleton<IWidgetIconService>(new ContractWidgetIcons());
		services.AddSingleton<ILabelTextService>(new ContractLabelText());
		services.AddSingleton<ILocalizationResolver>(new LocalizationResolver(new LocalizationCatalogRegistry()));
		services.AddSingleton<IAppPreferenceService>(new ContractAppPreferences());
		services.AddSingleton<IIconPackCache>(new ContractIconPacks());
		services.AddSingleton<DeviceSurfaceBuilder>();
		services
			.AddSingleton<IUiTransportMessageHandler<ExecuteActionButtonTriggerRequest,
				ExecuteActionButtonTriggerResponse>>(Triggers);
		configure?.Invoke(services);
		_services = services.BuildServiceProvider();

		var scopeFactory = _services.GetRequiredService<IServiceScopeFactory>();
		Service = new DeviceSurfaceService(scopeFactory,
			new SingleProviderResolver(provider, providerId),
			Profiles,
			Presence,
			new DeviceInteractionRouter(scopeFactory, new CallbackFakeHostLockState(), Time, Serilog.Core.Logger.None),
			new NoFocusRules(),
			new NoEventBus(),
			new WidgetStateSubscriptionTracker(),
			new LabelSubscriptionTracker(),
			Time,
			Serilog.Core.Logger.None);
	}

	public MacroDeck.Plugin.Testing.ManualTimeProvider Time { get; } = new();

	public ContractProfileRegistry Profiles { get; } = ContractDeck.Registry();

	public ContractDeviceRepository Devices { get; } = new();

	public ContractWidgetStates WidgetStates { get; }

	public RecordingTriggerPipeline Triggers { get; } = new();

	public ProviderDevicePresenceTracker Presence { get; } = new();

	public IDeviceSurfaceProvider Provider { get; }

	public string ProviderId { get; }

	public DeviceSurfaceService Service { get; }

	public async Task<Guid> RegisterAndOpenAsync(string providerDeviceId)
	{
		var deviceId = Guid.NewGuid();
		await Devices.Create(ContractDeck.Device(deviceId, ProviderId, providerDeviceId));
		Presence.Set(deviceId, online: true);
		await Service.OpenAsync(deviceId, ProviderId, providerDeviceId);
		return deviceId;
	}

	public void Dispose()
	{
		Service.Dispose();
		_services.Dispose();
	}

	private sealed class SingleProviderResolver : IDeviceSurfaceProviderResolver
	{
		private readonly IDeviceSurfaceProvider _provider;
		private readonly string _providerId;

		public SingleProviderResolver(IDeviceSurfaceProvider provider, string providerId)
		{
			_provider = provider;
			_providerId = providerId;
		}

		public IDeviceSurfaceProvider? Resolve(string providerId)
			=> string.Equals(providerId, _providerId, StringComparison.Ordinal) ? _provider : null;
	}

	private sealed class NoFocusRules : IApplicationFocusCoordinator
	{
		public Task OnFocusChanged(FocusedApplication app, CancellationToken cancellationToken)
			=> Task.CompletedTask;

		public Task OnFolderReported(Guid deviceId,
			Guid folderId,
			string? navigationToken,
			bool isResync,
			CancellationToken cancellationToken) => Task.CompletedTask;

		public Task OnDevicePresenceChanged(Guid deviceId, bool online, CancellationToken cancellationToken)
			=> Task.CompletedTask;

		public Task OnRulesChanged(CancellationToken cancellationToken) => Task.CompletedTask;
	}

	private sealed class NoEventBus : IEventBus
	{
		private readonly System.Threading.Channels.Channel<EventOccurrence> _channel =
			System.Threading.Channels.Channel.CreateUnbounded<EventOccurrence>();

		public System.Threading.Channels.ChannelReader<EventOccurrence> Reader => _channel.Reader;

		public void Publish(EventOccurrence occurrence) => _channel.Writer.TryWrite(occurrence);
	}
}

/// <summary>The host's trigger pipeline entry point, recording what a device session ran through it.</summary>
internal sealed class RecordingTriggerPipeline
	: IUiTransportMessageHandler<ExecuteActionButtonTriggerRequest, ExecuteActionButtonTriggerResponse>
{
	private readonly List<ExecuteActionButtonTriggerRequest> _requests = [];
	private readonly Lock _sync = new();

	public IReadOnlyList<ExecuteActionButtonTriggerRequest> Requests
	{
		get
		{
			lock (_sync)
			{
				return [.. _requests];
			}
		}
	}

	public ValueTask<ExecuteActionButtonTriggerResponse> Handle(
		ExecuteActionButtonTriggerRequest request,
		CancellationToken cancellationToken)
	{
		lock (_sync)
		{
			_requests.Add(request);
		}

		return ValueTask.FromResult(new ExecuteActionButtonTriggerResponse { Success = true });
	}
}

internal sealed class ContractDeviceRepository : IDeviceRepository
{
	public List<DeviceEntity> Devices { get; } = [];

	public Task<DeviceEntity?> GetById(Guid id) => Task.FromResult(Devices.FirstOrDefault(device => device.Id == id));

	public Task<DeviceEntity?> GetByProviderIdentity(string providerId, string providerDeviceId)
		=> Task.FromResult(Devices.FirstOrDefault(device
			=> device.ProviderId == providerId && device.ProviderDeviceId == providerDeviceId));

	public Task<IReadOnlyList<DeviceEntity>> GetByProviderId(string providerId)
		=> Task.FromResult<IReadOnlyList<DeviceEntity>>([.. Devices.Where(device => device.ProviderId == providerId)]);

	public Task<IReadOnlyList<DeviceEntity>> GetAll() => Task.FromResult<IReadOnlyList<DeviceEntity>>([.. Devices]);

	public Task<IReadOnlyList<DeviceEntity>> GetByStartupProfileId(string profileId)
		=> Task.FromResult<IReadOnlyList<DeviceEntity>>([
			.. Devices.Where(device => device.StartupProfileId == profileId)
		]);

	public Task Create(DeviceEntity device)
	{
		Devices.Add(device);
		return Task.CompletedTask;
	}

	public Task Update(DeviceEntity device)
	{
		var index = Devices.FindIndex(candidate => candidate.Id == device.Id);
		if (index >= 0)
		{
			Devices[index] = device;
		}

		return Task.CompletedTask;
	}

	public Task Delete(Guid id)
	{
		Devices.RemoveAll(device => device.Id == id);
		return Task.CompletedTask;
	}

	public Task TouchLastSeen(IReadOnlyCollection<Guid> ids, DateTime seenAt) => Task.CompletedTask;

	public Task<IReadOnlyList<DeviceEntity>> GetStale(DateTime lastSeenBefore)
		=> Task.FromResult<IReadOnlyList<DeviceEntity>>([]);

	public Task DeleteMany(IReadOnlyCollection<Guid> ids)
	{
		Devices.RemoveAll(device => ids.Contains(device.Id));
		return Task.CompletedTask;
	}
}
