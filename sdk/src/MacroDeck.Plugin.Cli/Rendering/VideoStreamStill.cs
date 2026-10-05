namespace MacroDeck.Plugin.Cli.Rendering;

internal static class VideoStreamStill
{
	public const long MaxBytes = 8 * 1024 * 1024;

	private static readonly byte[] _pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
	private static readonly byte[] _jpegSignature = [0xFF, 0xD8, 0xFF];

	private const string ErrorCode = "invalid-video-stream-image";

	public static bool TryRead(CliConsole console, string path, out string? dataUrl)
	{
		dataUrl = null;

		var extension = Path.GetExtension(path).ToLowerInvariant();

		if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp"))
		{
			console.WriteError(ErrorCode, $"--video-stream-image '{path}' must be a .png, .jpg, .jpeg or .webp file.");
			return false;
		}

		byte[] bytes;

		try
		{
			var info = new FileInfo(path);

			if (!info.Exists)
			{
				console.WriteError(ErrorCode, $"--video-stream-image '{path}' does not exist.");
				return false;
			}

			if (info.Length > MaxBytes)
			{
				console.WriteError(ErrorCode, $"--video-stream-image '{path}' is larger than {MaxBytes / (1024 * 1024)} MB.");
				return false;
			}

			bytes = File.ReadAllBytes(path);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			console.WriteError(ErrorCode, $"--video-stream-image '{path}' could not be read: {exception.Message}");
			return false;
		}

		var mediaType = MediaType(bytes);

		if (mediaType is null || !Matches(extension, mediaType))
		{
			console.WriteError(ErrorCode, $"--video-stream-image '{path}' is not a PNG, JPEG or WebP image.");
			return false;
		}

		dataUrl = $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}";

		return true;
	}

	private static bool Matches(string extension, string mediaType) => extension switch
	{
		".png" => mediaType == "image/png",
		".webp" => mediaType == "image/webp",
		_ => mediaType == "image/jpeg"
	};

	private static string? MediaType(byte[] bytes)
	{
		if (bytes.AsSpan().StartsWith(_pngSignature))
		{
			return "image/png";
		}

		if (bytes.AsSpan().StartsWith(_jpegSignature))
		{
			return "image/jpeg";
		}

		return bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8)
			? "image/webp"
			: null;
	}
}
