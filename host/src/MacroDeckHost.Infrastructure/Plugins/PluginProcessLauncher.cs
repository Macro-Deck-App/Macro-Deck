using System.Diagnostics;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Infrastructure.Plugins.Jobs;
using Serilog;

namespace MacroDeckHost.Infrastructure.Plugins;

public sealed class PluginProcessLauncher : IPluginProcessLauncher
{
	private static readonly string[] _scrubExactNames = ["ASPNETCORE_URLS"];
	private const string ScrubPrefix = "MACRO_DECK_PLUGIN_";

	private readonly IPluginProcessJobFactory _jobs;
	private readonly ILogger _logger;

	public PluginProcessLauncher(IPluginProcessJobFactory jobs, ILogger logger)
	{
		_jobs = jobs;
		_logger = logger.ForContext<PluginProcessLauncher>();
	}

	public IPluginProcess Start(PluginProcessStartRequest request)
	{
		var startInfo = new ProcessStartInfo
		{
			FileName = request.ExecutablePath,
			WorkingDirectory = request.WorkingDirectory,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = false
		};

		foreach (var argument in request.Arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		ScrubAndSetEnvironment(startInfo, request.Environment);

		var job = _jobs.Create();

		try
		{
			var process = Process.Start(startInfo) ??
				throw new InvalidOperationException($"Process.Start returned null for '{request.ExecutablePath}'.");
			// Nothing may run between Process.Start and the assign: the plugin is already executing, and
			// .NET has no CREATE_SUSPENDED, so this window is as small as it can be made.
			var assigned = !job.IsActive || job.TryAssign(process.Id);

			if (!assigned)
			{
				PluginInfrastructureLog.JobAssignFailed(_logger, process.Id);
			}

			return new PluginProcess(process,
				job,
				request.BootstrapOutputMaxLines,
				request.BootstrapOutputMaxBytes,
				_logger);
		}
		catch (Exception ex)
		{
			job.Dispose();
			PluginInfrastructureLog.LaunchFailed(_logger, request.ExecutablePath, ex);
			throw;
		}
	}

	private static void ScrubAndSetEnvironment(ProcessStartInfo startInfo,
		IReadOnlyDictionary<string, string?> environment)
	{
		var keysToRemove = startInfo.Environment.Keys
			.Where(key => key.StartsWith(ScrubPrefix, StringComparison.Ordinal) ||
				_scrubExactNames.Contains(key, StringComparer.OrdinalIgnoreCase))
			.ToList();

		foreach (var key in keysToRemove)
		{
			startInfo.Environment.Remove(key);
		}

		foreach (var (key, value) in environment)
		{
			startInfo.Environment[key] = value;
		}
	}
}
