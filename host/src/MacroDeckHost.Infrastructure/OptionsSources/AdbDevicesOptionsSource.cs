using MacroDeckHost.Application.Actions.Options;
using MacroDeckHost.Application.Adb;
using MacroDeckHost.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Infrastructure.OptionsSources;

public sealed class AdbDevicesOptionsSource : IHostOptionsSource
{
	private readonly IAdbManager _adbManager;

	public AdbDevicesOptionsSource(IAdbManager adbManager)
	{
		_adbManager = adbManager;
	}

	public string Id => AdbOptionsSourceIds.Devices;

	public Task<DynamicOptionsResult> GetOptionsAsync(string? filter, CancellationToken cancellationToken)
	{
		// "Default device" stands outside the list rather than in it - it names the absence of a
		// choice, so a filter meant to narrow the devices must not be able to hide it. Its label is a
		// localization reference and carries no literal to match against either.
		var options = new List<ActionParameterOption>
		{
			new() { Value = string.Empty, Label = AppStrings.Integrations.Adb.Params.DeviceDefaultPlaceholder() }
		};

		options.AddRange(_adbManager.Devices
			.Where(device => device.State != AdbDeviceState.Disconnected)
			.OrderBy(device => device.Model ?? device.Serial, StringComparer.OrdinalIgnoreCase)
			.Select(device => new ActionParameterOption
			{
				Value = device.Serial,
				Label = $"{device.Model ?? device.Serial} ({device.Serial})"
			})
			.Where(option =>
				filter is null ||
				(option.Label.Literal ?? string.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase)));

		return Task.FromResult(new DynamicOptionsResult
		{
			Options = options,
			AllowsCustomValue = true,
			CacheSeconds = 5
		});
	}
}
