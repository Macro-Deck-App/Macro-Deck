using MacroDeck.Plugin.Protocol.Limits;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Widgets.Icons;

namespace MacroDeckHost.Application.Widgets;

public enum WidgetIconRenditionStatus
{
	Rendered,
	NotFound,
	TooLarge
}

public sealed record WidgetIconRendition(WidgetIconRenditionStatus Status,
	byte[]? Content = null,
	string? MediaType = null,
	bool Stable = true);

public static class WidgetIconRenditions
{
	public const int DefaultSize = 256;

	public static int Bucket(int requestedSize)
		=> IconVariants.TargetSizes.Where(size => size >= requestedSize).DefaultIfEmpty(IconVariants.TargetSizes.Max()).Min();

	public static Task<WidgetIconRendition> ProduceAsync(IWidgetIconSource source,
		string reference,
		CancellationToken cancellationToken)
		=> ProduceAsync(source, reference, DefaultSize, cancellationToken);

	public static Task<WidgetIconRendition> ProduceAsync(IWidgetIconSource source,
		string reference,
		int size,
		CancellationToken cancellationToken)
		=> ProduceAsync(source, reference, size, ProtocolLimits.MaxUiResourceBytes, cancellationToken);

	public static async Task<WidgetIconRendition> ProduceAsync(IWidgetIconSource source,
		string reference,
		int size,
		int maxBytes,
		CancellationToken cancellationToken)
	{
		// Never WebP: one rendition goes to every client and Safari before 14 draws WebP blank. An animated
		// GIF over maxBytes steps down in size and finally to its first frame.
		foreach (var (candidate, staticFrame) in Candidates(Bucket(size)))
		{
			var image = await source
				.GetImageAsync(reference, candidate, acceptWebp: false, staticFrame, cancellationToken)
				.ConfigureAwait(false);

			if (image is null)
			{
				return new WidgetIconRendition(WidgetIconRenditionStatus.NotFound);
			}

			byte[] content;
			try
			{
				using var memory = new MemoryStream();
				await image.Content.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
				content = memory.ToArray();
			}
			finally
			{
				await image.Content.DisposeAsync().ConfigureAwait(false);
			}

			if (content.Length <= maxBytes)
			{
				return new WidgetIconRendition(WidgetIconRenditionStatus.Rendered, content, image.MediaType, image.Stable);
			}
		}

		return new WidgetIconRendition(WidgetIconRenditionStatus.TooLarge);
	}

	private static IEnumerable<(int Size, bool StaticFrame)> Candidates(int size)
	{
		foreach (var candidate in IconVariants.TargetSizes.Where(target => target <= size).OrderDescending())
		{
			yield return (candidate, false);
		}

		yield return (IconVariants.TargetSizes.Min(), true);
	}
}
