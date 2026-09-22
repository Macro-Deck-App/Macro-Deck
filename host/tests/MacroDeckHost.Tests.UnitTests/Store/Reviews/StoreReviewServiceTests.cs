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
	public async Task Install_counts_are_asked_for_official_packages_only_including_zero_and_cached_briefly()
	{
		_packages.Listed.Add("com.acme.icons");
		_platform.Installs[PackageId] = 1234;
		_platform.Installs["com.acme.icons"] = 0;

		var first = await _service.GetInstalls([PackageId, "com.acme.icons", "com.other.thing"], CancellationToken.None);
		await _service.GetInstalls([PackageId, "com.acme.icons"], CancellationToken.None);
		_time.Advance(StorePlatformOptions.Default.RatingsCacheLifetime);
		await _service.GetInstalls([PackageId], CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(first.Available, Is.True);
			Assert.That(first.Installs, Is.EquivalentTo(new Dictionary<string, long>
			{
				[PackageId] = 1234,
				["com.acme.icons"] = 0
			}));
			Assert.That(_platform.InstallRequests.SelectMany(request => request), Does.Not.Contain("com.other.thing"));
			Assert.That(_platform.InstallRequests, Has.Count.EqualTo(2));
		});
	}

	[Test]
	public async Task A_package_the_platform_does_not_count_has_no_install_count()
	{
		var installs = await _service.GetInstalls([PackageId], CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(installs.Available, Is.True);
			Assert.That(installs.Installs, Is.Empty);
		});
	}

	[Test]
	public async Task Unreachable_install_counts_are_unavailable_for_a_while_and_leave_ratings_alone()
	{
		_platform.InstallsFailure = StorePlatformFailure.Unavailable;
		_platform.Ratings[PackageId] = new StorePlatformRating(4.5, 2, []);

		var failed = await _service.GetInstalls([PackageId], CancellationToken.None);
		_platform.InstallsFailure = StorePlatformFailure.None;
		_platform.Installs[PackageId] = 5;
		var backedOff = await _service.GetInstalls([PackageId], CancellationToken.None);
		var ratings = await _service.GetRatings([PackageId], CancellationToken.None);
		_time.Advance(StorePlatformOptions.Default.RatingsCacheLifetime);
		var recovered = await _service.GetInstalls([PackageId], CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(failed.Available, Is.False);
			Assert.That(backedOff.Available, Is.False);
			Assert.That(_platform.InstallRequests, Has.Count.EqualTo(2));
			Assert.That(ratings.Available, Is.True);
			Assert.That(ratings.Ratings[PackageId].Rating, Is.EqualTo(4.5));
			Assert.That(recovered.Installs[PackageId], Is.EqualTo(5));
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

	[Test]
	public async Task A_report_needs_a_signed_in_account_that_is_not_suspended()
	{
		_session.Current = ConnectSessionSnapshot.SignedOut;
		var signedOut = await _service.ReportEntry(StoreExtensionKind.Plugin, PackageId,
			new ReportStoreContentRequest { Category = "Spam" }, CancellationToken.None);
		_session.Current = FakeConnectSessionService.SignedIn(null) with { Status = ConnectAccountStatus.Suspended };
		var suspended = await _service.ReportReview(StoreExtensionKind.Plugin, PackageId, Guid.NewGuid(),
			new ReportStoreContentRequest { Category = "Spam" }, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(signedOut.Error?.Code, Is.EqualTo("sign_in_required"));
			Assert.That(suspended.Error?.Code, Is.EqualTo("account_suspended"));
			Assert.That(_platform.Reports, Is.Empty);
		});
	}

	[Test]
	public async Task A_package_outside_the_official_catalog_cannot_be_reported()
	{
		var result = await _service.ReportEntry(StoreExtensionKind.Plugin, "com.other.thing",
			new ReportStoreContentRequest { Category = "Spam" }, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error?.Code, Is.EqualTo("not_found"));
			Assert.That(_platform.Reports, Is.Empty);
		});
	}

	[TestCase("Abuse", null, "Category")]
	[TestCase("other", "Uses our logo", "Category")]
	[TestCase("Other", "   ", "Detail")]
	[TestCase("Spam", "ring\u0007ring", "Detail")]
	public async Task An_invalid_entry_report_is_refused_before_it_reaches_the_platform(string category,
		string? detail,
		string field)
	{
		var result = await _service.ReportEntry(StoreExtensionKind.Plugin, PackageId,
			new ReportStoreContentRequest { Category = category, Detail = detail },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error?.Code, Is.EqualTo("validation"));
			Assert.That(result.Error?.Field, Is.EqualTo(field));
			Assert.That(_platform.Reports, Is.Empty);
		});
	}

	[Test]
	public async Task A_review_report_offers_only_review_reasons_and_needs_a_description_for_other()
	{
		var entryReason = await _service.ReportReview(StoreExtensionKind.Plugin, PackageId, Guid.NewGuid(),
			new ReportStoreContentRequest { Category = "Malicious" }, CancellationToken.None);
		var otherWithoutDetail = await _service.ReportReview(StoreExtensionKind.Plugin, PackageId, Guid.NewGuid(),
			new ReportStoreContentRequest { Category = "Other" }, CancellationToken.None);
		var tooLong = await _service.ReportReview(StoreExtensionKind.Plugin, PackageId, Guid.NewGuid(),
			new ReportStoreContentRequest
			{
				Category = "Other",
				Detail = new string('x', StoreReviewService.MaxReportDetailLength + 1)
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(entryReason.Error?.Field, Is.EqualTo("Category"));
			Assert.That(otherWithoutDetail.Error?.Field, Is.EqualTo("Detail"));
			Assert.That(tooLong.Error?.Field, Is.EqualTo("Detail"));
			Assert.That(_platform.Reports, Is.Empty);
		});
	}

	[Test]
	public async Task A_valid_report_reaches_the_platform_trimmed_and_leaves_the_ratings_alone()
	{
		var reviewId = Guid.NewGuid();
		_platform.Ratings[PackageId] = new StorePlatformRating(4.5, 2, []);
		await _service.GetRatings([PackageId], CancellationToken.None);

		var entry = await _service.ReportEntry(StoreExtensionKind.Plugin, PackageId,
			new ReportStoreContentRequest { Category = " Other ", Detail = "  Uses our logo  " }, CancellationToken.None);
		var review = await _service.ReportReview(StoreExtensionKind.Plugin, PackageId, reviewId,
			new ReportStoreContentRequest { Category = "Abuse", Detail = " " }, CancellationToken.None);
		await _service.GetRatings([PackageId], CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(entry.Success, Is.True);
			Assert.That(review.Success, Is.True);
			Assert.That(_platform.Reports, Is.EqualTo(new (string, Guid?, string, string?)[]
			{
				(PackageId, null, "Other", "Uses our logo"),
				(PackageId, reviewId, "Abuse", null)
			}));
			Assert.That(_platform.RatingRequests, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_platform_without_entry_reports_reads_as_unavailable_while_a_missing_review_reads_as_gone()
	{
		_platform.ReportResult = StorePlatformResult.Fail<bool>(StorePlatformFailure.NotFound);

		var entry = await _service.ReportEntry(StoreExtensionKind.Plugin, PackageId,
			new ReportStoreContentRequest { Category = "Spam" }, CancellationToken.None);
		var review = await _service.ReportReview(StoreExtensionKind.Plugin, PackageId, Guid.NewGuid(),
			new ReportStoreContentRequest { Category = "Spam" }, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(entry.Error?.Code, Is.EqualTo("report_unavailable"));
			Assert.That(review.Error?.Code, Is.EqualTo("not_found"));
		});
	}

	[Test]
	public async Task A_second_report_of_the_same_content_is_answered_as_already_reported()
	{
		_platform.ReportResult = StorePlatformResult.Fail<bool>(StorePlatformFailure.AlreadyReported);

		var result = await _service.ReportReview(StoreExtensionKind.Plugin, PackageId, Guid.NewGuid(),
			new ReportStoreContentRequest { Category = "Spam" }, CancellationToken.None);

		Assert.That(result.Error?.Code, Is.EqualTo("already_reported"));
	}
}
