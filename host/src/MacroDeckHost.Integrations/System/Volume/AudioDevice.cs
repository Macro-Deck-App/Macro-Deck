namespace MacroDeckHost.Integrations.System.Volume;

public enum AudioFlow
{
	Output,
	Input
}

public sealed record AudioDevice(string Id, string Name, AudioFlow Flow, bool IsDefault);

public readonly record struct AudioTarget(AudioFlow Flow, string? DeviceId)
{
	private const string DefaultOutputValue = "default-output";
	private const string DefaultInputValue = "default-input";
	private const string OutputPrefix = "output:";
	private const string InputPrefix = "input:";

	public static AudioTarget DefaultOutput => new(AudioFlow.Output, null);

	public static AudioTarget DefaultInput => new(AudioFlow.Input, null);

	public bool IsDefault => DeviceId is null;

	public string ToParameterValue() => (Flow, DeviceId) switch
	{
		(AudioFlow.Output, null) => DefaultOutputValue,
		(AudioFlow.Input, null) => DefaultInputValue,
		(AudioFlow.Output, var id) => OutputPrefix + id,
		(_, var id) => InputPrefix + id
	};

	// A missing value is how every action saved before device selection existed reads, so it has to mean
	// the default output. Anything else unrecognised is refused rather than guessed.
	public static bool TryParse(string? value, out AudioTarget target)
	{
		target = DefaultOutput;
		if (string.IsNullOrEmpty(value) || value == DefaultOutputValue)
		{
			return true;
		}

		if (value == DefaultInputValue)
		{
			target = DefaultInput;
			return true;
		}

		if (TryDevice(value, OutputPrefix, AudioFlow.Output, out target) ||
			TryDevice(value, InputPrefix, AudioFlow.Input, out target))
		{
			return true;
		}

		target = DefaultOutput;
		return false;
	}

	private static bool TryDevice(string value, string prefix, AudioFlow flow, out AudioTarget target)
	{
		target = DefaultOutput;
		if (!value.StartsWith(prefix, StringComparison.Ordinal) || value.Length == prefix.Length)
		{
			return false;
		}

		target = new AudioTarget(flow, value[prefix.Length..]);
		return true;
	}
}
