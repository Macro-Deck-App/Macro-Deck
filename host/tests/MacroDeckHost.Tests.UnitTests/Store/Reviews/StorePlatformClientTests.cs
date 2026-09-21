using System.Net;
using System.Text;
using System.Text.Json;
using MacroDeckHost.Application.Connect;
using MacroDeckHost.Application.Store.Reviews;
using MacroDeckHost.Infrastructure.Store;
using MacroDeckHost.Tests.UnitTests.Connect;

namespace MacroDeckHost.Tests.UnitTests.Store.Reviews;

[TestFixture]
internal sealed class StorePlatformClientTests
{
	private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

	private RecordingHandler _handler = null!;
	private TokenSession _session = null!;
	private StorePlatformClient _client = null!;

	[SetUp]
	public void SetUp()
	{
		_handler = new RecordingHandler();
		_session = new TokenSession { Current = FakeConnectSessionService.SignedIn(null) };
		_client = new StorePlatformClient(_handler, _session, StorePlatformOptions.Default, Serilog.Core.Logger.None);
	}

	[TearDown]
	public void TearDown()
	{
		_client.Dispose();
		_handler.Dispose();
	}

	[Test]
	public async Task Ratings_for_more_than_a_hundred_packages_are_requested_in_batches_and_merged()
	{
		var ids = Enumerable.Range(1, 250).Select(number => $"com.acme.p{number}").ToList();
		_handler.Respond = request =>
		{
			var requested = Uri.UnescapeDataString(request.RequestUri!.Query).Split('=')[1].Split(',');
			return Json(requested.ToDictionary(id => id, _ => new { rating = 4.5, ratingCount = 2, distribution = Array.Empty<object>() }));
		};

		var result = await _client.GetRatings(ids);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Value!.Keys, Is.EquivalentTo(ids));
			Assert.That(_handler.Requests.Select(request => request.Uri.Query.Split(',').Length),
				Is.EqualTo(new[] { 100, 100, 50 }));
			Assert.That(_handler.Requests.All(request => request.Authorization is null), Is.True,
				"public reads must not carry the account token");
		});
	}

	[Test]
	public async Task A_claim_for_more_than_a_hundred_packages_is_split_and_sends_the_bearer_token()
	{
		var ids = Enumerable.Range(1, 150).Select(number => $"com.acme.p{number}").ToList();
		_handler.Respond = request =>
		{
			var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().Result).RootElement;
			return Json(body.GetProperty("packageIds").EnumerateArray().ToDictionary(id => id.GetString()!, _ => "Claimed"));
		};

		var result = await _client.ClaimEntitlements(ids);

		Assert.Multiple(() =>
		{
			Assert.That(result.Value!.Values, Is.All.EqualTo(StoreEntitlementClaimStatus.Claimed));
			Assert.That(result.Value!.Count, Is.EqualTo(150));
			Assert.That(_handler.Requests.Select(request => request.Uri.AbsolutePath),
				Is.All.EqualTo("/api/v1/store/entitlements/claim"));
			Assert.That(_handler.Requests.Count, Is.EqualTo(2));
			Assert.That(_handler.Requests.Select(request => request.Authorization), Is.All.EqualTo("Bearer access-token"));
		});
	}

	[Test]
	public async Task Writing_a_review_sends_the_whole_review_including_a_cleared_title()
	{
		_handler.Respond = _ => Json(new
		{
			id = Guid.NewGuid(), packageId = "com.acme.hue", rating = 4, title = (string?)null, body = "Works well",
			visibility = "Visible", downloadedBeforeReview = false, createdAt = DateTimeOffset.UnixEpoch,
			updatedAt = DateTimeOffset.UnixEpoch, isEdited = true, moderationReason = (string?)null
		}, HttpStatusCode.OK);

		var result = await _client.PutOwnReview("com.acme.hue", 4, null, "Works well");
		var sent = JsonDocument.Parse(_handler.Requests.Single().Body!).RootElement;

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_handler.Requests.Single().Method, Is.EqualTo("PUT"));
			Assert.That(_handler.Requests.Single().Uri.AbsolutePath, Is.EqualTo("/api/v1/store/packages/com.acme.hue/reviews/me"));
			Assert.That(sent.GetProperty("rating").GetInt32(), Is.EqualTo(4));
			Assert.That(sent.GetProperty("title").ValueKind, Is.EqualTo(JsonValueKind.Null));
			Assert.That(sent.GetProperty("body").GetString(), Is.EqualTo("Works well"));
		});
	}

	private static IEnumerable<TestCaseData> WriteFailures()
	{
		yield return new TestCaseData(HttpStatusCode.Forbidden, "{\"downloadRequired\":true}", null, StorePlatformFailure.DownloadRequired, null);
		yield return new TestCaseData(HttpStatusCode.Forbidden, "{\"accountSuspended\":true}", null, StorePlatformFailure.AccountSuspended, null);
		yield return new TestCaseData(HttpStatusCode.Forbidden, "{\"title\":\"no\"}", null, StorePlatformFailure.Forbidden, null);
		yield return new TestCaseData(HttpStatusCode.Conflict, "{}", null, StorePlatformFailure.Moderated, null);
		yield return new TestCaseData(HttpStatusCode.Gone, "", null, StorePlatformFailure.Gone, null);
		yield return new TestCaseData(HttpStatusCode.TooManyRequests, "{}", "60", StorePlatformFailure.Cooldown, 60);
		yield return new TestCaseData(HttpStatusCode.TooManyRequests, "", null, StorePlatformFailure.Cooldown, null);
		yield return new TestCaseData(HttpStatusCode.ServiceUnavailable, "{}", "30", StorePlatformFailure.RetryLater, 30);
		yield return new TestCaseData(HttpStatusCode.BadRequest, "{\"errors\":{\"Title\":[\"too long\"]}}", null, StorePlatformFailure.Validation, null);
		yield return new TestCaseData(HttpStatusCode.Unauthorized, "", null, StorePlatformFailure.Unavailable, null);
		yield return new TestCaseData(HttpStatusCode.InternalServerError, "", null, StorePlatformFailure.Unavailable, null);
	}

	[TestCaseSource(nameof(WriteFailures))]
	public async Task Every_refused_write_is_reported_as_what_the_platform_meant(HttpStatusCode status,
		string body,
		string? retryAfter,
		StorePlatformFailure expected,
		int? expectedRetryAfterSeconds)
	{
		_handler.Respond = _ =>
		{
			var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/problem+json") };
			if (retryAfter is not null)
			{
				response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
			}

			return response;
		};

		var result = await _client.PutOwnReview("com.acme.hue", 5, "Great", null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Failure, Is.EqualTo(expected));
			Assert.That(result.RetryAfter?.TotalSeconds, Is.EqualTo(expectedRetryAfterSeconds));
			Assert.That(result.Field, Is.EqualTo(expected == StorePlatformFailure.Validation ? "Title" : null));
		});
	}

	[Test]
	public async Task Reports_are_posted_for_the_signed_in_account_with_their_reason_and_detail()
	{
		var reviewId = Guid.Parse("6f1c1a4e-2b0d-4c55-9f0e-6f39b6a1d2c3");
		_handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.NoContent);

		var entry = await _client.ReportPackage("com.acme.hue", "Misleading", "Claims features it lacks");
		var review = await _client.ReportReview("com.acme.hue", reviewId, "Spam", null);

		Assert.Multiple(() =>
		{
			Assert.That(entry.Success, Is.True);
			Assert.That(review.Success, Is.True);
			Assert.That(_handler.Requests.Select(request => (request.Method, request.Uri.AbsolutePath)), Is.EqualTo(new[]
			{
				("POST", "/api/v1/store/packages/com.acme.hue/report"),
				("POST", $"/api/v1/store/packages/com.acme.hue/reviews/{reviewId:D}/report")
			}));
			Assert.That(_handler.Requests.All(request => request.Authorization == "Bearer access-token"), Is.True);
			var entryBody = JsonSerializer.Deserialize<JsonElement>(_handler.Requests[0].Body!, _json);
			Assert.That(entryBody.GetProperty("category").GetString(), Is.EqualTo("Misleading"));
			Assert.That(entryBody.GetProperty("detail").GetString(), Is.EqualTo("Claims features it lacks"));
			var reviewBody = JsonSerializer.Deserialize<JsonElement>(_handler.Requests[1].Body!, _json);
			Assert.That(reviewBody.GetProperty("category").GetString(), Is.EqualTo("Spam"));
			Assert.That(reviewBody.GetProperty("detail").ValueKind, Is.EqualTo(JsonValueKind.Null));
		});
	}

	[TestCase(HttpStatusCode.Conflict, "{}", StorePlatformFailure.AlreadyReported)]
	[TestCase(HttpStatusCode.Forbidden, "{\"title\":\"You cannot report your own review.\"}", StorePlatformFailure.Forbidden)]
	[TestCase(HttpStatusCode.Forbidden, "{\"accountSuspended\":true}", StorePlatformFailure.AccountSuspended)]
	[TestCase(HttpStatusCode.NotFound, "", StorePlatformFailure.NotFound)]
	[TestCase(HttpStatusCode.TooManyRequests, "{}", StorePlatformFailure.Cooldown)]
	public async Task A_refused_report_says_what_the_platform_meant(HttpStatusCode status,
		string body,
		StorePlatformFailure expected)
	{
		_handler.Respond = _ =>
			new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/problem+json") };

		var entry = await _client.ReportPackage("com.acme.hue", "Spam", null);
		var review = await _client.ReportReview("com.acme.hue", Guid.NewGuid(), "Spam", null);

		Assert.Multiple(() =>
		{
			Assert.That(entry.Failure, Is.EqualTo(expected));
			Assert.That(review.Failure, Is.EqualTo(expected));
		});
	}

	[Test]
	public async Task A_report_is_never_sent_while_signed_out()
	{
		_session.Current = ConnectSessionSnapshot.SignedOut;

		var result = await _client.ReportReview("com.acme.hue", Guid.NewGuid(), "Spam", null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Failure, Is.EqualTo(StorePlatformFailure.SignInRequired));
			Assert.That(_handler.Requests, Is.Empty);
		});
	}

	[Test]
	public async Task No_own_review_is_an_empty_answer_not_a_failure()
	{
		_handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

		var result = await _client.GetOwnReview("com.acme.hue");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Value, Is.Null);
		});
	}

	[Test]
	public async Task An_unreachable_platform_is_unavailable_rather_than_an_exception()
	{
		_handler.Respond = _ => throw new HttpRequestException("connection refused");

		var ratings = await _client.GetRatings(["com.acme.hue"]);
		var reviews = await _client.GetReviews("com.acme.hue", 1, 20, StorePlatformReviewSort.NewestFirst, null);

		Assert.Multiple(() =>
		{
			Assert.That(ratings.Failure, Is.EqualTo(StorePlatformFailure.Unavailable));
			Assert.That(reviews.Failure, Is.EqualTo(StorePlatformFailure.Unavailable));
		});
	}

	[Test]
	public async Task A_platform_that_never_answers_times_out_as_unavailable()
	{
		using var client = new StorePlatformClient(_handler,
			_session,
			StorePlatformOptions.Default with { RequestTimeout = TimeSpan.FromMilliseconds(100) },
			Serilog.Core.Logger.None);
		_handler.NeverAnswers = true;

		var result = await client.GetRating("com.acme.hue");

		Assert.That(result.Failure, Is.EqualTo(StorePlatformFailure.Unavailable));
	}

	[Test]
	public async Task Account_requests_are_never_sent_without_a_usable_session()
	{
		_session.Current = ConnectSessionSnapshot.SignedOut;
		var signedOut = await _client.GetEntitlements(["com.acme.hue"]);

		_session.Current = FakeConnectSessionService.SignedIn(null);
		_session.Failure = new ConnectAuthRejectedException("revoked");
		var rejected = await _client.ClaimEntitlements(["com.acme.hue"]);

		_session.Failure = new ConnectAuthTransientException("offline");
		var transient = await _client.DeleteOwnReview("com.acme.hue");

		_session.Failure = new ConnectAccountSuspendedException("suspended");
		var suspended = await _client.PutOwnReview("com.acme.hue", 5, null, null);

		Assert.Multiple(() =>
		{
			Assert.That(_handler.Requests, Is.Empty);
			Assert.That(signedOut.Failure, Is.EqualTo(StorePlatformFailure.SignInRequired));
			Assert.That(rejected.Failure, Is.EqualTo(StorePlatformFailure.SignInRequired));
			Assert.That(transient.Failure, Is.EqualTo(StorePlatformFailure.Unavailable));
			Assert.That(suspended.Failure, Is.EqualTo(StorePlatformFailure.AccountSuspended));
		});
	}

	[Test]
	public async Task Public_reviews_keep_null_titles_and_bodies()
	{
		_handler.Respond = _ => Json(new
		{
			items = new object[]
			{
				new
				{
					id = Guid.NewGuid(), rating = 3, title = "Only a title", body = (string?)null,
					author = new { displayName = "Ada", avatarUrl = (string?)null },
					createdAt = DateTimeOffset.UnixEpoch, updatedAt = DateTimeOffset.UnixEpoch,
					downloadedBeforeReview = true, isEdited = false
				}
			},
			page = 2, pageSize = 10, totalCount = 11, rating = 3.0, ratingCount = 11, reviewCount = 11
		});

		var result = await _client.GetReviews("com.acme.hue", 2, 10, StorePlatformReviewSort.OldestFirst, 3);
		var review = result.Value!.Items.Single();

		Assert.Multiple(() =>
		{
			Assert.That(_handler.Requests.Single().Uri.Query, Is.EqualTo("?page=2&pageSize=10&sort=OldestFirst&rating=3"));
			Assert.That(review.Title, Is.EqualTo("Only a title"));
			Assert.That(review.Body, Is.Null);
			Assert.That(review.DownloadedBeforeReview, Is.True);
			Assert.That(review.Author.DisplayName, Is.EqualTo("Ada"));
		});
	}

	[Test]
	public async Task Tests_are_read_for_the_signed_in_account_with_its_bearer_token()
	{
		var buildId = Guid.NewGuid();
		_handler.Respond = _ => Json(new[]
		{
			new
			{
				packageId = "com.acme.hue",
				displayName = "Hue",
				joinedAt = DateTimeOffset.UnixEpoch,
				builds = new[]
				{
					new
					{
						id = buildId,
						version = "1.2.0",
						build = "42",
						changelog = "Fixes",
						fileName = "hue.macroDeckPlugin",
						sha256 = new string('a', 64),
						sizeInBytes = 1024,
						uploadedAt = DateTimeOffset.UnixEpoch,
						availableAt = DateTimeOffset.UnixEpoch
					}
				}
			}
		});

		var result = await _client.GetTests();

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Value!.Single().Builds.Single().Id, Is.EqualTo(buildId));
			Assert.That(_handler.Requests.Single().Method, Is.EqualTo("GET"));
			Assert.That(_handler.Requests.Single().Uri.AbsolutePath, Is.EqualTo("/api/v1/store/tests"));
			Assert.That(_handler.Requests.Single().Authorization, Is.EqualTo("Bearer access-token"));
		});
	}

	[Test]
	public async Task A_test_build_link_is_asked_for_by_package_and_build_and_never_while_signed_out()
	{
		var buildId = Guid.NewGuid();
		_handler.Respond = _ => Json(new
		{
			url = "https://staging.example/hue.macroDeckPlugin",
			fileName = "hue.macroDeckPlugin",
			sha256 = new string('b', 64),
			sizeInBytes = 2048,
			expiresAt = DateTimeOffset.UnixEpoch
		});

		var link = await _client.GetTestBuildDownload("com.acme.hue", buildId);
		_session.Current = ConnectSessionSnapshot.SignedOut;
		var signedOut = await _client.GetTestBuildDownload("com.acme.hue", buildId);

		Assert.Multiple(() =>
		{
			Assert.That(link.Value!.Url, Is.EqualTo(new Uri("https://staging.example/hue.macroDeckPlugin")));
			Assert.That(_handler.Requests.Single().Method, Is.EqualTo("POST"));
			Assert.That(_handler.Requests.Single().Uri.AbsolutePath,
				Is.EqualTo($"/api/v1/store/tests/com.acme.hue/builds/{buildId:D}/download"));
			Assert.That(signedOut.Failure, Is.EqualTo(StorePlatformFailure.SignInRequired));
			Assert.That(_handler.Requests, Has.Count.EqualTo(1), "a signed-out account asks nothing");
		});
	}

	private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) =>
		new(status)
		{
			Content = new StringContent(JsonSerializer.Serialize(value, _json),
				Encoding.UTF8,
				"application/json")
		};

	private sealed record RecordedRequest(string Method, Uri Uri, string? Authorization, string? Body);

	private sealed class RecordingHandler : HttpMessageHandler
	{
		public List<RecordedRequest> Requests { get; } = [];

		public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

		public bool NeverAnswers { get; set; }

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			Requests.Add(new RecordedRequest(request.Method.Method,
				request.RequestUri!,
				request.Headers.Authorization?.ToString(),
				request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));

			if (NeverAnswers)
			{
				await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
			}

			return Respond(request);
		}
	}

	private sealed class TokenSession : IConnectSessionService
	{
		public ConnectSessionSnapshot Current { get; set; } = ConnectSessionSnapshot.SignedOut;

		public Exception? Failure { get; set; }

		public event EventHandler<ConnectSessionSnapshot>? SessionChanged;

		public Task Initialize(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task<ConnectSignInStart> StartSignIn(CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public Task CancelSignIn(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task SignOut(CancellationToken cancellationToken = default)
		{
			SessionChanged?.Invoke(this, ConnectSessionSnapshot.SignedOut);
			return Task.CompletedTask;
		}

		public Task<string> GetAccessToken(CancellationToken cancellationToken = default) =>
			Failure is null ? Task.FromResult("access-token") : Task.FromException<string>(Failure);
	}
}
