namespace MacroDeckHost.Integrations.Http.Client;

internal static class HttpFileContentTypes
{
	private const string Fallback = "application/octet-stream";

	private static readonly Dictionary<string, string> _byExtension =
		new(StringComparer.OrdinalIgnoreCase)
		{
			[".png"] = "image/png",
			[".jpg"] = "image/jpeg",
			[".jpeg"] = "image/jpeg",
			[".gif"] = "image/gif",
			[".webp"] = "image/webp",
			[".svg"] = "image/svg+xml",
			[".bmp"] = "image/bmp",
			[".ico"] = "image/x-icon",
			[".pdf"] = "application/pdf",
			[".txt"] = "text/plain",
			[".csv"] = "text/csv",
			[".html"] = "text/html",
			[".json"] = "application/json",
			[".xml"] = "application/xml",
			[".zip"] = "application/zip",
			[".mp3"] = "audio/mpeg",
			[".wav"] = "audio/wav",
			[".mp4"] = "video/mp4",
			[".webm"] = "video/webm"
		};

	public static string Resolve(string filePath)
	{
		var extension = Path.GetExtension(filePath);
		return !string.IsNullOrEmpty(extension) && _byExtension.TryGetValue(extension, out var contentType)
			? contentType
			: Fallback;
	}
}
