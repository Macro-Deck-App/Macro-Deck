using System.Net;
using System.Text.Json;

namespace MacroDeck.Plugin.Testing;

/// <summary>
/// A running plugin, hosted in-process (<see cref="InProcessPlugin" />) or as a real child process
/// (<see cref="ExternalPlugin" />). Common to both: an HTTP base address where the SDK's own
/// <c>/_macrodeck/*</c> endpoints are served, which is what <see cref="ProbeHealthAsync" /> reads.
/// </summary>
public abstract class PluginUnderTest : IAsyncDisposable
{
	// One client for this instance's whole lifetime, not one per ProbeHealthAsync call: HealthPolling
	// alone can call it every 50ms for up to 10s, which would otherwise open (and leak, since nothing
	// here ever disposed the old one) 200 sockets per wait.
	private readonly HttpClient _httpClient = new();

	protected private PluginUnderTest()
	{
	}

	/// <summary>The plugin's own HTTP base address - where <c>/_macrodeck/*</c> is served, independent of any session.</summary>
	public abstract Uri BaseAddress { get; }

	/// <summary>
	/// Fetches <c>/_macrodeck/health</c>, <c>/ready</c>, <c>/info</c> and <c>/diagnostics</c> together.
	/// Never throws for a connection failure - a process that is not listening yet, or has stopped,
	/// simply reports <see cref="PluginHealthReport.Live" /> false, since "not reachable" is exactly the
	/// state this method exists to observe rather than fail on.
	/// </summary>
	public async Task<PluginHealthReport> ProbeHealthAsync(TimeSpan? timeout = null)
	{
		var live = await TryGetAsync("/_macrodeck/health", timeout, expectOk: true).ConfigureAwait(false);

		if (live is null)
		{
			return new PluginHealthReport { Live = false, Ready = false };
		}

		var ready = await TryGetAsync("/_macrodeck/ready", timeout, expectOk: true).ConfigureAwait(false);
		var info = await TryGetAsync("/_macrodeck/info", timeout, expectOk: true).ConfigureAwait(false);
		var diagnostics = await TryGetAsync("/_macrodeck/diagnostics", timeout, expectOk: true).ConfigureAwait(false);

		return new PluginHealthReport
		{
			Live = true,
			Ready = ready is not null,
			Id = info?.GetOrNull("id"),
			Name = info?.GetOrNull("name"),
			Version = info?.GetOrNull("version"),
			Mode = info?.GetOrNull("mode"),
			Status = diagnostics?.GetOrNull("status"),
			NegotiatedVersion = diagnostics?.GetIntOrNull("negotiatedVersion"),
			DeclaredCapabilities = diagnostics?.GetIntOrNull("declaredCapabilities"),
			AcceptedCapabilities = diagnostics?.GetIntOrNull("acceptedCapabilities"),
			InFlightInvocations = diagnostics?.GetIntOrNull("inFlightInvocations")
		};
	}

	/// <summary>Stops the plugin and releases everything it holds.</summary>
	public abstract ValueTask DisposeAsync();

	/// <summary>Disposes the shared <see cref="HttpClient" /> <see cref="ProbeHealthAsync" /> uses. Every
	/// override of <see cref="DisposeAsync" /> must call this too - there is no base implementation to
	/// chain to, since each subclass owns different unmanaged resources of its own.</summary>
	protected private void DisposeHttpClient() => _httpClient.Dispose();

	private async Task<JsonElement?> TryGetAsync(string path, TimeSpan? timeout, bool expectOk)
	{
		try
		{
			using var deadline = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
			using var response = await _httpClient.GetAsync(new Uri(BaseAddress, path), deadline.Token)
				.ConfigureAwait(false);

			if (expectOk && response.StatusCode != HttpStatusCode.OK)
			{
				return null;
			}

			var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

			if (string.IsNullOrEmpty(text))
			{
				return null;
			}

			// Cloned so the value outlives the JsonDocument its bytes were parsed into - disposed here
			// rather than left for the GC, which is what actually returns its pooled buffer.
			using var document = JsonDocument.Parse(text);
			return document.RootElement.Clone();
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
		{
			return null;
		}
	}
}

/// <summary>Small, tolerant readers over an anonymous JSON object from an SDK endpoint.</summary>
file static class JsonElementExtensions
{
	public static string? GetOrNull(this JsonElement element, string property)
		=> element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	public static int? GetIntOrNull(this JsonElement element, string property)
		=> element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
			? value.GetInt32()
			: null;
}
