namespace MacroDeckHost.Application.Portable;

public sealed class PortableIcon
{
	public Guid Id { get; set; }

	public string Name { get; set; } = string.Empty;

	public int? Width { get; set; }

	public int? Height { get; set; }

	public bool IsAnimated { get; set; }

	public int? FrameCount { get; set; }

	public string? SourceContentHash { get; set; }

	public Dictionary<string, string> FileContentHashes { get; set; } = [];

	public string? Checksum { get; set; }

	public string? OriginalFileName { get; set; }

	public string? OriginalFormat { get; set; }

	public List<int> AvailableSizes { get; set; } = [];
}
