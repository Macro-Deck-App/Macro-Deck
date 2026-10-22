using System.Threading.Channels;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Sdk.Batteries;
using MacroDeckHost.Application.Batteries;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Application.Email;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class IntegrationCapabilityDataBroadcastBackgroundService : HostReadyBackgroundService
{
	private static readonly TimeSpan _defaultCoalesceWindow = TimeSpan.FromMilliseconds(500);

	private readonly IBatteryRegistry _batteries;
	private readonly ICalendarEventCache _calendars;
	private readonly IEmailInboxCache _inboxes;
	private readonly IUiTransport _transport;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;
	private readonly TimeSpan _coalesceWindow;
	private readonly Channel<string> _changedKinds = Channel.CreateUnbounded<string>();
	private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _last = new(StringComparer.Ordinal);

	public IntegrationCapabilityDataBroadcastBackgroundService(
		IHostApplicationLifetime lifetime,
		IBatteryRegistry batteries,
		ICalendarEventCache calendars,
		IEmailInboxCache inboxes,
		IUiTransport transport,
		TimeProvider time,
		ILogger logger,
		TimeSpan? coalesceWindow = null)
		: base(lifetime)
	{
		_batteries = batteries;
		_calendars = calendars;
		_inboxes = inboxes;
		_transport = transport;
		_time = time;
		_logger = logger.ForContext<IntegrationCapabilityDataBroadcastBackgroundService>();
		_coalesceWindow = coalesceWindow ?? _defaultCoalesceWindow;
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		_batteries.Changed += OnBatteriesChanged;
		_calendars.Changed += OnCalendarsChanged;
		_inboxes.Changed += OnInboxesChanged;
		try
		{
			foreach (var kind in (string[])
				[CapabilityKinds.BatteryProvider, CapabilityKinds.Calendar, CapabilityKinds.EmailInbox])
			{
				_last[kind] = Fingerprint(kind);
			}

			while (await _changedKinds.Reader.WaitToReadAsync(stoppingToken))
			{
				await Task.Delay(_coalesceWindow, _time, stoppingToken);

				var kinds = new HashSet<string>(StringComparer.Ordinal);
				while (_changedKinds.Reader.TryRead(out var kind))
				{
					kinds.Add(kind);
				}

				foreach (var kind in kinds)
				{
					await Broadcast(kind, stoppingToken);
				}
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
		}
		finally
		{
			_batteries.Changed -= OnBatteriesChanged;
			_calendars.Changed -= OnCalendarsChanged;
			_inboxes.Changed -= OnInboxesChanged;
		}
	}

	private async Task Broadcast(string kind, CancellationToken cancellationToken)
	{
		try
		{
			var next = Fingerprint(kind);
			var changed = IntegrationCapabilityFingerprints.ChangedOwners(_last[kind], next);
			_last[kind] = next;
			if (changed.Count == 0)
			{
				return;
			}

			await _transport.SendToGroup(UiAdminGroups.Admin,
				new IntegrationCapabilityDataChangedEvent { Kind = kind, IntegrationIds = [.. changed] },
				cancellationToken);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			_logger.Warning(exception, "Broadcasting a {Kind} data change failed", kind);
		}
	}

	private IReadOnlyDictionary<string, string> Fingerprint(string kind)
	{
		return kind switch
		{
			CapabilityKinds.BatteryProvider => IntegrationCapabilityFingerprints.Batteries(_batteries.Devices),
			CapabilityKinds.Calendar => IntegrationCapabilityFingerprints.Calendars(_calendars.Snapshot),
			_ => IntegrationCapabilityFingerprints.EmailInboxes(_inboxes.Snapshot)
		};
	}

	private void OnBatteriesChanged(object? sender, BatteryDevicesChangedEventArgs e)
	{
		_changedKinds.Writer.TryWrite(CapabilityKinds.BatteryProvider);
	}

	private void OnCalendarsChanged()
	{
		_changedKinds.Writer.TryWrite(CapabilityKinds.Calendar);
	}

	private void OnInboxesChanged()
	{
		_changedKinds.Writer.TryWrite(CapabilityKinds.EmailInbox);
	}
}
