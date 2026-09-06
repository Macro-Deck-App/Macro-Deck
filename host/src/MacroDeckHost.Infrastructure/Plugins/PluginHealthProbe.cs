using Serilog;


namespace MacroDeckHost.Infrastructure.Plugins;

public static class PluginHealthProbeHttpClient
{
	public const string Name = "plugin-health";
}

public interface IPluginHealthProbe
{
	Task<bool> Probe(int port,
		string path,
		TimeSpan timeout,
		string pluginId,
		CancellationToken cancellationToken = default);
}

public sealed class PluginHealthProbe : IPluginHealthProbe
{
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly ILogger _logger;

	public PluginHealthProbe(IHttpClientFactory httpClientFactory, ILogger logger)
	{
		_httpClientFactory = httpClientFactory;
		_logger = logger.ForContext<PluginHealthProbe>();
	}

	public async Task<bool> Probe(int port,
		string path,
		TimeSpan timeout,
		string pluginId,
		CancellationToken cancellationToken = default)
	{
		var address = $"http://127.0.0.1:{port}{path}";

		using var client = _httpClientFactory.CreateClient(PluginHealthProbeHttpClient.Name);
		client.Timeout = timeout;

		using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		linkedCts.CancelAfter(timeout);

		try
		{
			using var response = await client.GetAsync(address, linkedCts.Token);
			return response.IsSuccessStatusCode;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
		{
			PluginInfrastructureLog.HealthProbeFailed(_logger, pluginId, address, ex);
			return false;
		}
	}
}
