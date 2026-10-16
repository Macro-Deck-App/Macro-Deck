using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeckHost.Integrations.Steam.State;

namespace MacroDeckHost.Integrations.Steam.Protocol;

internal enum SteamWebFailure
{
	InvalidKey,

	RateLimited,

	Unavailable
}

internal sealed class SteamWebApiException(SteamWebFailure failure)
	: Exception($"The Steam Web API request failed: {failure}.")
{
	public SteamWebFailure Failure { get; } = failure;
}

internal sealed record SteamPlayerSummary(
	string SteamId,
	string? Name,
	int PersonaState,
	int? GameId,
	string? GameName,
	string? AvatarUrl = null);

internal interface ISteamWebApiClient
{
	Task<SteamPlayerSummary?> GetPlayerSummaryAsync(string steamId, CancellationToken cancellationToken);

	Task<IReadOnlyList<SteamPlayerSummary>> GetPlayerSummariesAsync(
		IReadOnlyList<string> steamIds,
		CancellationToken cancellationToken);

	Task<IReadOnlyList<string>?> GetFriendIdsAsync(string steamId, CancellationToken cancellationToken);

	Task<string?> ResolveVanityUrlAsync(string vanityName, CancellationToken cancellationToken);

	Task<IReadOnlyList<SteamOwnedGame>?> GetOwnedGamesAsync(string steamId, CancellationToken cancellationToken);
}

internal sealed class SteamWebApiClient : ISteamWebApiClient, IDisposable
{
	internal const string BaseUrl = "https://api.steampowered.com/";

	private const int MaximumBatch = 100;

	private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

	private readonly HttpClient _http;
	private readonly string _key;

	public SteamWebApiClient(string key)
		: this(key, IntegrationHttp.CreateClient(TimeSpan.FromSeconds(15)))
	{
	}

	internal SteamWebApiClient(string key, HttpClient http)
	{
		_key = key;
		_http = http;
		_http.BaseAddress = new Uri(BaseUrl);
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
	}

	public async Task<SteamPlayerSummary?> GetPlayerSummaryAsync(string steamId, CancellationToken cancellationToken)
	{
		return (await GetPlayerSummariesAsync([steamId], cancellationToken)).FirstOrDefault(player =>
			string.Equals(player.SteamId, steamId, StringComparison.Ordinal));
	}

	public async Task<IReadOnlyList<SteamPlayerSummary>> GetPlayerSummariesAsync(
		IReadOnlyList<string> steamIds,
		CancellationToken cancellationToken)
	{
		var players = new List<SteamPlayerSummary>();

		foreach (var batch in steamIds.Chunk(MaximumBatch))
		{
			var response = await GetAsync<PlayerSummariesEnvelope>("ISteamUser/GetPlayerSummaries/v2/",
				$"steamids={string.Join(',', batch)}",
				cancellationToken);

			players.AddRange((response?.Response?.Players ?? []).Select(player => new SteamPlayerSummary(
				player.SteamId ?? string.Empty,
				player.PersonaName,
				player.PersonaState,
				int.TryParse(player.GameId, out var gameId) && gameId > 0 ? gameId : null,
				player.GameExtraInfo,
				player.AvatarMedium)));
		}

		return players;
	}

	public async Task<IReadOnlyList<string>?> GetFriendIdsAsync(string steamId, CancellationToken cancellationToken)
	{
		FriendListEnvelope? response;
		try
		{
			response = await GetAsync<FriendListEnvelope>("ISteamUser/GetFriendList/v1/",
				$"steamid={Uri.EscapeDataString(steamId)}&relationship=friend",
				cancellationToken);
		}
		catch (SteamWebApiException exception) when (exception.Failure == SteamWebFailure.InvalidKey)
		{
			return null;
		}

		return response?.FriendsList?.Friends is { } friends
			? [.. friends.Select(friend => friend.SteamId).OfType<string>()]
			: null;
	}

	public async Task<string?> ResolveVanityUrlAsync(string vanityName, CancellationToken cancellationToken)
	{
		var response = await GetAsync<VanityEnvelope>("ISteamUser/ResolveVanityURL/v1/",
			$"vanityurl={Uri.EscapeDataString(vanityName)}",
			cancellationToken);

		return response?.Response is { Success: 1, SteamId: { Length: > 0 } id } ? id : null;
	}

	public async Task<IReadOnlyList<SteamOwnedGame>?> GetOwnedGamesAsync(
		string steamId,
		CancellationToken cancellationToken)
	{
		var response = await GetAsync<OwnedGamesEnvelope>("IPlayerService/GetOwnedGames/v1/",
			$"steamid={Uri.EscapeDataString(steamId)}&include_appinfo=1&include_played_free_games=1",
			cancellationToken);

		// Steam answers a private game list with an empty response object, so only game_count tells it apart from
		// an empty library.
		if (response?.Response is not { GameCount: not null } body)
		{
			return null;
		}

		return
		[
			.. (body.Games ?? []).Where(game => game.AppId > 0)
			.Select(game => new SteamOwnedGame(game.AppId,
				string.IsNullOrWhiteSpace(game.Name) ? game.AppId.ToString(CultureInfo.InvariantCulture) : game.Name,
				Math.Max(0, game.PlaytimeForever),
				Math.Max(0, game.Playtime2Weeks),
				game.LastPlayed is > 0 and < 253402300799 ? DateTimeOffset.FromUnixTimeSeconds(game.LastPlayed) : null))
		];
	}

	public void Dispose()
	{
		_http.Dispose();
	}

	private async Task<T?> GetAsync<T>(string path, string query, CancellationToken cancellationToken)
	{
		try
		{
			using var response = await _http.GetAsync($"{path}?key={Uri.EscapeDataString(_key)}&{query}",
				cancellationToken);

			switch (response.StatusCode)
			{
				case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
					throw new SteamWebApiException(SteamWebFailure.InvalidKey);
				case HttpStatusCode.TooManyRequests:
					throw new SteamWebApiException(SteamWebFailure.RateLimited);
			}

			if (!response.IsSuccessStatusCode)
			{
				throw new SteamWebApiException(SteamWebFailure.Unavailable);
			}

			var content = await response.Content.ReadAsStringAsync(cancellationToken);
			return string.IsNullOrWhiteSpace(content) ? default : JsonSerializer.Deserialize<T>(content, _json);
		}
		catch (Exception exception) when (exception is HttpRequestException
				or JsonException
				or IOException
				or InvalidOperationException
			|| exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
		{
			// The request URL carries the key, so the original exception is not chained or logged.
			throw new SteamWebApiException(SteamWebFailure.Unavailable);
		}
	}

	private sealed record PlayerSummariesEnvelope(PlayerSummariesBody? Response);

	private sealed record PlayerSummariesBody(List<PlayerDto>? Players);

	private sealed record PlayerDto(
		[property: JsonPropertyName("steamid")]
		string? SteamId,
		[property: JsonPropertyName("personaname")]
		string? PersonaName,
		[property: JsonPropertyName("personastate")]
		int PersonaState,
		[property: JsonPropertyName("gameid")] string? GameId,
		[property: JsonPropertyName("gameextrainfo")]
		string? GameExtraInfo,
		[property: JsonPropertyName("avatarmedium")]
		string? AvatarMedium);

	private sealed record FriendListEnvelope(
		[property: JsonPropertyName("friendslist")]
		FriendListBody? FriendsList);

	private sealed record FriendListBody(List<FriendDto>? Friends);

	private sealed record FriendDto(
		[property: JsonPropertyName("steamid")]
		string? SteamId);

	private sealed record OwnedGamesEnvelope(OwnedGamesBody? Response);

	private sealed record OwnedGamesBody(
		[property: JsonPropertyName("game_count")]
		int? GameCount,
		[property: JsonPropertyName("games")] List<OwnedGameDto>? Games);

	private sealed record OwnedGameDto(
		[property: JsonPropertyName("appid")] int AppId,
		[property: JsonPropertyName("name")] string? Name,
		[property: JsonPropertyName("playtime_forever")]
		int PlaytimeForever,
		[property: JsonPropertyName("playtime_2weeks")]
		int Playtime2Weeks,
		[property: JsonPropertyName("rtime_last_played")]
		long LastPlayed);

	private sealed record VanityEnvelope(VanityBody? Response);

	private sealed record VanityBody(
		[property: JsonPropertyName("steamid")]
		string? SteamId,
		[property: JsonPropertyName("success")]
		int Success);
}
