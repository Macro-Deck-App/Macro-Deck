namespace MacroDeckHost.Application.Rendering;

public sealed record UserFont(
	string FontId,
	string FaceId,
	string Family,
	string StyleName,
	int Weight,
	int Width,
	string Slant,
	string Format,
	long SizeBytes,
	string ContentHash);

public sealed record UserFontUpload(string FileName, byte[] Bytes);

public enum UserFontImportStatus
{
	Imported,
	AlreadyPresent,
	UnsupportedFormat,
	TooLarge,
	InvalidFont,
	AlreadyInstalled,
	AlreadyImported
}

public sealed record UserFontImportResult(string FileName, UserFontImportStatus Status, UserFont? Font);

public sealed record UserFontFile(string FontId, string Format, byte[] Bytes);

public interface IUserFontLibrary
{
	IReadOnlyList<UserFont> List();

	Task<IReadOnlyList<UserFontImportResult>> Import(IReadOnlyList<UserFontUpload> uploads,
		CancellationToken cancellationToken);

	Task<bool> Remove(string fontId, CancellationToken cancellationToken);

	UserFontFile? ReadFile(string fontId);
}
