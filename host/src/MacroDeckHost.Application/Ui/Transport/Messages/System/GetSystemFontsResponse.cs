namespace MacroDeckHost.Application.Ui.Transport.Messages.System;

public class GetSystemFontsResponse
{
	public List<SystemFontFace> Faces { get; set; } = [];
}

public class SystemFontFace
{
	public string FaceId { get; set; } = string.Empty;
	public string Family { get; set; } = string.Empty;
	public int Weight { get; set; }
	public int Width { get; set; }
	public string Slant { get; set; } = string.Empty;
	public string StyleName { get; set; } = string.Empty;
	public bool RemoteRenderable { get; set; }
}
