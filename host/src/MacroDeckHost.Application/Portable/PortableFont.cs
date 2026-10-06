namespace MacroDeckHost.Application.Portable;

public sealed class PortableFont
{
	public string FontId { get; set; } = string.Empty;

	public string Format { get; set; } = string.Empty;

	public string ContentHash { get; set; } = string.Empty;

	public string Family { get; set; } = string.Empty;

	public string StyleName { get; set; } = string.Empty;

	public List<string> FaceIds { get; set; } = [];
}

public sealed record PortableFontFile(string FontId, string Format, byte[] Bytes);
