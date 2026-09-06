using System.Net;
using MacroDeckHost.Integrations.Spotify;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Issues;
using SpotifyAPI.Web.Http;

namespace MacroDeckHost.Tests.UnitTests.Spotify;

[TestFixture]
internal sealed class SpotifyIntegrationIssueTests
{
	[Test]
	public async Task An_invalid_grant_refresh_surfaces_the_reconnect_issue()
	{
		var oauth = new FakeSpotifyOAuthClient();
		oauth.Results.Enqueue(FakeSpotifyOAuthClient.Rejected());
		var integration = new SpotifyIntegration(oauth, OfflineTransport());
		var config = new SpotifyConfigStub().WithExpiredAccessToken();
		await integration.InitializeAsync(new SpotifyContextStub(config));

		await PollAsync(integration);
		var issues = await integration.GetIssuesAsync();

		Assert.Multiple(() =>
		{
			Assert.That(issues, Has.Count.EqualTo(1));
			Assert.That(issues[0].Id, Is.EqualTo("credentials-invalid"));
			Assert.That(TestLocalization.Resolve(issues[0].ActionLabel), Is.EqualTo("Reconnect"));
		});

		await integration.ShutdownAsync();
	}

	[Test]
	public async Task A_temporarily_unavailable_token_endpoint_surfaces_no_issue()
	{
		var oauth = new FakeSpotifyOAuthClient();
		oauth.Results.Enqueue(FakeSpotifyOAuthClient.Unreachable());
		var integration = new SpotifyIntegration(oauth, OfflineTransport());
		var config = new SpotifyConfigStub().WithExpiredAccessToken();
		await integration.InitializeAsync(new SpotifyContextStub(config));

		await PollAsync(integration);

		Assert.That(await integration.GetIssuesAsync(), Is.Empty);
		await integration.ShutdownAsync();
	}

	[Test]
	public async Task Resolving_the_credentials_issue_starts_the_config_flow()
	{
		var integration = new SpotifyIntegration(new FakeSpotifyOAuthClient(), OfflineTransport());

		var resolution = await integration.ResolveIssueAsync("credentials-invalid");

		Assert.Multiple(() =>
		{
			Assert.That(resolution.Success, Is.True);
			Assert.That(resolution.FollowUp, Is.EqualTo(IssueResolutionFollowUp.StartConfigFlow));
		});
	}

	[Test]
	public async Task A_successful_refresh_restores_spotify_without_recreating_anything()
	{
		var oauth = new FakeSpotifyOAuthClient();
		var integration = new SpotifyIntegration(oauth, OfflineTransport());
		var config = new SpotifyConfigStub().WithExpiredAccessToken();
		await integration.InitializeAsync(new SpotifyContextStub(config));

		await PollAsync(integration);
		var issues = await integration.GetIssuesAsync();
		await integration.ShutdownAsync();

		Assert.Multiple(() =>
		{
			Assert.That(oauth.RefreshCount, Is.EqualTo(1));
			Assert.That(issues, Is.Empty);
			Assert.That(config.Secrets[SpotifyConfigKeys.RefreshToken], Is.EqualTo("refresh-1"));
		});
	}

	[Test]
	public async Task A_complete_grant_raises_no_issue()
	{
		var integration = new SpotifyIntegration(new FakeSpotifyOAuthClient(), OfflineTransport());
		var config = new SpotifyConfigStub();
		await integration.InitializeAsync(new SpotifyContextStub(config));

		Assert.That(await integration.GetIssuesAsync(), Is.Empty);

		await integration.ShutdownAsync();
	}

	[Test]
	public async Task An_outdated_grant_surfaces_the_scopes_outdated_issue()
	{
		var integration = new SpotifyIntegration(new FakeSpotifyOAuthClient(), OfflineTransport());
		var config = new SpotifyConfigStub().WithOutdatedScopes();
		await integration.InitializeAsync(new SpotifyContextStub(config));

		var issues = await integration.GetIssuesAsync();

		Assert.Multiple(() =>
		{
			Assert.That(issues, Has.Count.EqualTo(1));
			Assert.That(issues[0].Id, Is.EqualTo("scopes-outdated"));
			Assert.That(TestLocalization.Resolve(issues[0].ActionLabel), Is.EqualTo("Reconnect"));
		});

		await integration.ShutdownAsync();
	}

	[Test]
	public async Task A_missing_stored_scope_value_is_treated_as_outdated()
	{
		var integration = new SpotifyIntegration(new FakeSpotifyOAuthClient(), OfflineTransport());
		var config = new SpotifyConfigStub().WithoutStoredScope();
		await integration.InitializeAsync(new SpotifyContextStub(config));

		var issues = await integration.GetIssuesAsync();

		Assert.Multiple(() =>
		{
			Assert.That(issues, Has.Count.EqualTo(1));
			Assert.That(issues[0].Id, Is.EqualTo("scopes-outdated"));
		});

		await integration.ShutdownAsync();
	}

	[Test]
	public async Task Invalid_credentials_take_precedence_over_outdated_scopes()
	{
		var oauth = new FakeSpotifyOAuthClient();
		oauth.Results.Enqueue(FakeSpotifyOAuthClient.Rejected());
		var integration = new SpotifyIntegration(oauth, OfflineTransport());
		var config = new SpotifyConfigStub().WithOutdatedScopes().WithExpiredAccessToken();
		await integration.InitializeAsync(new SpotifyContextStub(config));

		await PollAsync(integration);
		var issues = await integration.GetIssuesAsync();

		Assert.Multiple(() =>
		{
			Assert.That(issues, Has.Count.EqualTo(1));
			Assert.That(issues[0].Id, Is.EqualTo("credentials-invalid"));
		});

		await integration.ShutdownAsync();
	}

	[Test]
	public async Task The_scopes_outdated_issue_clears_after_shutdown()
	{
		var integration = new SpotifyIntegration(new FakeSpotifyOAuthClient(), OfflineTransport());
		var config = new SpotifyConfigStub().WithOutdatedScopes();
		await integration.InitializeAsync(new SpotifyContextStub(config));
		Assert.That(await integration.GetIssuesAsync(), Has.Count.EqualTo(1));

		await integration.ShutdownAsync();

		Assert.That(await integration.GetIssuesAsync(), Is.Empty);
	}

	[Test]
	public async Task Resolving_the_scopes_outdated_issue_starts_the_config_flow()
	{
		var integration = new SpotifyIntegration(new FakeSpotifyOAuthClient(), OfflineTransport());

		var resolution = await integration.ResolveIssueAsync("scopes-outdated");

		Assert.Multiple(() =>
		{
			Assert.That(resolution.Success, Is.True);
			Assert.That(resolution.FollowUp, Is.EqualTo(IssueResolutionFollowUp.StartConfigFlow));
		});
	}

	[Test]
	public async Task A_long_unreachable_episode_surfaces_the_informational_issue()
	{
		var oauth = new FakeSpotifyOAuthClient();
		oauth.Results.Enqueue(FakeSpotifyOAuthClient.Unreachable());
		var integration = new SpotifyIntegration(oauth, OfflineTransport(), unreachableIssueThreshold: TimeSpan.Zero);
		var config = new SpotifyConfigStub().WithExpiredAccessToken();
		await integration.InitializeAsync(new SpotifyContextStub(config));

		await PollAsync(integration);
		var issues = await integration.GetIssuesAsync();

		Assert.Multiple(() =>
		{
			Assert.That(issues, Has.Count.EqualTo(1));
			Assert.That(issues[0].Id, Is.EqualTo("temporarily-unreachable"));
			Assert.That(issues[0].Severity, Is.EqualTo(IntegrationIssueSeverity.Info));
			// Informational: no Reconnect button - re-authorizing cannot fix an unreachable endpoint.
			Assert.That(TestLocalization.Resolve(issues[0].ActionLabel), Is.Null);
		});

		await integration.ShutdownAsync();
	}

	[Test]
	public async Task The_unreachable_issue_clears_once_polls_succeed()
	{
		var transport = new SwitchableTransport { Fail = new HttpRequestException("connection reset") };
		var integration = new SpotifyIntegration(new FakeSpotifyOAuthClient(),
			transport,
			unreachableIssueThreshold: TimeSpan.Zero);
		var config = new SpotifyConfigStub();
		await integration.InitializeAsync(new SpotifyContextStub(config));

		await PollAsync(integration);
		Assert.That(await integration.GetIssuesAsync(), Has.Count.EqualTo(1));

		transport.Fail = null;
		await PollAsync(integration);

		Assert.That(await integration.GetIssuesAsync(), Is.Empty);
		await integration.ShutdownAsync();
	}

	[Test]
	public async Task Invalid_credentials_take_precedence_over_the_unreachable_issue()
	{
		var transport = new SwitchableTransport { Fail = new HttpRequestException("connection reset") };
		var integration = new SpotifyIntegration(new FakeSpotifyOAuthClient(),
			transport,
			unreachableIssueThreshold: TimeSpan.Zero);
		var config = new SpotifyConfigStub();
		await integration.InitializeAsync(new SpotifyContextStub(config));

		await PollAsync(integration);
		transport.Fail = null;
		transport.Unauthorized = true;
		await PollAsync(integration);

		var issues = await integration.GetIssuesAsync();

		Assert.Multiple(() =>
		{
			Assert.That(issues, Has.Count.EqualTo(1));
			Assert.That(issues[0].Id, Is.EqualTo("credentials-invalid"));
		});

		await integration.ShutdownAsync();
	}

	[Test]
	public async Task A_premium_only_refusal_surfaces_the_premium_required_issue()
	{
		var transport = new SwitchableTransport { PremiumRequired = true };
		var integration = new SpotifyIntegration(new FakeSpotifyOAuthClient(), transport);
		await integration.InitializeAsync(new SpotifyContextStub(new SpotifyConfigStub()));

		await PlayAsync(integration);
		var issues = await integration.GetIssuesAsync();

		Assert.Multiple(() =>
		{
			Assert.That(issues, Has.Count.EqualTo(1));
			Assert.That(issues[0].Id, Is.EqualTo("premium-required"));
			Assert.That(TestLocalization.Resolve(issues[0].ActionLabel), Is.EqualTo("Reconnect"));
			Assert.That(TestLocalization.Resolve(issues[0].Description), Does.Contain("Premium"));
			Assert.That(TestLocalization.Resolve(issues[0].Description),
				Does.Contain("https://developer.spotify.com/documentation/web-api"));
		});

		await integration.ShutdownAsync();
	}

	[Test]
	public async Task The_premium_required_issue_clears_once_a_command_succeeds()
	{
		var transport = new SwitchableTransport { PremiumRequired = true };
		var integration = new SpotifyIntegration(new FakeSpotifyOAuthClient(), transport);
		await integration.InitializeAsync(new SpotifyContextStub(new SpotifyConfigStub()));

		await PlayAsync(integration);
		Assert.That(await integration.GetIssuesAsync(), Has.Count.EqualTo(1));

		// An upgraded account must not stay accused until the next host restart.
		transport.PremiumRequired = false;
		await PlayAsync(integration);

		Assert.That(await integration.GetIssuesAsync(), Is.Empty);
		await integration.ShutdownAsync();
	}

	[Test]
	public async Task A_command_that_only_lacks_a_device_raises_no_issue()
	{
		var transport = new SwitchableTransport { NoActiveDevice = true };
		var integration = new SpotifyIntegration(new FakeSpotifyOAuthClient(), transport);
		await integration.InitializeAsync(new SpotifyContextStub(new SpotifyConfigStub()));

		await PlayAsync(integration);

		Assert.That(await integration.GetIssuesAsync(), Is.Empty);
		await integration.ShutdownAsync();
	}

	[Test]
	public async Task Resolving_the_premium_required_issue_starts_the_config_flow()
	{
		var integration = new SpotifyIntegration(new FakeSpotifyOAuthClient(), OfflineTransport());

		var resolution = await integration.ResolveIssueAsync("premium-required");

		Assert.Multiple(() =>
		{
			Assert.That(resolution.Success, Is.True);
			Assert.That(resolution.FollowUp, Is.EqualTo(IssueResolutionFollowUp.StartConfigFlow));
		});
	}

	private static async Task PlayAsync(SpotifyIntegration integration)
	{
		var player = integration.GetPlayer(new SpotifyConfigStub().EntryId.ToString())!;
		await player.PlayAsync();
	}

	private static async Task PollAsync(SpotifyIntegration integration)
	{
		var player = (SpotifyMusicPlayer)integration.GetPlayer(new SpotifyConfigStub().EntryId.ToString())!;
		await player.RefreshStateAsync();
	}

	private static IdleTransport OfflineTransport() => new();

	private sealed class SwitchableTransport : IHTTPClient
	{
		public Exception? Fail { get; set; }

		public bool Unauthorized { get; set; }

		public bool PremiumRequired { get; set; }

		public bool NoActiveDevice { get; set; }

		public Task<IResponse> DoRequest(IRequest request, CancellationToken cancel)
		{
			if (Fail is not null)
			{
				throw Fail;
			}

			if (PremiumRequired)
			{
				return Task.FromResult<IResponse>(Error(HttpStatusCode.Forbidden,
					"""{"error":{"status":403,"message":"Player command failed: Premium required","reason":"PREMIUM_REQUIRED"}}"""));
			}

			if (NoActiveDevice)
			{
				return Task.FromResult<IResponse>(Error(HttpStatusCode.NotFound,
					"""{"error":{"status":404,"message":"Player command failed: No active device found","reason":"NO_ACTIVE_DEVICE"}}"""));
			}

			return Task.FromResult<IResponse>(new Response(new Dictionary<string, string>())
			{
				StatusCode = Unauthorized ? HttpStatusCode.Unauthorized : HttpStatusCode.NoContent,
				ContentType = "application/json",
				Body = Unauthorized ? """{"error":{"status":401,"message":"Access token missing"}}""" : string.Empty
			});
		}

		private static Response Error(HttpStatusCode statusCode, string body)
			=> new(new Dictionary<string, string>())
			{
				StatusCode = statusCode, ContentType = "application/json", Body = body
			};

		public void SetRequestTimeout(TimeSpan timeout)
		{
		}

		public void Dispose()
		{
		}
	}

	private sealed class IdleTransport : IHTTPClient
	{
		public Task<IResponse> DoRequest(IRequest request, CancellationToken cancel)
			=> Task.FromResult<IResponse>(new Response(new Dictionary<string, string>())
			{
				StatusCode = HttpStatusCode.NoContent,
				ContentType = "application/json",
				Body = string.Empty
			});

		public void SetRequestTimeout(TimeSpan timeout)
		{
		}

		public void Dispose()
		{
		}
	}
}
