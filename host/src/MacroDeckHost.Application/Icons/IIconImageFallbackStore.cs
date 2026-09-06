using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Icons;

public sealed record FallbackIconImage(Stream Content, string ContentType, string FileExtension);

public interface IIconImageFallbackStore
{
	/// <summary>
	/// The icon's <paramref name="variant" /> as PNG, or as GIF when the icon is animated - unless
	/// <paramref name="staticFrame" /> asks for its first frame alone as PNG, for a consumer that cannot
	/// carry the whole animation.
	/// </summary>
	Task<FallbackIconImage?> GetOrCreate(IconEntity icon,
		string variant,
		bool staticFrame,
		CancellationToken cancellationToken);
}
