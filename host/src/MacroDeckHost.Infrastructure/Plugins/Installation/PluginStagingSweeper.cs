using MacroDeckHost.Application.Paths;
using Serilog;

namespace MacroDeckHost.Infrastructure.Plugins.Installation;

public static class PluginStagingSweeper
{
	public static void Sweep(IMacroDeckPaths paths)
	{
		if (!Directory.Exists(paths.PluginStagingDirectory))
		{
			return;
		}

		foreach (var leftover in Directory.GetFileSystemEntries(paths.PluginStagingDirectory))
		{
			try
			{
				if (Directory.Exists(leftover))
				{
					Directory.Delete(leftover, true);
				}
				else
				{
					File.Delete(leftover);
				}
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException)
			{
				Log.Warning(e, "Could not remove the interrupted plugin install {Path}", leftover);
			}
		}
	}
}
