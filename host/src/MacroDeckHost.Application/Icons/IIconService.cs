using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Icons;

public sealed record IconImageResult(Stream Content, string ETag, string ContentType = "image/webp");

public interface IIconService
{
	Task<Result<IconEntity, IconError>> Rename(Guid iconId, string name);

	Task<Result<IconError>> Delete(Guid iconId);

	Task<Result<int, IconError>> DeleteMany(IReadOnlyList<Guid> iconIds);

	/// <summary>
	/// The icon's rendition for <paramref name="size" />. A client that does not accept WebP gets PNG,
	/// or GIF for an animated icon; <paramref name="staticFrame" /> asks for the first frame alone
	/// instead, for a consumer that cannot carry the whole animation.
	/// </summary>
	Task<Result<IconImageResult, IconError>> GetImage(Guid iconId,
		int? size,
		bool acceptWebp,
		bool staticFrame,
		CancellationToken cancellationToken);
}
