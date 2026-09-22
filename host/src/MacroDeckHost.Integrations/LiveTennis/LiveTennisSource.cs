using System.Globalization;
using System.Text.Json;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.LiveTennis;

internal sealed class LiveTennisSource : IAsyncDisposable
{
	// 86,400 / 900 = 96 attempts per day, within the free tier's 100; every widget shares this floor.
	private static readonly TimeSpan _interval = TimeSpan.FromMinutes(15);
	private static readonly SemaphoreSlim _gate = new(1, 1);
	private static readonly ILogger _logger = IntegrationLog.For<LiveTennisSource>(LiveTennisIntegration.IntegrationId);
	private static readonly Uri _endpoint = new("https://api.livetennisapi.com/api/public/v1/matches?status=live&limit=500");
	private readonly CancellationTokenSource _stopped = new();
	private readonly CancellationToken _stopToken;
	private readonly HttpClient _client;
	private readonly TimeProvider _clock;
	private readonly IIntegrationConfig _config;
	private readonly Guid _entryId;
	private readonly string _apiKey;
	private LiveTennisSnapshot? _snapshot;

	public LiveTennisSource(HttpClient client, TimeProvider clock, IIntegrationConfig config, Guid entryId, string apiKey)
	{
		_client = client;
		_clock = clock;
		_config = config;
		_entryId = entryId;
		_apiKey = apiKey;
		_stopToken = _stopped.Token;
	}

	public async Task<LiveTennisSnapshot?> ReadAsync(CancellationToken cancellationToken)
	{
		if (_stopToken.IsCancellationRequested)
		{
			return null;
		}

		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopToken);
		var token = linked.Token;
		await _gate.WaitAsync(token);
		try
		{
			var now = _clock.GetUtcNow();
			var stored = await _config.GetStringAsync(_entryId, LiveTennisIntegration.LastAttempt, token);
			if (!long.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out var last) ||
				last < 0 || last > now.ToUnixTimeMilliseconds())
			{
				// Missing or reset state starts a cooldown, including after deleting or reconfiguring the entry.
				await SaveAttempt(now, token);
				_snapshot = null;
				return null;
			}

			if (now.ToUnixTimeMilliseconds() - last < _interval.TotalMilliseconds)
			{
				return _snapshot;
			}

			_snapshot = null;
			await SaveAttempt(now, token);
			using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);
			request.Headers.Add("X-API-Key", _apiKey);
			using var response = await _client.SendAsync(request, token);
			response.EnsureSuccessStatusCode();
			using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
			_snapshot = LiveTennisSnapshot.Parse(json.RootElement, _clock.GetUtcNow());
			return _snapshot;
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return null;
		}
		catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or IOException or KeyNotFoundException or FormatException)
		{
			_logger.Warning("Tennis snapshot unavailable ({FailureType})", ex.GetType().Name);
			_snapshot = null;
			return null;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async ValueTask DisposeAsync()
	{
		await _stopped.CancelAsync();
		await _gate.WaitAsync();
		_gate.Release();
		_stopped.Dispose();
	}

	private Task SaveAttempt(DateTimeOffset now, CancellationToken token)
		=> _config.SetStringAsync(_entryId, LiveTennisIntegration.LastAttempt,
			now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture), token);
}
