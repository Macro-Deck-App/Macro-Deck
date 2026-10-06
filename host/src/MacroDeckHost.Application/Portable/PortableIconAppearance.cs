namespace MacroDeckHost.Application.Portable;

public sealed class PortableIconAppearance
{
	public Guid Id { get; set; }

	public Dictionary<string, string> Traits { get; set; } = [];

	public int? Width { get; set; }

	public int? Height { get; set; }

	public bool IsAnimated { get; set; }

	public int? FrameCount { get; set; }

	public string? SourceContentHash { get; set; }

	public Dictionary<string, string> FileContentHashes { get; set; } = [];

	public string? OriginalFormat { get; set; }
}
