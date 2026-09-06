using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Api.Support;

public static class ArchivePathReader
{
	public static async Task<Result<byte[], PortabilityError>> Read(string? path,
		IReadOnlyCollection<string> allowedExtensions,
		long maxBytes,
		CancellationToken cancellationToken)
	{
		var validation = Validate(path, allowedExtensions, maxBytes);
		if (!validation.Success)
		{
			return Result.Fail<byte[], PortabilityError>(validation.Error!.Value, validation.ErrorMessage);
		}

		try
		{
			return Result.Ok<byte[], PortabilityError>(await File.ReadAllBytesAsync(path!, cancellationToken));
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return Result.Fail<byte[], PortabilityError>(PortabilityError.NotFound, "The file could not be read");
		}
	}

	public static Result<Stream, PortabilityError> OpenRead(string? path,
		IReadOnlyCollection<string> allowedExtensions,
		long maxBytes)
	{
		var validation = Validate(path, allowedExtensions, maxBytes);
		if (!validation.Success)
		{
			return Result.Fail<Stream, PortabilityError>(validation.Error!.Value, validation.ErrorMessage);
		}

		try
		{
			return Result.Ok<Stream, PortabilityError>(File.OpenRead(path!));
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return Result.Fail<Stream, PortabilityError>(PortabilityError.NotFound, "The file could not be read");
		}
	}

	public static async Task<Result<byte[], PortabilityError>> ReadUpload(IFormFile? file,
		CancellationToken cancellationToken)
	{
		if (file is null || file.Length == 0)
		{
			return Result.Fail<byte[], PortabilityError>(PortabilityError.ValidationError, "A file is required");
		}

		await using var memory = new MemoryStream();
		await file.CopyToAsync(memory, cancellationToken);
		return Result.Ok<byte[], PortabilityError>(memory.ToArray());
	}

	public static Result<PortabilityError> Validate(string? path,
		IReadOnlyCollection<string> allowedExtensions,
		long maxBytes)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return Result.Fail(PortabilityError.ValidationError, "A file path is required");
		}

		if (path.AsSpan().IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !IsFullyQualified(path))
		{
			return Result.Fail(PortabilityError.ValidationError, "An absolute file path is required");
		}

		if (Directory.Exists(path))
		{
			return Result.Fail(PortabilityError.ValidationError, "The path is a directory, not an archive");
		}

		if (!File.Exists(path))
		{
			return Result.Fail(PortabilityError.NotFound, "The file does not exist");
		}

		var extension = Path.GetExtension(path);
		if (!allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
		{
			return Result.Fail(PortabilityError.ValidationError, "The file is not a Macro Deck archive");
		}

		try
		{
			if (new FileInfo(path).Length > maxBytes)
			{
				return Result.Fail(PortabilityError.ValidationError, "The file is too large to import");
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return Result.Fail(PortabilityError.NotFound, "The file could not be read");
		}

		return Result.Ok<PortabilityError>();
	}

	private static bool IsFullyQualified(string path)
	{
		try
		{
			return Path.IsPathFullyQualified(path);
		}
		catch (ArgumentException)
		{
			return false;
		}
	}
}
