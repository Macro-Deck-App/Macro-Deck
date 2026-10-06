namespace MacroDeckHost.Application.Rendering;

public sealed record FontFaceInfo(
	string FaceId,
	string Family,
	int Weight,
	int Width,
	string Slant,
	string StyleName,
	bool RemoteRenderable)
{
	public bool UserImported { get; init; }

	public string? ContentHash { get; init; }
}

public interface IFontCatalog
{
	IReadOnlyList<FontFaceInfo> GetFaces();

	byte[]? GetFaceFile(string faceId);

	string ResolveFaceId(string faceId) => faceId;

	FontFaceInfo? FindFace(string faceId)
	{
		var resolved = ResolveFaceId(faceId);
		return GetFaces().FirstOrDefault(face => string.Equals(face.FaceId, resolved, StringComparison.Ordinal));
	}

	void Reload()
	{
	}
}
