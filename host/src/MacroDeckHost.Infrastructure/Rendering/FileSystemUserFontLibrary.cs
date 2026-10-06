using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Domain.Icons;

namespace MacroDeckHost.Infrastructure.Rendering;

public sealed class FileSystemUserFontLibrary : IUserFontLibrary, IDisposable
{
	private readonly IFontCatalog _catalog;
	private readonly string _directory;
	private readonly SemaphoreSlim _gate = new(1, 1);

	public FileSystemUserFontLibrary(IFontCatalog catalog, IMacroDeckPaths paths)
	{
		_catalog = catalog;
		_directory = paths.FontsDirectory;
	}

	public void Dispose() => _gate.Dispose();

	public IReadOnlyList<UserFont> List()
	{
		var files = UserFontFiles.Enumerate(_directory)
			.ToDictionary(path => Path.GetFileNameWithoutExtension(path), StringComparer.Ordinal);

		var listed = _catalog.GetFaces()
			.Where(face => face is { UserImported: true, ContentHash: not null })
			.Select(face => ToUserFont(face, files))
			.OfType<UserFont>()
			.ToList();
		var unlisted = files
			.Where(file => listed.All(font => font.FontId != file.Key))
			.Select(file => UnlistedFont(file.Key, file.Value))
			.OfType<UserFont>();

		return listed.Concat(unlisted)
			.OrderBy(font => font.Family, StringComparer.OrdinalIgnoreCase)
			.ThenBy(font => font.Weight)
			.ThenBy(font => font.Slant, StringComparer.Ordinal)
			.ToList();
	}

	public async Task<IReadOnlyList<UserFontImportResult>> Import(IReadOnlyList<UserFontUpload> uploads,
		CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			Directory.CreateDirectory(_directory);
			var faces = _catalog.GetFaces();
			var installedFamilies = faces
				.Where(face => !face.UserImported)
				.Select(face => face.Family)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			var listedHashes = faces
				.Where(face => face is { UserImported: true, ContentHash: not null })
				.Select(face => face.ContentHash!)
				.ToHashSet(StringComparer.Ordinal);
			var userKeys = faces
				.Where(face => face.UserImported)
				.Select(face => KeyOf(face.Family, face.Weight, face.Width, face.Slant))
				.ToHashSet(StringComparer.Ordinal);

			var outcomes = new List<(UserFontUpload Upload, UserFontImportStatus Status, string? FontId)>();
			var imported = false;
			try
			{
				foreach (var upload in uploads)
				{
					cancellationToken.ThrowIfCancellationRequested();
					var (status, fontId) = Store(upload, installedFamilies, listedHashes, userKeys);
					imported |= status == UserFontImportStatus.Imported;
					outcomes.Add((upload, status, fontId));
				}
			}
			finally
			{
				if (imported)
				{
					_catalog.Reload();
				}
			}

			var fonts = List().ToDictionary(font => font.FontId, StringComparer.Ordinal);
			return outcomes
				.Select(outcome => new UserFontImportResult(outcome.Upload.FileName,
					outcome.Status,
					outcome.FontId is not null ? fonts.GetValueOrDefault(outcome.FontId) : null))
				.ToList();
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<bool> Remove(string fontId, CancellationToken cancellationToken)
	{
		if (!UserFontFiles.IsFontId(fontId))
		{
			return false;
		}

		await _gate.WaitAsync(cancellationToken);
		try
		{
			var paths = UserFontFiles.Enumerate(_directory)
				.Where(path => Path.GetFileNameWithoutExtension(path) == fontId)
				.ToList();
			if (paths.Count == 0)
			{
				return false;
			}

			foreach (var path in paths)
			{
				File.Delete(path);
			}

			_catalog.Reload();
			return true;
		}
		finally
		{
			_gate.Release();
		}
	}

	public UserFontFile? ReadFile(string fontId)
	{
		if (!UserFontFiles.IsFontId(fontId))
		{
			return null;
		}

		var path = UserFontFiles.Enumerate(_directory)
			.FirstOrDefault(candidate => Path.GetFileNameWithoutExtension(candidate) == fontId);
		if (path is null)
		{
			return null;
		}

		try
		{
			return new UserFontFile(fontId, FormatOf(path), File.ReadAllBytes(path));
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private (UserFontImportStatus Status, string? FontId) Store(UserFontUpload upload,
		HashSet<string> installedFamilies,
		HashSet<string> listedHashes,
		HashSet<string> userKeys)
	{
		if (!UserFontFiles.HasFontExtension(upload.FileName))
		{
			return (UserFontImportStatus.UnsupportedFormat, null);
		}

		if (upload.Bytes.LongLength > UserFontFiles.MaxFileBytes)
		{
			return (UserFontImportStatus.TooLarge, null);
		}

		var face = UserFontFiles.Inspect(upload.Bytes);
		if (face is not { RemoteRenderable: true })
		{
			return (UserFontImportStatus.InvalidFont, null);
		}

		if (installedFamilies.Contains(face.Family))
		{
			return (UserFontImportStatus.AlreadyInstalled, null);
		}

		var contentHash = ContentHash.Compute(upload.Bytes);
		var fontId = UserFontFiles.FontIdOf(contentHash);
		if (UserFontFiles.Enumerate(_directory).Any(path => Path.GetFileNameWithoutExtension(path) == fontId))
		{
			return listedHashes.Contains(contentHash)
				? (UserFontImportStatus.AlreadyPresent, fontId)
				: (UserFontImportStatus.AlreadyImported, null);
		}

		if (!userKeys.Add(KeyOf(face.Family, face.Weight, face.Width, SlantName(face.Slant))))
		{
			return (UserFontImportStatus.AlreadyImported, null);
		}

		var extension = Path.GetExtension(upload.FileName).ToLowerInvariant();
		var target = Path.Combine(_directory, fontId + extension);
		var temporary = Path.Combine(_directory, fontId + ".part");
		try
		{
			File.WriteAllBytes(temporary, upload.Bytes);
			File.Move(temporary, target, overwrite: true);
		}
		catch
		{
			File.Delete(temporary);
			throw;
		}
		return (UserFontImportStatus.Imported, fontId);
	}

	private static UserFont? ToUserFont(FontFaceInfo face, Dictionary<string, string> files)
	{
		var fontId = UserFontFiles.FontIdOf(face.ContentHash!);
		if (!files.TryGetValue(fontId, out var path))
		{
			return null;
		}

		long size;
		try
		{
			size = new FileInfo(path).Length;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return null;
		}

		return new UserFont(fontId,
			face.FaceId,
			face.Family,
			face.StyleName,
			face.Weight,
			face.Width,
			face.Slant,
			FormatOf(path),
			size,
			face.ContentHash!);
	}

	// A file the catalog does not offer, for example after its family was installed on the computer, is
	// still listed without a face so it can be removed.
	private static UserFont? UnlistedFont(string fontId, string path)
	{
		byte[] bytes;
		try
		{
			bytes = File.ReadAllBytes(path);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return null;
		}

		var face = UserFontFiles.Inspect(bytes);
		if (face is null)
		{
			return null;
		}

		return new UserFont(fontId,
			string.Empty,
			face.Family,
			SkiaFontCatalog.StyleNameOf(face.Weight, face.Width, face.Slant),
			face.Weight,
			face.Width,
			SlantName(face.Slant),
			FormatOf(path),
			bytes.LongLength,
			ContentHash.Compute(bytes));
	}

	private static string FormatOf(string path) => Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

	private static string KeyOf(string family, int weight, int width, string slant) =>
		$"{family.ToUpperInvariant()}|{weight}|{width}|{slant}";

	private static string SlantName(SkiaSharp.SKFontStyleSlant slant) => slant switch
	{
		SkiaSharp.SKFontStyleSlant.Italic => FontFaceIdentity.ItalicSlant,
		SkiaSharp.SKFontStyleSlant.Oblique => FontFaceIdentity.ObliqueSlant,
		_ => FontFaceIdentity.UprightSlant
	};
}
