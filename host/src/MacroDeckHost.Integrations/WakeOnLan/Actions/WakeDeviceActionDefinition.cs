using System.Net.Sockets;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Integrations.WakeOnLan.Configuration;
using MacroDeckHost.Integrations.WakeOnLan.Protocol;
using Serilog;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.WakeOnLan;

namespace MacroDeckHost.Integrations.WakeOnLan.Actions;

internal sealed class WakeDeviceActionDefinition : IDynamicOptionsActionDefinition
{
	public const string DeviceParameter = "device";

	private readonly Func<IIntegrationConfig?> _config;
	private readonly IWakeOnLanSender _sender;
	private readonly Func<TimeSpan, CancellationToken, Task> _delay;
	private readonly ILogger _logger;

	public WakeDeviceActionDefinition(
		Func<IIntegrationConfig?> config,
		IWakeOnLanSender sender,
		Func<TimeSpan, CancellationToken, Task> delay,
		ILogger logger)
	{
		_config = config;
		_sender = sender;
		_delay = delay;
		_logger = logger;
	}

	public string Id => "wake-device";

	public LocalizedText Name => Strings.Actions.WakeDeviceName();

	public LocalizedText Description => Strings.Actions.WakeDeviceDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.DynamicChoice(DeviceParameter,
			Strings.Actions.DeviceLabel(),
			Strings.Actions.DeviceDescription(),
			placeholder: Strings.Actions.DevicePlaceholder(),
			required: true)
	];

	public IActionExecutor CreateExecutor()
	{
		return new Executor(_config, _sender, _delay, _logger);
	}

	public async Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		if (context.ParameterName != DeviceParameter || _config() is not { } config)
		{
			return new DynamicOptionsResult { Options = [] };
		}

		var entries = await config.GetEntriesAsync(cancellationToken).ConfigureAwait(false);

		return new DynamicOptionsResult
		{
			Options =
			[
				.. entries.Select(entry
					=> new ActionParameterOption { Value = entry.Id.ToString(), Label = entry.Title })
			],
			CacheSeconds = 5
		};
	}

	private sealed class Executor : IActionExecutor
	{
		private readonly Func<IIntegrationConfig?> _config;
		private readonly IWakeOnLanSender _sender;
		private readonly Func<TimeSpan, CancellationToken, Task> _delay;
		private readonly ILogger _logger;

		public Executor(
			Func<IIntegrationConfig?> config,
			IWakeOnLanSender sender,
			Func<TimeSpan, CancellationToken, Task> delay,
			ILogger logger)
		{
			_config = config;
			_sender = sender;
			_delay = delay;
			_logger = logger;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var cancellationToken = context.CancellationToken;
			if (!Guid.TryParse(context.Parameters.GetValueOrDefault(DeviceParameter) as string, out var entryId))
			{
				return ActionResult.Failed(ActionErrorCodes.NotConfigured, Strings.Errors.NoDevice());
			}

			var config = _config();
			var entries = config is null
				? []
				: await config.GetEntriesAsync(cancellationToken).ConfigureAwait(false);
			var entry = entries.FirstOrDefault(candidate => candidate.Id == entryId);
			if (config is null || entry is null)
			{
				return ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Errors.DeviceNotFound());
			}

			var device = await WakeOnLanDevice.ReadAsync(config, entryId, cancellationToken).ConfigureAwait(false);
			if (device is null)
			{
				_logger.Warning("Wake on LAN device {Device} has invalid saved settings", entry.Title);
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					Strings.Errors.InvalidDevice(entry.Title));
			}

			var packet = MagicPacket.Build(device.Mac);
			var target = device.Target;
			for (var sent = 0; sent < device.Packets; sent++)
			{
				if (sent > 0)
				{
					await _delay(device.Delay, cancellationToken).ConfigureAwait(false);
				}

				try
				{
					await _sender.SendAsync(packet, target, cancellationToken).ConfigureAwait(false);
				}
				catch (SocketException ex)
				{
					_logger.Warning(ex,
						"Wake on LAN packet {Packet} of {Packets} for {Device} to {Target} was not sent: {SocketError}",
						sent + 1,
						device.Packets,
						entry.Title,
						target,
						ex.SocketErrorCode);
					var (code, message) = WakeOnLanErrors.Describe(ex, target);
					return ActionResult.Failed(code, message);
				}
			}

			return ActionResult.Success();
		}
	}
}
