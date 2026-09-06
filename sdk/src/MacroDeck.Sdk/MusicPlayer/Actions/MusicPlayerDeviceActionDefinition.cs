using MacroDeck.Sdk.Actions;
using Serilog;

namespace MacroDeck.Sdk.MusicPlayer.Actions;

/// <summary>
/// Transfers playback to a specific device, optionally starting it. The device parameter is
/// optional: when the user left it empty at configure time, the executor asks the triggering
/// client to pick a device at run time via <see cref="IActionInteractions.RequestDevicePicker"/>;
/// otherwise it transfers to the configured device directly. Device options for the parameter come
/// from <see cref="IMusicPlayerDeviceProvider"/>. Backs both the "Play on Device" and "Transfer
/// Playback" actions - <c>startPlayback</c> is the only thing that tells them apart.
/// </summary>
public sealed class MusicPlayerDeviceActionDefinition : MusicPlayerActionDefinition
{
	private static readonly ILogger _logger = Log.ForContext<MusicPlayerDeviceActionDefinition>();

	private readonly string _integrationId;
	private readonly MusicPlayerResolver _resolver;
	private readonly Func<IReadOnlyList<MusicPlayerInstance>> _getInstances;
	private readonly bool _startPlayback;

	public MusicPlayerDeviceActionDefinition(
		string integrationId,
		MusicPlayerResolver resolver,
		Func<IReadOnlyList<MusicPlayerInstance>> getInstances,
		string id,
		string name,
		string description,
		bool startPlayback)
		: base(resolver,
			getInstances,
			id,
			name,
			description,
			[
				ActionParameter.Autocomplete(MusicPlayerActions.DeviceParameterName,
					label: "Device",
					description: "Leave empty to pick at run time.",
					placeholder: "Search devices…",
					required: false)
			],
			NoOpCommand)
	{
		_integrationId = integrationId;
		_resolver = resolver;
		_getInstances = getInstances;
		_startPlayback = startPlayback;
	}

	// The device action has its own executor (needs _startPlayback, which the base command
	// delegate can't capture), so the base command is a no-op.
	private static Task NoOpCommand(
		IMusicPlayer _,
		IReadOnlyDictionary<string, object> __,
		IActionInteractions? ___,
		CancellationToken ____)
		=> Task.CompletedTask;

	public override IActionExecutor CreateExecutor() => new DeviceExecutor(this, _resolver);

	public override async Task<DynamicOptionsResult> GetDynamicOptionsAsync(DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		if (context.ParameterName == MusicPlayerActions.InstanceParameterName)
		{
			return new DynamicOptionsResult { Options = InstanceOptions(_getInstances()), CacheSeconds = 2 };
		}

		if (context.ParameterName != MusicPlayerActions.DeviceParameterName)
		{
			return new DynamicOptionsResult { Options = [] };
		}

		var instanceId = ResolveInstanceId(context.CurrentParameters);
		if (instanceId is null)
		{
			return new DynamicOptionsResult { Options = [] };
		}

		if (_resolver(instanceId) is not IMusicPlayerDeviceProvider deviceProvider)
		{
			return new DynamicOptionsResult { Options = [], AllowsCustomValue = true };
		}

		try
		{
			var devices = await deviceProvider.GetDevicesAsync(cancellationToken);

			// The action-builder disables client-side filtering for dynamic autocompletes
			// (param-row.component.html binds [filterLocally]="!isDynamic"), so this has to filter
			// itself or typing in the Device field would keep showing the unfiltered list.
			if (!string.IsNullOrEmpty(context.Filter))
			{
				devices = devices.Where(d =>
						d.Name.Contains(context.Filter, StringComparison.OrdinalIgnoreCase) ||
						(d.Type?.Contains(context.Filter, StringComparison.OrdinalIgnoreCase) ?? false))
					.ToList();
			}

			// MusicPlayerDevice.Type is optional, so the parenthesised suffix is dropped rather than
			// rendered as an empty "Name ()".
			var options = devices
				.Select(d => new ActionParameterOption
				{
					Value = d.Id,
					Label = string.IsNullOrEmpty(d.Type) ? d.Name : $"{d.Name} ({d.Type})"
				})
				.ToList();

			// Devices churn faster than a track/playlist library (Spotify Connect endpoints come and
			// go with what's actually open right now), so this caches for less time than the item
			// action's catalog options do.
			return new DynamicOptionsResult { Options = options, AllowsCustomValue = true, CacheSeconds = 2 };
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return new DynamicOptionsResult { Options = [], AllowsCustomValue = true };
		}
		catch (Exception ex)
		{
			// Unlike the runtime picker, degrading to an empty list is right here: this parameter
			// accepts a custom value, so the user can still type the id even with no suggestions. It
			// is logged rather than silent, because "the autocomplete is empty" and "the provider is
			// unreachable" look identical from the editor.
			_logger.Warning(ex, "Could not load device suggestions for {Instance}", instanceId);
			return new DynamicOptionsResult { Options = [], AllowsCustomValue = true };
		}
	}

	private async Task<ActionResult> TransferAsync(
		IMusicPlayer player,
		IReadOnlyDictionary<string, object> values,
		IActionInteractions? interactions,
		string? originClientId,
		CancellationToken cancellationToken)
	{
		var deviceId = MusicPlayerActions.GetString(values, MusicPlayerActions.DeviceParameterName);
		var localInstanceId = values.GetValueOrDefault(MusicPlayerActions.InstanceParameterName)?.ToString();

		if (player is not IMusicPlayerDeviceProvider devices)
		{
			return ActionResult.Failed(ActionErrorCodes.Unavailable,
				"This music player cannot switch playback devices.");
		}

		if (!string.IsNullOrWhiteSpace(deviceId))
		{
			await devices.TransferPlaybackAsync(deviceId.Trim(), _startPlayback, cancellationToken);
			return ActionResult.Success();
		}

		// No device configured → ask the triggering client to pick one now (fire-and-forget). The
		// picker needs the globally-unique instance id (the client's reply is routed through the
		// host registry, which keys by integrationId::localId). RequestDevicePicker is documented
		// fire-and-forget, so nothing has moved yet - Accepted, not Success.
		var globalInstanceId = ToGlobalInstanceId(localInstanceId);
		if (globalInstanceId is null)
		{
			return ActionResult.Failed(ActionErrorCodes.NotFound,
				"No music player instance was available to ask for a device.");
		}

		interactions?.RequestDevicePicker(originClientId, globalInstanceId, _startPlayback);
		return ActionResult.Accepted("Waiting for a device to be picked.");
	}

	private string? ToGlobalInstanceId(string? localInstanceId)
	{
		if (!string.IsNullOrEmpty(localInstanceId))
		{
			return $"{_integrationId}::{localInstanceId}";
		}

		var instances = _getInstances();
		return instances.Count > 0 ? $"{_integrationId}::{instances[0].Id}" : null;
	}

	private string? ResolveInstanceId(IReadOnlyDictionary<string, object?> currentParameters)
	{
		var instanceId = currentParameters.GetValueOrDefault(MusicPlayerActions.InstanceParameterName)?.ToString();
		if (!string.IsNullOrEmpty(instanceId))
		{
			return instanceId;
		}

		var instances = _getInstances();
		return instances.Count > 0 ? instances[0].Id : null;
	}

	private sealed class DeviceExecutor : IActionExecutor
	{
		private readonly MusicPlayerDeviceActionDefinition _owner;
		private readonly MusicPlayerResolver _resolver;

		public DeviceExecutor(MusicPlayerDeviceActionDefinition owner, MusicPlayerResolver resolver)
		{
			_owner = owner;
			_resolver = resolver;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var instanceId = context.Parameters.GetValueOrDefault(MusicPlayerActions.InstanceParameterName)?.ToString();
			var player = string.IsNullOrEmpty(instanceId) ? _resolver(null) : _resolver(instanceId) ?? _resolver(null);
			if (player is null)
			{
				return ActionResult.Failed(ActionErrorCodes.NotConfigured, "No music player is set up.");
			}

			try
			{
				return await _owner.TransferAsync(player,
					context.Parameters,
					context.Interactions,
					context.OriginClientId,
					context.CancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_logger.Warning(ex, "Music player device action failed");
				return ActionResult.Failed(ActionErrorCodes.ProviderError,
					"The music player could not run this action.");
			}
		}
	}
}
