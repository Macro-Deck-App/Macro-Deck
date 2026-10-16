using MacroDeckHost.Integrations.System.Variables;
using MacroDeckHost.Integrations.System.Volume;
using MacroDeckHost.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.System.Actions;

internal static class AudioDeviceParameter
{
	public const string Name = "device";

	public static ActionParameter Create()
	{
		return ActionParameter.DynamicChoice(Name,
			AppStrings.Integrations.System.Actions.AudioDevice.Label(),
			AppStrings.Integrations.System.Actions.AudioDevice.Description(),
			placeholder: AppStrings.Integrations.System.Actions.AudioDevice.DefaultOutput());
	}

	public static bool TryRead(object? value, out AudioTarget target)
	{
		return AudioTarget.TryParse(value?.ToString(), out target);
	}

	public static async Task<DynamicOptionsResult> GetOptionsAsync(
		IVolumeService volume,
		Func<AudioTarget, string?> knownName,
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		IReadOnlyList<AudioDevice> devices;
		try
		{
			devices = volume.IsSupported ? await volume.GetDevicesAsync(cancellationToken) : [];
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			devices = [];
		}

		var options = new List<ActionParameterOption>
		{
			new()
			{
				Value = AudioTarget.DefaultOutput.ToParameterValue(),
				Label = AppStrings.Integrations.System.Actions.AudioDevice.DefaultOutput()
			},
			new()
			{
				Value = AudioTarget.DefaultInput.ToParameterValue(),
				Label = AppStrings.Integrations.System.Actions.AudioDevice.DefaultInput()
			}
		};

		foreach (var device in devices.OrderBy(d => d.Flow)
			.ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
		{
			var twin = devices.Count(other => other.Flow == device.Flow && other.Name == device.Name) > 1;
			var name = twin ? $"{device.Name} #{AudioDeviceVariables.ShortId(device.Id)}" : device.Name;
			options.Add(new ActionParameterOption
			{
				Value = new AudioTarget(device.Flow, device.Id).ToParameterValue(),
				Label = device.Flow == AudioFlow.Output
					? AppStrings.Integrations.System.Actions.AudioDevice.OutputOption(name)
					: AppStrings.Integrations.System.Actions.AudioDevice.InputOption(name)
			});
		}

		if (TryRead(context.CurrentParameters.GetValueOrDefault(Name), out var current)
			&& !current.IsDefault
			&& options.All(option => option.Value != current.ToParameterValue()))
		{
			options.Add(new ActionParameterOption
			{
				Value = current.ToParameterValue(),
				Label = knownName(current) is { } name
					? AppStrings.Integrations.System.Actions.AudioDevice.UnavailableOption(name)
					: AppStrings.Integrations.System.Actions.AudioDevice.UnknownUnavailableOption()
			});
		}

		return new DynamicOptionsResult { Options = options };
	}
}
