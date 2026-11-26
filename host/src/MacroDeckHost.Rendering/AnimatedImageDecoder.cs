using SkiaSharp;

namespace MacroDeckHost.Rendering;

public sealed record AnimatedImage(
	int Width,
	int Height,
	IReadOnlyList<byte[]> Frames,
	IReadOnlyList<int> DurationsMs,
	IReadOnlyList<int> EffectiveDurationsMs,
	int? Plays)
{
	public long CycleMs => EffectiveDurationsMs.Sum(duration => (long)duration);

	public int FrameAt(double elapsedMs)
	{
		if (Frames.Count <= 1 || CycleMs <= 0)
		{
			return 0;
		}

		var elapsed = Math.Max(0, elapsedMs);
		if (Plays is { } plays && elapsed >= plays * (double)CycleMs)
		{
			return Frames.Count - 1;
		}

		var into = elapsed % CycleMs;
		for (var index = 0; index < EffectiveDurationsMs.Count; index++)
		{
			if (into < EffectiveDurationsMs[index])
			{
				return index;
			}

			into -= EffectiveDurationsMs[index];
		}

		return Frames.Count - 1;
	}

	public double? NextChangeMs(double elapsedMs)
	{
		if (Frames.Count <= 1 || CycleMs <= 0)
		{
			return null;
		}

		var elapsed = Math.Max(0, elapsedMs);
		if (Plays is { } plays && elapsed >= plays * (double)CycleMs)
		{
			return null;
		}

		var into = elapsed % CycleMs;
		double boundary = 0;
		foreach (var duration in EffectiveDurationsMs)
		{
			boundary += duration;
			if (into < boundary)
			{
				return boundary - into;
			}
		}

		return CycleMs - into;
	}
}

public static class AnimatedImageDecoder
{
	// A frame of 10 ms or less shows for 100 ms, the browsers' rule for every format.
	private const int ShortFrameMs = 10;

	private const int ShortFrameShownMs = 100;

	public static AnimatedImage? Decode(ReadOnlySpan<byte> data)
	{
		using var skData = SKData.CreateCopy(data);
		using var codec = SKCodec.Create(skData);
		if (codec is null)
		{
			return null;
		}

		var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
		var frameInfos = codec.FrameInfo;
		var frames = new List<byte[]>();
		var durations = new List<int>();
		if (frameInfos.Length <= 1)
		{
			using var bitmap = new SKBitmap(info);
			var result = codec.GetPixels(info, bitmap.GetPixels());
			if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
			{
				return null;
			}

			frames.Add(bitmap.Bytes);
			durations.Add(frameInfos.Length == 1 ? frameInfos[0].Duration : 0);
		}
		else
		{
			for (var index = 0; index < frameInfos.Length; index++)
			{
				var required = frameInfos[index].RequiredFrame;
				using var bitmap = new SKBitmap(info);
				if (required >= 0 && required < frames.Count)
				{
					System.Runtime.InteropServices.Marshal.Copy(frames[required],
						0,
						bitmap.GetPixels(),
						frames[required].Length);
				}
				else
				{
					bitmap.Erase(SKColors.Transparent);
				}

				// A frame with broken data still shows what decoded of it, as Chrome's decoder does.
				var result = codec.GetPixels(info, bitmap.GetPixels(), new SKCodecOptions(index, required));
				if (result is not (SKCodecResult.Success
					or SKCodecResult.IncompleteInput
					or SKCodecResult.ErrorInInput
					or SKCodecResult.InvalidInput))
				{
					break;
				}

				frames.Add(bitmap.Bytes);
				durations.Add(frameInfos[index].Duration);
			}
		}

		if (frames.Count == 0)
		{
			return null;
		}

		var repetitions = codec.RepetitionCount;
		int? plays = frames.Count <= 1 ? 1 : repetitions < 0 ? null : repetitions + 1;
		return new AnimatedImage(info.Width,
			info.Height,
			frames,
			durations,
			durations.Select(duration => duration <= ShortFrameMs ? ShortFrameShownMs : duration).ToList(),
			plays);
	}
}
