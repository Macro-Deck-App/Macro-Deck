using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.LiveTennis;

[MacroDeckIntegration]
public sealed class LiveTennisIntegration : IIntegration, IConfigFlowProvider, IVariableProvider, IDisposable
{
	public const string IntegrationId = "app.macro-deck.live-tennis";
	internal const string ApiKey = "apiKey";
	internal const string LastAttempt = "lastAttempt";

	private readonly HttpClient _client;
	private readonly TimeProvider _clock;
	private LiveTennisSource? _source;

	public LiveTennisIntegration()
		: this(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
		{
			Timeout = TimeSpan.FromSeconds(20),
			MaxResponseContentBufferSize = 2 * 1024 * 1024
		}, TimeProvider.System)
	{
	}

	internal LiveTennisIntegration(HttpClient client, TimeProvider clock)
	{
		_client = client;
		_clock = clock;
	}

	public string Id => IntegrationId;
	public LocalizedText Name => AppStrings.Integrations.LiveTennis.Name();
	public string Version => "1.0.0";
	public bool IsInitialized { get; private set; }
	public bool AllowsMultipleConfigurations => false;
	public IReadOnlyList<IActionDefinition> Actions => [];

	public IReadOnlyList<VariableDefinition> Variables { get; } =
	[
		VariableDefinition.Eager("tennis_scores", VariableType.Text, refreshInterval: TimeSpan.FromMinutes(1))
			with { DisplayName = AppStrings.Integrations.LiveTennis.Scores() },
		VariableDefinition.Eager("tennis_match_count", VariableType.Numeric, refreshInterval: TimeSpan.FromMinutes(1))
			with { DisplayName = AppStrings.Integrations.LiveTennis.MatchCount() },
		VariableDefinition.Eager("tennis_updated_at", VariableType.Text, refreshInterval: TimeSpan.FromMinutes(1))
			with { DisplayName = AppStrings.Integrations.LiveTennis.UpdatedAt() }
	];

	public IConfigFlow CreateConfigFlow() => new LiveTennisConfigFlow();

	public async Task InitializeAsync(IIntegrationContext context)
	{
		var entries = await context.Config.GetEntriesAsync();
		if (entries.Count == 1)
		{
			var entry = entries[0];
			var key = await context.Config.GetSecretAsync(entry.Id, ApiKey);
			if (!string.IsNullOrWhiteSpace(key))
			{
				_source = new LiveTennisSource(_client, _clock, context.Config, entry.Id, key);
			}
		}

		IsInitialized = true;
	}

	public async Task ShutdownAsync()
	{
		var source = Interlocked.Exchange(ref _source, null);
		if (source is not null)
		{
			await source.DisposeAsync();
		}

		IsInitialized = false;
	}

	public async ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		var source = _source;
		if (source is null || localId is not ("tennis_scores" or "tennis_match_count" or "tennis_updated_at"))
		{
			return VariableReading.Unavailable;
		}

		var snapshot = await source.ReadAsync(cancellationToken);
		if (snapshot is null)
		{
			return VariableReading.Unavailable;
		}

		return VariableReading.Of(localId switch
		{
			"tennis_scores" => snapshot.Scores,
			"tennis_match_count" => (object)snapshot.MatchCount,
			_ => snapshot.UpdatedAt
		});
	}

	public void Dispose() => _client.Dispose();
}
