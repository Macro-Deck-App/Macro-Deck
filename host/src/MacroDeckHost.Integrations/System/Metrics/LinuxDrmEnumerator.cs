using System.Text.RegularExpressions;

namespace MacroDeckHost.Integrations.System.Metrics;

internal static partial class LinuxDrmEnumerator
{
	public const string DefaultRoot = "/sys/class/drm";
	public const string DefaultNvidiaRoot = "/proc/driver/nvidia/gpus";

	public static IReadOnlyList<string> FindAmdGpuBusyPercentPaths(string drmRoot = DefaultRoot)
	{
		try
		{
			if (!Directory.Exists(drmRoot))
			{
				return [];
			}

			return Directory.GetDirectories(drmRoot)
				// This directory also holds one entry per connector (card0-DP-1, card0-HDMI-A-1).
				.Where(d => CardDirectory().IsMatch(Path.GetFileName(d)))
				.Order(StringComparer.Ordinal)
				.Select(d => Path.Combine(d, "device", "gpu_busy_percent"))
				.Where(File.Exists)
				.ToArray();
		}
		catch
		{
			return [];
		}
	}

	public static int? CountNvidiaGpus(string nvidiaRoot = DefaultNvidiaRoot)
	{
		try
		{
			return Directory.Exists(nvidiaRoot) ? Directory.GetDirectories(nvidiaRoot).Length : null;
		}
		catch
		{
			return null;
		}
	}

	[GeneratedRegex(@"^card\d+$")]
	private static partial Regex CardDirectory();
}
