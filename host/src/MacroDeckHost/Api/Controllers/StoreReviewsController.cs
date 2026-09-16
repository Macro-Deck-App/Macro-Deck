using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Reviews;
using MacroDeckHost.Application.Ui.Transport.Messages.Store;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/store")]
public class StoreReviewsController : ControllerBase
{
	private readonly IStoreReviewService _reviews;
	private readonly IStoreReviewAvatarProxy _avatars;

	public StoreReviewsController(IStoreReviewService reviews, IStoreReviewAvatarProxy avatars)
	{
		_reviews = reviews;
		_avatars = avatars;
	}

	[HttpGet("ratings")]
	public async Task<ActionResult<GetStoreRatingsResponse>> GetRatings([FromQuery] string? ids, CancellationToken ct)
	{
		var packageIds = (ids ?? string.Empty)
			.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Distinct(StringComparer.Ordinal)
			.ToList();
		if (packageIds.Count > StorePlatformOptions.MaxIdsPerRequest)
		{
			return BadRequest();
		}

		return packageIds.Count == 0
			? new GetStoreRatingsResponse { Available = true }
			: await _reviews.GetRatings(packageIds, ct);
	}

	[HttpGet("catalog/{kind}/{id}/rating")]
	public Task<GetStoreRatingResponse> GetRating(StoreExtensionKind kind, string id, CancellationToken ct) =>
		_reviews.GetRating(kind, id, ct);

	[HttpGet("catalog/{kind}/{id}/reviews")]
	public Task<GetStoreReviewsResponse> GetReviews(StoreExtensionKind kind,
		string id,
		CancellationToken ct,
		[FromQuery] int page = 1,
		[FromQuery] int pageSize = 20,
		[FromQuery] StoreReviewSortOrder sort = StoreReviewSortOrder.Newest,
		[FromQuery] int? rating = null) =>
		_reviews.GetReviews(kind, id, page, pageSize, sort, rating, ct);

	[HttpGet("catalog/{kind}/{id}/reviews/me")]
	public Task<GetStoreOwnReviewResponse> GetOwnReview(StoreExtensionKind kind, string id, CancellationToken ct) =>
		_reviews.GetOwnReview(kind, id, ct);

	[HttpPut("catalog/{kind}/{id}/reviews/me")]
	public Task<StoreOwnReviewWriteResponse> PutOwnReview(StoreExtensionKind kind,
		string id,
		PutStoreOwnReviewRequest body,
		CancellationToken ct) =>
		_reviews.PutOwnReview(kind, id, body, ct);

	[HttpDelete("catalog/{kind}/{id}/reviews/me")]
	public Task<StoreOwnReviewWriteResponse> DeleteOwnReview(StoreExtensionKind kind, string id, CancellationToken ct) =>
		_reviews.DeleteOwnReview(kind, id, ct);

	[HttpGet("review-avatars")]
	public async Task<IActionResult> GetAvatar([FromQuery] string? src, CancellationToken ct)
	{
		if (await _avatars.Fetch(src, ct) is not { } avatar)
		{
			return NotFound();
		}

		Response.Headers.Append("X-Content-Type-Options", "nosniff");
		Response.Headers.CacheControl = "private, max-age=3600";
		return File(avatar.Content, avatar.ContentType);
	}
}
