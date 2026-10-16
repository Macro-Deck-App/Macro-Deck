using System.Net;
using System.Text;
using MacroDeckHost.Integrations.Steam.Protocol;
using MacroDeckHost.Integrations.Steam.State;

namespace MacroDeckHost.Tests.UnitTests.Steam;

[TestFixture]
internal sealed class SteamWebApiClientTests
{
	private const string Key = "SECRETKEY1234567890ABCDEF12345678";
	private const string SteamId = "76561198000000001";

	[Test]
	public async Task Player_summary_maps_state_and_current_game()
	{
		var handler = new StubHandler(_ => Json("""
												{"response":{"players":[{"steamid":"76561198000000001","personaname":"Ann","personastate":1,"gameid":"730","gameextrainfo":"Counter-Strike 2"}]}}
												"""));

		var summary = await Client(handler).GetPlayerSummaryAsync(SteamId, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(summary!.Name, Is.EqualTo("Ann"));
			Assert.That(summary.PersonaState, Is.EqualTo(1));
			Assert.That(summary.GameId, Is.EqualTo(730));
			Assert.That(summary.GameName, Is.EqualTo("Counter-Strike 2"));
		});
	}

	[Test]
	public async Task A_player_summary_carries_the_medium_avatar()
	{
		var handler = new StubHandler(_ => Json("""
												{"response":{"players":[{"steamid":"76561198000000001","personaname":"Ann","personastate":1,"avatar":"https://avatars.fastly.steamstatic.com/c5d56249ee5d28a07db4ac9f7f60af961fab5426.jpg","avatarmedium":"https://avatars.fastly.steamstatic.com/c5d56249ee5d28a07db4ac9f7f60af961fab5426_medium.jpg","avatarfull":"https://avatars.fastly.steamstatic.com/c5d56249ee5d28a07db4ac9f7f60af961fab5426_full.jpg"}]}}
												"""));

		var summary = await Client(handler).GetPlayerSummaryAsync(SteamId, CancellationToken.None);

		Assert.That(summary!.AvatarUrl,
			Is.EqualTo("https://avatars.fastly.steamstatic.com/c5d56249ee5d28a07db4ac9f7f60af961fab5426_medium.jpg"));
	}

	[Test]
	public async Task Owned_games_carry_their_playtime_and_when_they_were_last_played()
	{
		var handler = new StubHandler(_ => Json("""
												{"response":{"game_count":2,"games":[{"appid":730,"name":"Counter-Strike 2","playtime_forever":6000,"playtime_2weeks":90,"rtime_last_played":1700000000},{"appid":220,"name":"Half-Life 2","playtime_forever":0,"rtime_last_played":0}]}}
												"""));

		var games = await Client(handler).GetOwnedGamesAsync(SteamId, CancellationToken.None);

		Assert.That(games,
			Is.EqualTo(new[]
			{
				new SteamOwnedGame(730, "Counter-Strike 2", 6000, 90, DateTimeOffset.FromUnixTimeSeconds(1700000000)),
				new SteamOwnedGame(220, "Half-Life 2", 0, 0, null)
			}));
		Assert.That(handler.Requests.Single().Uri.AbsoluteUri, Does.Contain("IPlayerService/GetOwnedGames/v1/"));
	}

	[Test]
	public async Task Private_game_details_are_told_apart_from_an_empty_library()
	{
		var privateGames = await Client(new StubHandler(_ => Json("""{"response":{}}""")))
			.GetOwnedGamesAsync(SteamId, CancellationToken.None);
		var noGames = await Client(new StubHandler(_ => Json("""{"response":{"game_count":0}}""")))
			.GetOwnedGamesAsync(SteamId, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(privateGames, Is.Null);
			Assert.That(noGames, Is.Empty);
		});
	}

	[Test]
	public async Task A_player_who_hides_the_game_has_no_game()
	{
		var handler = new StubHandler(_ => Json("""
												{"response":{"players":[{"steamid":"76561198000000001","personaname":"Ann","personastate":1}]}}
												"""));

		var summary = await Client(handler).GetPlayerSummaryAsync(SteamId, CancellationToken.None);

		Assert.That(summary!.GameId, Is.Null);
	}

	[Test]
	public async Task An_unknown_profile_yields_no_summary()
	{
		var handler = new StubHandler(_ => Json("""{"response":{"players":[]}}"""));

		Assert.That(await Client(handler).GetPlayerSummaryAsync(SteamId, CancellationToken.None), Is.Null);
	}

	[TestCase(HttpStatusCode.Unauthorized)]
	[TestCase(HttpStatusCode.Forbidden)]
	public void A_rejected_key_is_reported_as_invalid(HttpStatusCode status)
	{
		var handler = new StubHandler(_ => new HttpResponseMessage(status));

		var exception = Assert.ThrowsAsync<SteamWebApiException>(() =>
			Client(handler).GetPlayerSummaryAsync(SteamId, CancellationToken.None));

		Assert.That(exception!.Failure, Is.EqualTo(SteamWebFailure.InvalidKey));
	}

	[Test]
	public void Too_many_requests_is_reported_as_rate_limiting()
	{
		var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

		var exception = Assert.ThrowsAsync<SteamWebApiException>(() =>
			Client(handler).GetPlayerSummaryAsync(SteamId, CancellationToken.None));

		Assert.That(exception!.Failure, Is.EqualTo(SteamWebFailure.RateLimited));
	}

	[TestCase(HttpStatusCode.InternalServerError)]
	[TestCase(HttpStatusCode.ServiceUnavailable)]
	public void Server_errors_are_reported_as_unavailable(HttpStatusCode status)
	{
		var handler = new StubHandler(_ => new HttpResponseMessage(status));

		var exception = Assert.ThrowsAsync<SteamWebApiException>(() =>
			Client(handler).GetPlayerSummaryAsync(SteamId, CancellationToken.None));

		Assert.That(exception!.Failure, Is.EqualTo(SteamWebFailure.Unavailable));
	}

	[Test]
	public void A_body_that_is_not_json_is_reported_as_unavailable()
	{
		var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent("<html>maintenance</html>")
		});

		var exception = Assert.ThrowsAsync<SteamWebApiException>(() =>
			Client(handler).GetPlayerSummaryAsync(SteamId, CancellationToken.None));

		Assert.That(exception!.Failure, Is.EqualTo(SteamWebFailure.Unavailable));
	}

	[Test]
	public void A_network_failure_neither_leaks_the_key_in_the_exception_nor_chains_the_original()
	{
		var handler = new StubHandler(request => throw new HttpRequestException($"failed {request.RequestUri}"));

		var exception = Assert.ThrowsAsync<SteamWebApiException>(() =>
			Client(handler).GetPlayerSummaryAsync(SteamId, CancellationToken.None));

		Assert.Multiple(() =>
		{
			Assert.That(exception!.Failure, Is.EqualTo(SteamWebFailure.Unavailable));
			Assert.That(exception.ToString(), Does.Not.Contain(Key));
			Assert.That(exception.InnerException, Is.Null);
		});
	}

	[Test]
	public async Task The_key_is_sent_only_as_the_key_query_parameter_over_https()
	{
		var handler = new StubHandler(_ => Json("""{"response":{"players":[]}}"""));

		await Client(handler).GetPlayerSummaryAsync(SteamId, CancellationToken.None);

		var request = handler.Requests.Single();
		Assert.Multiple(() =>
		{
			Assert.That(request.Uri.Scheme, Is.EqualTo("https"));
			Assert.That(request.Uri.Host, Is.EqualTo("api.steampowered.com"));
			Assert.That(request.Uri.Query, Does.Contain("key=" + Key));
			Assert.That(request.Headers, Does.Not.Contain(Key));
		});
	}

	[Test]
	public async Task More_than_one_hundred_friends_are_looked_up_in_batches_of_at_most_one_hundred()
	{
		var handler = new StubHandler(_ => Json("""{"response":{"players":[]}}"""));
		var ids = Enumerable.Range(0, 250)
			.Select(i => (76561198000000000 + i).ToString(global::System.Globalization.CultureInfo.InvariantCulture))
			.ToList();

		await Client(handler).GetPlayerSummariesAsync(ids, CancellationToken.None);

		var batches = handler.Requests
			.Select(request => request.Uri.Query.Split('&')
				.Single(part => part.StartsWith("steamids=", StringComparison.Ordinal)))
			.Select(part => part["steamids=".Length..].Split(',').Length)
			.ToList();
		Assert.That(batches, Is.EqualTo(new[] { 100, 100, 50 }));
	}

	[TestCase(HttpStatusCode.Unauthorized)]
	[TestCase(HttpStatusCode.Forbidden)]
	public async Task A_private_friend_list_is_reported_as_unavailable_rather_than_empty(HttpStatusCode status)
	{
		var handler = new StubHandler(_ => new HttpResponseMessage(status));

		Assert.That(await Client(handler).GetFriendIdsAsync(SteamId, CancellationToken.None), Is.Null);
	}

	[Test]
	public async Task A_friend_list_with_no_friends_is_an_empty_list()
	{
		var handler = new StubHandler(_ => Json("""{"friendslist":{"friends":[]}}"""));

		Assert.That(await Client(handler).GetFriendIdsAsync(SteamId, CancellationToken.None), Is.Empty);
	}

	[Test]
	public async Task Friend_ids_are_returned()
	{
		var handler = new StubHandler(_ => Json("""
												{"friendslist":{"friends":[{"steamid":"1","relationship":"friend"},{"steamid":"2","relationship":"friend"}]}}
												"""));

		Assert.That(await Client(handler).GetFriendIdsAsync(SteamId, CancellationToken.None),
			Is.EqualTo(new[] { "1", "2" }));
	}

	[Test]
	public async Task A_vanity_name_resolves_to_a_steam_id()
	{
		var handler = new StubHandler(_ => Json("""{"response":{"steamid":"76561198000000001","success":1}}"""));

		Assert.That(await Client(handler).ResolveVanityUrlAsync("ann", CancellationToken.None), Is.EqualTo(SteamId));
	}

	[Test]
	public async Task An_unknown_vanity_name_resolves_to_nothing()
	{
		var handler = new StubHandler(_ => Json("""{"response":{"success":42,"message":"No match"}}"""));

		Assert.That(await Client(handler).ResolveVanityUrlAsync("nobody", CancellationToken.None), Is.Null);
	}

	private static SteamWebApiClient Client(StubHandler handler)
	{
		return new SteamWebApiClient(Key, new HttpClient(handler));
	}

	private static HttpResponseMessage Json(string body)
	{
		return new HttpResponseMessage(HttpStatusCode.OK)
			{ Content = new StringContent(body, Encoding.UTF8, "application/json") };
	}

	private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
	{
		public List<(Uri Uri, string Headers)> Requests { get; } = [];

		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			Requests.Add((request.RequestUri!, request.Headers.ToString()));
			return Task.FromResult(respond(request));
		}
	}
}
