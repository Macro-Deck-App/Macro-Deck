using System.Globalization;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace MacroDeckHost.Integrations.System.Volume;

[SupportedOSPlatform("linux")]
internal sealed partial class LinuxVolumeService : IVolumeService
{
	private const string DefaultSink = "@DEFAULT_SINK@";

	private readonly bool _hasPactl = ProcessRunner.CommandExists("pactl");
	private readonly bool _hasAmixer = ProcessRunner.CommandExists("amixer");

	public bool IsSupported => _hasPactl || _hasAmixer;

	public async Task<float?> GetVolumeAsync(CancellationToken cancellationToken = default)
	{
		if (_hasPactl)
		{
			var output = await ProcessRunner.RunAsync("pactl", ["get-sink-volume", DefaultSink], cancellationToken);
			return ParsePercent(output);
		}

		if (_hasAmixer)
		{
			var output = await ProcessRunner.RunAsync("amixer", ["get", "Master"], cancellationToken);
			return ParsePercent(output);
		}

		return null;
	}

	public Task SetVolumeAsync(float level, CancellationToken cancellationToken = default)
	{
		var percent = (int)Math.Round(Math.Clamp(level, 0f, 1f) * 100);
		var value = $"{percent.ToString(CultureInfo.InvariantCulture)}%";

		if (_hasPactl)
		{
			return ProcessRunner.RunAsync("pactl", ["set-sink-volume", DefaultSink, value], cancellationToken);
		}

		if (_hasAmixer)
		{
			return ProcessRunner.RunAsync("amixer", ["set", "Master", value], cancellationToken);
		}

		return Task.CompletedTask;
	}

	public async Task<bool?> GetMuteAsync(CancellationToken cancellationToken = default)
	{
		if (_hasPactl)
		{
			var output = await ProcessRunner.RunAsync("pactl", ["get-sink-mute", DefaultSink], cancellationToken);
			return output.Contains("yes", StringComparison.OrdinalIgnoreCase);
		}

		if (_hasAmixer)
		{
			var output = await ProcessRunner.RunAsync("amixer", ["get", "Master"], cancellationToken);
			return output.Contains("[off]", StringComparison.OrdinalIgnoreCase);
		}

		return null;
	}

	public Task SetMuteAsync(bool mute, CancellationToken cancellationToken = default)
	{
		if (_hasPactl)
		{
			return ProcessRunner.RunAsync("pactl", ["set-sink-mute", DefaultSink, mute ? "1" : "0"], cancellationToken);
		}

		if (_hasAmixer)
		{
			return ProcessRunner.RunAsync("amixer", ["set", "Master", mute ? "mute" : "unmute"], cancellationToken);
		}

		return Task.CompletedTask;
	}

	private static float? ParsePercent(string output)
	{
		var match = PercentRegex().Match(output);
		return match.Success && int.TryParse(match.Groups[1].Value, out var percent)
			? Math.Clamp(percent / 100f, 0f, 1f)
			: null;
	}

	[GeneratedRegex(@"(\d{1,3})%")]
	private static partial Regex PercentRegex();
}
