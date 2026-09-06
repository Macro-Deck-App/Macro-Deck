using Serilog;

namespace MacroDeckHost.Infrastructure.Plugins.Jobs;

public interface IPluginProcessJobFactory
{
	IPluginProcessJob Create();
}

public sealed class PluginProcessJobFactory : IPluginProcessJobFactory
{
	private static readonly Lazy<bool> _isSupported = new(Probe);

	private readonly ILogger _logger;

	public PluginProcessJobFactory(ILogger logger) => _logger = logger.ForContext<PluginProcessJobFactory>();

	public IPluginProcessJob Create()
	{
		if (!OperatingSystem.IsWindows() || !_isSupported.Value)
		{
			return new NullPluginProcessJob();
		}

		return WindowsPluginProcessJob.TryCreate(_logger) ?? (IPluginProcessJob)new NullPluginProcessJob();
	}

	private static bool Probe()
	{
		if (!OperatingSystem.IsWindows())
		{
			return false;
		}

		try
		{
			using var job = WindowsPluginProcessJob.TryCreate(Serilog.Core.Logger.None);
			return job is not null;
		}
		catch (Exception ex) when (ex is DllNotFoundException
			or EntryPointNotFoundException
			or BadImageFormatException
			or MissingMethodException)
		{
			return false;
		}
	}
}
