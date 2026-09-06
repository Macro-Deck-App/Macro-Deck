namespace MacroDeckHost.Application.Rendering;

public sealed record FontFaceInfo(
	string FaceId,
	string Family,
	int Weight,
	int Width,
	string Slant,
	string StyleName,
	bool RemoteRenderable);

public interface IFontCatalog
{
	IReadOnlyList<FontFaceInfo> GetFaces();

	byte[]? GetFaceFile(string faceId);
}
