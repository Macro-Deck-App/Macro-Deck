using System.Text.RegularExpressions;
using SkiaSharp;
using Svg;
using Svg.Skia;

namespace MacroDeckHost.Rendering.Paint;

internal sealed partial class SvgMotion
{
	private const string ClockValue
		= @"\d+:[0-5]\d:[0-5]\d(\.\d+)?|[0-5]?\d:[0-5]\d(\.\d+)?|(\d+(\.\d+)?|\.\d+)(h|min|s|ms)?";

	private readonly Lock _gate = new();
	private readonly SKSvg _document;

	private long? _seekedMs;

	private SvgMotion(SKSvg document, bool loops)
	{
		_document = document;
		Loops = loops;
	}

	public bool Loops { get; }

	public static SvgMotion? Create(ReadOnlyMemory<byte> data)
	{
		var document = new SKSvg();
		using var stream = new MemoryStream(data.ToArray(), false);
		if (document.Load(stream) is null || !document.HasAnimations || document.SourceDocument is not { } source)
		{
			document.Dispose();
			return null;
		}

		var animations = source.Descendants().OfType<SvgAnimationElement>().ToList();
		if (animations.Exists(Chained))
		{
			document.Dispose();
			return null;
		}

		return new SvgMotion(document, animations.Exists(Repeats));
	}

	// The next seek disposes the picture the previous one returned, so seeking and drawing share the lock.
	public bool TryDraw(SKCanvas canvas, long documentTimeMs, SKMatrix matrix)
	{
		lock (_gate)
		{
			try
			{
				if (_seekedMs != documentTimeMs)
				{
					_seekedMs = null;
					_document.SetAnimationTime(TimeSpan.FromMilliseconds(documentTimeMs));
					_seekedMs = documentTimeMs;
				}

				if (_document.Picture is not { } picture)
				{
					return false;
				}

				canvas.DrawPicture(picture, matrix);
				return true;
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				return false;
			}
		}
	}

	// Svg.Animation cannot seek a chain of syncbase or wallclock times to a unix-ms document time cheaply or
	// correctly, so such a document keeps its still picture.
	private static bool Chained(SvgAnimationElement animation)
	{
		return Timing(animation.Begin).Contains(Kind.Chained) || Timing(animation.End).Contains(Kind.Chained);
	}

	private static bool Repeats(SvgAnimationElement animation)
	{
		var begins = Timing(animation.Begin);
		if (begins.Length > 0 && !begins.Contains(Kind.Offset))
		{
			return false;
		}

		var forever = IsIndefinite(animation.RepeatCount) || IsIndefinite(animation.RepeatDuration);
		return forever && !Timing(animation.End).Contains(Kind.Offset);
	}

	private static bool IsIndefinite(string? value)
	{
		return string.Equals(value?.Trim(), "indefinite", StringComparison.Ordinal);
	}

	private static Kind[] Timing(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return [];
		}

		return value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(Classify)
			.ToArray();
	}

	// Pointer, key and access-key events never reach a key image, so an animation waiting on one never starts.
	private static Kind Classify(string token)
	{
		if (OffsetPattern().IsMatch(token))
		{
			return Kind.Offset;
		}

		return token.Contains("wallclock(", StringComparison.Ordinal) || SyncbasePattern().IsMatch(token)
			? Kind.Chained
			: Kind.Never;
	}

	[GeneratedRegex(@"^[+-]?\s*(" + ClockValue + ")$")]
	private static partial Regex OffsetPattern();

	[GeneratedRegex(@"\.(begin|end|repeat)\b")]
	private static partial Regex SyncbasePattern();

	private enum Kind
	{
		Offset,
		Never,
		Chained
	}
}
