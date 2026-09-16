using System.Net;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer.Actions;
using MacroDeckHost.Integrations.Spotify;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using SpotifyAPI.Web.Http;

namespace MacroDeckHost.Tests.UnitTests.Spotify;

public class SpotifyIntegrationTests
{
	private static readonly Guid _entryId = Guid.Parse("22222222-2222-2222-2222-222222222222");

	[Test]
	public void Spotify_allows_only_a_single_configuration()
	{
		Assert.That(new SpotifyIntegration().AllowsMultipleConfigurations, Is.False);
	}

	[Test]
	public void Refresh_state_keeps_its_stored_action_contract()
	{
		var action = new SpotifyIntegration().Actions.Single(candidate => candidate.Id == "refresh-state");

		Assert.Multiple(() =>
		{
			Assert.That(TestLocalization.Resolve(action.Name), Is.EqualTo("Refresh State"));
			Assert.That(TestLocalization.Resolve(action.Description),
				Is.EqualTo("Force a refresh of the now-playing state"));
			Assert.That(action.Parameters, Has.Count.EqualTo(1));
			Assert.That(action.Parameters[0].Name, Is.EqualTo("instance"));
		});
	}

	[Test]
	public async Task Refresh_state_action_executes_a_fresh_Spotify_poll()
	{
		var transport = new IdleTransport();
		var integration = new SpotifyIntegration(new FakeSpotifyOAuthClient(), transport);
		await integration.InitializeAsync(new SpotifyContextStub(new SpotifyConfigStub()));
		await WaitForRequestCountAsync(transport, 1);
		var before = transport.RequestCount;
		var action = integration.Actions.Single(candidate => candidate.Id == "refresh-state");

		var result = await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>
			{
				[MusicPlayerActions.InstanceParameterName] = _entryId.ToString()
			}
		});

		await WaitForRequestCountAsync(transport, before + 1);
		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(transport.RequestCount, Is.EqualTo(before + 1));
		});
		await integration.ShutdownAsync();
	}

	[Test]
	public void Local_playback_is_absent_from_the_published_integration_surface()
	{
		var integration = new SpotifyIntegration();
		var spotifyTypes = typeof(SpotifyIntegration).Assembly.GetTypes()
			.Where(type => type.Namespace?.Contains("Spotify", StringComparison.Ordinal) == true)
			.Select(type => type.FullName)
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(integration.Variables.Select(variable => variable.Name),
				Does.Not.Contain("spotify_local_playback_active"));
			Assert.That(spotifyTypes,
				Has.None.Matches<string>(name => name is not null &&
					(name.Contains("LocalPlayback", StringComparison.Ordinal) ||
						name.Contains("WindowsMediaControl", StringComparison.Ordinal))));
		});
	}

	[Test]
	public async Task Configured_instance_returns_with_the_same_id_after_disable_and_restart()
	{
		var config = new SpotifyConfigStub();
		var context = new SpotifyContextStub(config);
		var integration = NewIntegration();

		await integration.InitializeAsync(context);
		Assert.That(integration.GetInstances().Single().Id, Is.EqualTo(_entryId.ToString()));

		await integration.ShutdownAsync();
		Assert.Multiple(() =>
		{
			Assert.That(integration.IsInitialized, Is.False);
			Assert.That(integration.GetInstances(), Is.Empty);
		});

		var restarted = NewIntegration();
		await restarted.InitializeAsync(context);

		Assert.That(restarted.GetInstances().Single().Id, Is.EqualTo(_entryId.ToString()));
		await restarted.ShutdownAsync();
	}

	[Test]
	public async Task Shutdown_waits_for_overlapping_initialization_and_leaves_no_live_generation()
	{
		var loadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var config = new SpotifyConfigStub
		{
			BeforeGetEntries = async () =>
			{
				loadStarted.TrySetResult();
				await releaseLoad.Task.WaitAsync(TimeSpan.FromSeconds(5));
			}
		};
		var integration = NewIntegration();

		var initializing = integration.InitializeAsync(new SpotifyContextStub(config));
		await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var stopping = integration.ShutdownAsync();
		Assert.That(stopping.IsCompleted, Is.False);

		releaseLoad.SetResult();
		await Task.WhenAll(initializing, stopping).WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(integration.IsInitialized, Is.False);
			Assert.That(integration.GetInstances(), Is.Empty);
		});

		await integration.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
	}

	[Test]
	public async Task Shutdown_while_a_refresh_is_in_flight_stores_the_rotated_token()
	{
		var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var oauth = new FakeSpotifyOAuthClient
		{
			BeforeRefresh = () =>
			{
				arrived.TrySetResult();
				return held.Task;
			}
		};
		var config = new SpotifyConfigStub().WithExpiredAccessToken();
		var integration = new SpotifyIntegration(oauth, new IdleTransport());
		await integration.InitializeAsync(new SpotifyContextStub(config));

		var polling = integration.GetPlayer(_entryId.ToString())!.GetStateAsync();
		await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));

		var shutdown = integration.ShutdownAsync();
		held.SetResult();
		await Task.WhenAll(polling, shutdown).WaitAsync(TimeSpan.FromSeconds(10));

		Assert.That(config.Secrets[SpotifyConfigKeys.RefreshToken], Is.EqualTo("refresh-1"));
	}

	[Test]
	public async Task Reconfiguration_while_a_refresh_is_in_flight_completes_within_the_drain_bound()
	{
		var oauth = new FakeSpotifyOAuthClient { Stall = true };
		var integration = new SpotifyIntegration(oauth, new IdleTransport());
		await integration.InitializeAsync(new SpotifyContextStub(new SpotifyConfigStub().WithExpiredAccessToken()));

		_ = integration.GetPlayer(_entryId.ToString())!.GetStateAsync();
		for (var attempt = 0; attempt < 100 && oauth.RefreshCount == 0; attempt++)
		{
			await Task.Delay(20);
		}

		await integration.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(20));
		await integration.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(20));

		Assert.That(integration.IsInitialized, Is.False);
	}

	[Test]
	public async Task A_wedged_token_write_cannot_park_the_lifecycle_forever()
	{
		var wedged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var config = new SpotifyConfigStub { BeforeSecretWrite = _ => wedged.Task }.WithExpiredAccessToken();
		var integration = new SpotifyIntegration(new FakeSpotifyOAuthClient(),
			new IdleTransport(),
			persisterDrainTimeout: TimeSpan.FromMilliseconds(200),
			persistenceBudget: TimeSpan.FromMilliseconds(200));
		await integration.InitializeAsync(new SpotifyContextStub(config));

		await integration.GetPlayer(_entryId.ToString())!.GetStateAsync();

		await integration.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
		await integration.InitializeAsync(new SpotifyContextStub(new SpotifyConfigStub()));

		Assert.That(integration.IsInitialized, Is.True);

		wedged.SetResult();
		await integration.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
	}

	[Test]
	public async Task Podcast_playback_fills_the_track_variables_and_bounds_the_position()
	{
		var integration = new SpotifyIntegration(new FakeSpotifyOAuthClient(), new PodcastTransport());
		await integration.InitializeAsync(new SpotifyContextStub(new SpotifyConfigStub()));
		await WaitForPublishedEpisodeAsync(integration);

		var title = await integration.ReadAsync("spotify-current-track-name");
		var artist = await integration.ReadAsync("spotify-current-artist");
		var album = await integration.ReadAsync("spotify-current-album");
		var duration = await integration.ReadAsync("spotify-track-duration");
		var progress = await integration.ReadAsync("spotify-progress-percentage");
		var position = await integration.ReadAsync("spotify-current-position");
		var url = await integration.ReadAsync("spotify-current-track-url");

		Assert.Multiple(() =>
		{
			Assert.That(title.Value, Is.EqualTo("Some Episode"));
			Assert.That(artist.Value, Is.EqualTo("The Show"));
			Assert.That(album.Value, Is.EqualTo("The Show"));
			Assert.That(duration.Value, Is.EqualTo(300));
			Assert.That(progress.Value, Is.EqualTo(20));
			Assert.That(position.Max, Is.EqualTo(300));
			Assert.That(url.Value, Is.EqualTo("https://open.spotify.com/episode/episode-1"));
		});

		await integration.ShutdownAsync();
	}

	private static SpotifyIntegration NewIntegration()
		=> new(new FakeSpotifyOAuthClient(), new IdleTransport());

	private static async Task WaitForPublishedEpisodeAsync(SpotifyIntegration integration)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
		while (DateTime.UtcNow < deadline)
		{
			if ((await integration.ReadAsync("spotify-current-track-name")).Value is not null)
			{
				return;
			}

			await Task.Delay(1);
		}

		Assert.Fail("the Spotify poll never published the playing episode");
	}

	private static async Task WaitForRequestCountAsync(IdleTransport transport, int expected)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
		while (transport.RequestCount < expected && DateTime.UtcNow < deadline)
		{
			await Task.Delay(1);
		}

		Assert.That(transport.RequestCount, Is.GreaterThanOrEqualTo(expected));
	}

	private sealed class PodcastTransport : IHTTPClient
	{
		private const string WithoutEpisodeOptIn =
			"""
			{
			  "device": { "id": "dev-1", "is_active": true, "name": "Desk", "type": "Computer",
			    "volume_percent": 55 },
			  "repeat_state": "off",
			  "shuffle_state": false,
			  "progress_ms": 60000,
			  "is_playing": true,
			  "currently_playing_type": "episode",
			  "item": null
			}
			""";

		private const string WithEpisodeOptIn =
			"""
			{
			  "device": { "id": "dev-1", "is_active": true, "name": "Desk", "type": "Computer",
			    "volume_percent": 55 },
			  "repeat_state": "off",
			  "shuffle_state": false,
			  "progress_ms": 60000,
			  "is_playing": true,
			  "currently_playing_type": "episode",
			  "item": { "type": "episode", "id": "episode-1", "uri": "spotify:episode:episode-1",
			    "name": "Some Episode", "duration_ms": 300000, "show": { "name": "The Show" }, "images": [],
			    "external_urls": { "spotify": "https://open.spotify.com/episode/episode-1" } }
			}
			""";

		public Task<IResponse> DoRequest(IRequest request, CancellationToken cancel)
		{
			var asksForEpisodes = request.Parameters.TryGetValue("additional_types", out var types) &&
				types.Contains("episode", StringComparison.Ordinal);

			return Task.FromResult<IResponse>(new Response(new Dictionary<string, string>())
			{
				StatusCode = HttpStatusCode.OK,
				ContentType = "application/json",
				Body = asksForEpisodes ? WithEpisodeOptIn : WithoutEpisodeOptIn
			});
		}

		public void SetRequestTimeout(TimeSpan timeout)
		{
		}

		public void Dispose()
		{
		}
	}

	private sealed class IdleTransport : IHTTPClient
	{
		private int _requestCount;

		public int RequestCount => Volatile.Read(ref _requestCount);

		public Task<IResponse> DoRequest(IRequest request, CancellationToken cancel)
		{
			if (request.Method == HttpMethod.Get &&
				request.Endpoint.ToString().Contains("me/player", StringComparison.Ordinal) &&
				!request.Endpoint.ToString().Contains("devices", StringComparison.Ordinal))
			{
				Interlocked.Increment(ref _requestCount);
			}

			return Task.FromResult<IResponse>(new Response(new Dictionary<string, string>())
			{
				StatusCode = HttpStatusCode.NoContent,
				ContentType = "application/json",
				Body = string.Empty
			});
		}

		public void SetRequestTimeout(TimeSpan timeout)
		{
		}

		public void Dispose()
		{
		}
	}
}
