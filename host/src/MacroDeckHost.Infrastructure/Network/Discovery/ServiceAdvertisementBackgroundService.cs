using System.Net.NetworkInformation;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Network.Discovery;
using MacroDeckHost.Application.Network.Tls;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.BackgroundServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Infrastructure.Network.Discovery;

public sealed class ServiceAdvertisementBackgroundService : HostReadyBackgroundService, IDiscoveryAdvertisementRefresher
{
	private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(2);
	private static readonly TimeSpan RecheckInterval = TimeSpan.FromSeconds(60);

	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IServiceAdvertiser _advertiser;
	private readonly IHostNameProvider _hostNames;
	private readonly IHostListenerState _listenerState;
	private readonly INetworkInterfaceSnapshotProvider _interfaces;
	private readonly ILogger _logger = Log.ForContext<ServiceAdvertisementBackgroundService>();
	private readonly SemaphoreSlim _wake = new(0, 1);
	private readonly SemaphoreSlim _gate = new(1, 1);
	private ServiceAdvertisement? _published;
	private bool _advertised;
	private string? _lastFailure;

	public ServiceAdvertisementBackgroundService(IHostApplicationLifetime lifetime,
		IServiceScopeFactory scopeFactory,
		IServiceAdvertiser advertiser,
		IHostNameProvider hostNames,
		IHostListenerState listenerState,
		INetworkInterfaceSnapshotProvider interfaces)
		: base(lifetime)
	{
		_scopeFactory = scopeFactory;
		_advertiser = advertiser;
		_hostNames = hostNames;
		_listenerState = listenerState;
		_interfaces = interfaces;
	}

	public void RequestRefresh()
	{
		try
		{
			_wake.Release();
		}
		catch (SemaphoreFullException)
		{
		}
	}

	public override async Task StopAsync(CancellationToken cancellationToken)
	{
		await base.StopAsync(cancellationToken);

		// The host's shutdown timeout can end the wait above while a reconcile is still running.
		await _gate.WaitAsync(CancellationToken.None);
		try
		{
			Withdraw();
		}
		finally
		{
			_gate.Release();
		}
	}

	public override void Dispose()
	{
		_wake.Dispose();
		_gate.Dispose();
		base.Dispose();
	}

	internal async Task Reconcile()
	{
		await _gate.WaitAsync(CancellationToken.None);
		try
		{
			_advertiser.CheckLiveness();
			var plan = ServiceAdvertisementPlanner.Plan(await IsEnabled(),
				_listenerState.PublicEndpoints,
				_hostNames.MachineName,
				HostVersion.Current,
				_interfaces.GetInterfaces());

			if (plan is null)
			{
				Withdraw();
				return;
			}

			var changed = !plan.Equals(_published);
			if (!changed && !_advertiser.NeedsRepublish)
			{
				return;
			}

			_published = null;
			_advertised = true;
			_advertiser.Publish(plan);
			_published = plan;
			_lastFailure = null;

			if (changed)
			{
				_logger.Information("Handed {InstanceName} as {ServiceType} on port {Port} over {InterfaceCount} " +
					"network interfaces to the system mDNS responder",
					plan.WireName,
					ServiceAdvertisementPlanner.ServiceType,
					plan.Port,
					plan.Interfaces.Count);
			}
		}
		catch (Exception exception)
		{
			if (exception.Message == _lastFailure)
			{
				_logger.Debug(exception, "Network discovery advertisement is still failing");
			}
			else
			{
				_logger.Warning(exception, "Macro Deck could not be advertised on the local network; it keeps running");
			}

			_lastFailure = exception.Message;
		}
		finally
		{
			_gate.Release();
		}
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		if (!_advertiser.IsAvailable)
		{
			_logger.Information("Network discovery is not available on this system, so Macro Deck is not advertised " +
				"on the local network");
			return;
		}

		NetworkChange.NetworkAddressChanged += OnNetworkChanged;
		NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
		_advertiser.StateChanged += RequestRefresh;
		try
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				await Reconcile();
				if (await _wake.WaitAsync(RecheckInterval, stoppingToken))
				{
					await Task.Delay(Debounce, stoppingToken);
					_wake.Wait(0, CancellationToken.None);
				}
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
		}
		finally
		{
			NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
			NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
			_advertiser.StateChanged -= RequestRefresh;
		}
	}

	private void OnNetworkChanged(object? sender, EventArgs e) => RequestRefresh();

	private async Task<bool> IsEnabled()
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var settings = await scope.ServiceProvider.GetRequiredService<IAppPreferenceService>().GetNetwork();
		return settings.DiscoveryEnabled;
	}

	private void Withdraw()
	{
		if (!_advertised)
		{
			return;
		}

		try
		{
			_advertiser.Withdraw();
			_logger.Information("Stopped advertising Macro Deck on the local network");
		}
		catch (Exception exception)
		{
			_logger.Warning(exception, "Withdrawing the network discovery advertisement failed");
		}

		_published = null;
		_advertised = false;
	}
}
