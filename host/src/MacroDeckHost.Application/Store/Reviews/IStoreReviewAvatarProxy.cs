namespace MacroDeckHost.Application.Store.Reviews;

public sealed record StoreReviewAvatar(byte[] Content, string ContentType);

public interface IStoreReviewAvatarProxy
{
	Task<StoreReviewAvatar?> Fetch(string? sourceUrl, CancellationToken cancellationToken);
}
