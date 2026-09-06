using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;
using SkiaSharp;

namespace MacroDeckHost.Infrastructure.Icons;

internal static class SkiaAnimatedGifDecoder
{
	private const int DefaultFrameDelayMs = 100;

	public static Image<Rgba32>? TryDecode(byte[] bytes)
	{
		using var data = SKData.CreateCopy(bytes);
		using var codec = SKCodec.Create(data);
		if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
		{
			return null;
		}

		var info = new SKImageInfo(codec.Info.Width,
			codec.Info.Height,
			SKColorType.Rgba8888,
			SKAlphaType.Unpremul);
		var frameCount = Math.Max(1, codec.FrameCount);
		var frameInfos = codec.FrameInfo;

		var composited = new byte[frameCount][];
		using var bitmap = new SKBitmap(info);

		Image<Rgba32>? image = null;
		try
		{
			for (var i = 0; i < frameCount; i++)
			{
				var required = i < frameInfos.Length ? frameInfos[i].RequiredFrame : -1;
				if (required >= 0 && required != i - 1 && composited[required] is { } priorPixels)
				{
					Marshal.Copy(priorPixels, 0, bitmap.GetPixels(), priorPixels.Length);
				}

				var options = new SKCodecOptions(i, required);
				var result = codec.GetPixels(info, bitmap.GetPixels(), options);
				if (result is not SKCodecResult.Success and not SKCodecResult.IncompleteInput)
				{
					image?.Dispose();
					return null;
				}

				var pixels = new byte[info.BytesSize];
				Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
				composited[i] = pixels;

				var durationMs = i < frameInfos.Length && frameInfos[i].Duration > 0
					? frameInfos[i].Duration
					: DefaultFrameDelayMs;

				if (image is null)
				{
					image = Image.LoadPixelData<Rgba32>(pixels, info.Width, info.Height);
					SetDelay(image.Frames[0].Metadata, durationMs);
				}
				else
				{
					using var frameImage = Image.LoadPixelData<Rgba32>(pixels, info.Width, info.Height);
					var added = image.Frames.AddFrame(frameImage.Frames.RootFrame);
					SetDelay(added.Metadata, durationMs);
				}
			}

			var repetitions = codec.RepetitionCount;
			image!.Metadata.GetWebpMetadata().RepeatCount
				= repetitions <= 0 ? (ushort)0 : (ushort)Math.Min(repetitions, ushort.MaxValue);
			return image;
		}
		catch (Exception)
		{
			image?.Dispose();
			return null;
		}
	}

	private static void SetDelay(ImageFrameMetadata metadata, int durationMs)
	{
		metadata.GetWebpMetadata().FrameDelay = (uint)durationMs;
	}
}
