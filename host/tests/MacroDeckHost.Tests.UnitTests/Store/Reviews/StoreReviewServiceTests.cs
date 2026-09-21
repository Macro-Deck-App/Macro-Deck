using MacroDeckHost.Application.Connect;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Reviews;
using MacroDeckHost.Application.Ui.Transport.Messages.Store;
using MacroDeckHost.Tests.UnitTests.Connect;
using MacroDeckHost.Tests.UnitTests.Delegation;

namespace MacroDeckHost.Tests.UnitTests.Store.Reviews;

[TestFixture]
internal sealed class StoreReviewServiceTests
{
	private const string PackageId = "com.acme.hue";

	private FakeStorePlatformClient _platform = null!;
	private FakeStoreOfficialPackages _packages = null!;
	private FakeConnectSessionService _session = null!;
	private FakeTimeProvider _time = null!;
	private StoreReviewService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_platform = new FakeStorePlatformClient();
		_packages = new FakeStoreOfficialPackages();
		_packages.Listed.Add(PackageId);
		_session = new FakeConnectSessionService { Current = FakeConnectSessionService.SignedIn(null) };
		_time = new FakeTimeProvider();
		_service = new StoreReviewService(_platform, _packages, _session, StorePlatformOptions.Default, _time);
	}

	[Test]
	public async Task A_package_outside_the_official_catalog_never_reaches_the_platform()
	{
		var ratings = await _service.GetRatings(["com.other.thing"], CancellationToken.None);
		var reviews = await _service.GetReviews(StoreExtensionKind.Plugin, "com.other.thing", 1, 20,
			StoreReviewSortOrder.Newest, null, CancellationToken.None);
		var own = await _service.GetOwnReview(StoreExtensionKind.Plugin, "com.other.thing", CancellationToken.None);
		var put = await _service.PutOwnReview(StoreExtensionKind.Plugin, "com.other.thing",
			new PutStoreOwnReviewRequest { Rating = 5 }, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(ratings.Ratings, Is.Empty);
			Assert.That(reviews.Available, Is.False);
			Assert.That(own.State, Is.EqualTo(StoreReviewComposeState.Unavailable));
			Assert.That(put.Success, Is.False);
			Assert.That(_platform.RatingRequests, Is.Empty);
			Assert.That(_platform.Puts, Is.Empty);
		});
	}

	[Test]
	public async Task Ratings_list_only_packages_somebody_has_rated_and_are_cached_briefly()
	{
		_packages.Listed.Add("com.acme.icons");
		_platform.Ratings[PackageId] = new StorePlatformRating(4.5, 2, []);
		_platform.Ratings["com.acme.icons"] = new StorePlatformRating(null, 0, []);

		var first = await _service.GetRatings([PackageId, "com.acme.icons"], CancellationToken.None);
		await _service.GetRatings([PackageId, "com.acme.icons"], CancellationToken.None);
		_time.Advance(StorePlatformOptions.Default.RatingsCacheLifetime);
		await _service.GetRatings([PackageId], CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(first.Available, Is.True);
			Assert.That(first.Ratings.Keys, Is.EquivalentTo(new[] { PackageId }));
			Assert.That(first.Ratings[PackageId].Rating, Is.EqualTo(4.5));
			Assert.That(_platform.RatingRequests, Has.Count.EqualTo(2));
		});
	}

	[Test]
	public async Task An_unreachable_platform_hides_ratings_instead_of_failing()
	{
		_platform.RatingsFailure = StorePlatformFailure.Unavailable;

		var ratings = await _service.GetRatings([PackageId], CancellationToken.None);
		var rating = await _service.GetRating(StoreExtensionKind.Plugin, PackageId, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(ratings.Available, Is.False);
			Assert.That(rating.Available, Is.False);
		});
	}

	[Test]
	public async Task Signed_out_users_can_read_but_are_asked_to_sign_in_to_review()
	{
		_session.Current = ConnectSessionSnapshot.SignedOut;
		_platform.ReviewPage = new StorePlatformReviewPage([], 1, 20, 0, null, 0, 0);

		var reviews = await _service.GetReviews(StoreExtensionKind.Plugin, PackageId, 1, 20,
			StoreReviewSortOrder.Newest, null, CancellationToken.None);
		var own = await _service.GetOwnReview(StoreExtensionKind.Plugin, PackageId, CancellationToken.None);
		var put = await _service.PutOwnReview(StoreExtensionKind.Plugin, PackageId,
			new PutStoreOwnReviewRequest { Rating = 5 }, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(reviews.Available, Is.True);
			Assert.That(own.State, Is.EqualTo(StoreReviewComposeState.SignedOut));
			Assert.That(put.Error?.Code, Is.EqualTo("sign_in_required"));
		});
	}

	[Test]
	public async Task An_installed_package_the_platform_does_not_know_about_yet_is_claimed_before_answering()
	{
		_platform.Entitlement = StoreEntitlementStatus.NotEntitled;
		_packages.Installed = [PackageId];

		var own = await _service.GetOwnReview(StoreExtensionKind.Plugin, PackageId, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(own.State, Is.EqualTo(StoreReviewComposeState.Entitled));
			Assert.That(_platform.Claims.Single(), Is.EqualTo(new[] { PackageId }));
		});
	}

	[Test]
	public async Task A_package_that_is_not_installed_needs_a_download_and_still_shows_an_earlier_review()
	{
		_platform.Entitlement = StoreEntitlementStatus.NotEntitled;
		_platform.OwnReview = FakeStorePlatformClient.Own(PackageId, 2, "Old", null);

		var own = await _service.GetOwnReview(StoreExtensionKind.Plugin, PackageId, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(own.State, Is.EqualTo(StoreReviewComposeState.NotEntitled));
			Assert.That(own.Review?.Title, Is.EqualTo("Old"));
			Assert.That(_platform.Claims, Is.Empty);
		});
	}

	[TestCase(0, null, null, "Rating")]
	[TestCase(5, "Line one\nline two", null, "Title")]
	[TestCase(5, null, "ok", "Body")]
	public async Task Invalid_reviews_are_refused_before_they_reach_the_platform(int rating,
		string? title,
		string? body,
		string field)
	{
		var result = await _service.PutOwnReview(StoreExtensionKind.Plugin, PackageId,
			new PutStoreOwnReviewRequest { Rating = rating, Title = title, Body = body }, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error?.Code, Is.EqualTo("validation"));
			Assert.That(result.Error?.Field, Is.EqualTo(field));
			Assert.That(_platform.Puts, Is.Empty);
		});
	}

	[Test]
	public async Task A_title_over_the_limit_is_refused()
	{
		var result = await _service.PutOwnReview(StoreExtensionKind.Plugin, PackageId,
			new PutStoreOwnReviewRequest { Rating = 4, Title = new string('a', 121) }, CancellationToken.None);

		Assert.That(result.Error?.Field, Is.EqualTo("Title"));
	}

	[Test]
	public async Task Changing_only_the_stars_of_an_older_long_review_is_allowed()
	{
		var longBody = new string('a', 3000);
		_platform.OwnReview = FakeStorePlatformClient.Own(PackageId, 3, null, longBody);

		var unchanged = await _service.PutOwnReview(StoreExtensionKind.Plugin, PackageId,
			new PutStoreOwnReviewRequest { Rating = 4, Body = longBody }, CancellationToken.None);
		var changed = await _service.PutOwnReview(StoreExtensionKind.Plugin, PackageId,
			new PutStoreOwnReviewRequest { Rating = 4, Body = longBody + "b" }, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(unchanged.Success, Is.True);
			Assert.That(changed.Error?.Field, Is.EqualTo("Body"));
			Assert.That(_platform.Puts, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_write_refused_for_a_missing_download_of_an_installed_package_claims_and_retries_once()
	{
		_packages.Installed = [PackageId];
		_platform.PutResults.Enqueue(StorePlatformResult.Fail<StorePlatformOwnReview>(StorePlatformFailure.DownloadRequired));

		var result = await _service.PutOwnReview(StoreExtensionKind.Plugin, PackageId,
			new PutStoreOwnReviewRequest { Rating = 5, Title = "  Great  " }, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_platform.Claims, Has.Count.EqualTo(1));
			Assert.That(_platform.Puts, Has.Count.EqualTo(2));
			Assert.That(_platform.Puts[1], Is.EqualTo((5, (string?)"Great", (string?)null)));
		});
	}

	[Test]
	public async Task A_refused_write_carries_the_code_and_wait_the_ui_needs()
	{
		_platform.PutResults.Enqueue(StorePlatformResult.Fail<StorePlatformOwnReview>(StorePlatformFailure.Cooldown,
			TimeSpan.FromSeconds(42.2)));
		_platform.DeleteResults.Enqueue(StorePlatformResult.Fail<bool>(StorePlatformFailure.Moderated));

		var put = await _service.PutOwnReview(StoreExtensionKind.Plugin, PackageId,
			new PutStoreOwnReviewRequest { Rating = 5 }, CancellationToken.None);
		var delete = await _service.DeleteOwnReview(StoreExtensionKind.Plugin, PackageId, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(put.Error?.Code, Is.EqualTo("cooldown"));
			Assert.That(put.Error?.RetryAfterSeconds, Is.EqualTo(43));
			Assert.That(delete.Error?.Code, Is.EqualTo("moderated"));
		});
	}

	[Test]
	public async Task Only_issuer_avatars_are_offered_and_only_through_the_host()
	{
		_platform.ReviewPage = new StorePlatformReviewPage([
			Review("https://auth.macro-deck.app/assets/v1/org/users/1/avatar"),
			Review("https://tracker.example/pixel.png")
		], 1, 20, 2, 4, 2, 2);

		var reviews = await _service.GetReviews(StoreExtensionKind.Plugin, PackageId, 1, 20,
			StoreReviewSortOrder.Newest, null, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(reviews.Items[0].AuthorAvatarUrl,
				Is.EqualTo("/api/store/review-avatars?src=https%3A%2F%2Fauth.macro-deck.app%2Fassets%2Fv1%2Forg%2Fusers%2F1%2Favatar"));
			Assert.That(reviews.Items[1].AuthorAvatarUrl, Is.Null);
		});
	}

	[Test]
	public async Task A_creator_reply_reaches_the_store_on_the_review_it_answers_and_nowhere_else()
	{
		var repliedAt = new DateTimeOffset(2026, 9, 2, 8, 0, 0, TimeSpan.Zero);
		var answered = Review(null) with
		{
			Reply = new StorePlatformReviewReply("Thanks, fixed in 1.2.", repliedAt, repliedAt.AddDays(1), true)
		};
		var unanswered = Review(null);
		_platform.ReviewPage = new StorePlatformReviewPage([unanswered, answered], 1, 20, 2, 4, 2, 2);

		var reviews = await _service.GetReviews(StoreExtensionKind.Plugin, PackageId, 1, 20,
			StoreReviewSortOrder.Newest, null, CancellationToken.None);
		var reply = reviews.Items.Single(review => review.Id == answered.Id).Reply;

		Assert.Multiple(() =>
		{
			Assert.That(reviews.Items, Has.Count.EqualTo(2));
			Assert.That(reviews.TotalCount, Is.EqualTo(2));
			Assert.That(reviews.Items.Single(review => review.Id == unanswered.Id).Reply, Is.Null);
			Assert.That(reply?.Body, Is.EqualTo("Thanks, fixed in 1.2."));
			Assert.That(reply?.CreatedAt, Is.EqualTo(repliedAt));
			Assert.That(reply?.UpdatedAt, Is.EqualTo(repliedAt.AddDays(1)));
			Assert.That(reply?.IsEdited, Is.True);
		});
	}

	[Test]
	public async Task An_unreachable_platform_is_not_asked_again_for_every_page_until_the_cache_lifetime_passes()
	{
		_platform.RatingsFailure = StorePlatformFailure.Unavailable;

		await _service.GetRatings([PackageId], CancellationToken.None);
		var second = await _service.GetRatings([PackageId], CancellationToken.None);
		_time.Advance(StorePlatformOptions.Default.RatingsCacheLifetime);
		await _service.GetRatings([PackageId], CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(second.Available, Is.False);
			Assert.That(_platform.RatingRequests, Has.Count.EqualTo(2));
		});
	}

	[Test]
	public async Task Lengths_are_counted_in_characters_like_the_platform_counts_them()
	{
		var title = string.Concat(Enumerable.Repeat("\U0001F680", 120));

		var result = await _service.PutOwnReview(StoreExtensionKind.Plugin, PackageId,
			new PutStoreOwnReviewRequest { Rating = 4, Title = title }, CancellationToken.None);

		Assert.That(result.Success, Is.True);
	}

	private static StorePlatformReview Review(string? avatarUrl) =>
		new(Guid.NewGuid(), 4, "Title", "Body", new StorePlatformReviewAuthor("Ada", avatarUrl),
			DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, false, false);
}
