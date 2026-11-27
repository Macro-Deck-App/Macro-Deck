using MacroDeckHost.Rendering.Fit;
using MacroDeckHost.Rendering.Paint;

namespace MacroDeckHost.Rendering.Animation;

internal static class NextChange
{
	private const long RingFrameMs = 50;

	public static DateTimeOffset? Of(TilePipeline pipeline)
	{
		var now = pipeline.Input.Now.ToUnixTimeMilliseconds();
		long? next = null;

		void Offer(long at)
		{
			if (at > now && (next is null || at < next))
			{
				next = at;
			}
		}

		foreach (var period in pipeline.Host.TickPeriodsMs)
		{
			Offer(now - now % period + period);
		}

		foreach (var ring in pipeline.Document
			.Descendants()
			.Where(element => element.Classes.Contains("widget-button-ring") && element.Box.Displayed))
		{
			if (Rings.CycleMs.ContainsKey(ring.Attributes.GetValueOrDefault("data-ring-style") ?? "static"))
			{
				Offer(now - now % RingFrameMs + RingFrameMs);
			}
		}

		if (pipeline.Input.AnimatedImages)
		{
			foreach (var element in pipeline.Document.Descendants())
			{
				if (element.Paint is RenderResource resource
					&& element.Box.Displayed
					&& Artwork.Animated(resource) is { } image
					&& image.NextChangeMs(now) is { } wait)
				{
					Offer(now + (long)Math.Ceiling(wait));
				}
			}
		}

		return next is { } at ? DateTimeOffset.FromUnixTimeMilliseconds(at) : null;
	}
}
