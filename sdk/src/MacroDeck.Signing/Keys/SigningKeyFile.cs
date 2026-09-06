using System.Security.Cryptography;
using System.Text;

namespace MacroDeck.Signing.Keys;

/// <summary>
/// Reads and writes the base64-encoded raw key and signature files this library and the offline
/// key-generation tool exchange: one line of base64, trailing newline, refuse to overwrite.
/// </summary>
public static class SigningKeyFile
{
	/// <summary>Reads and base64-decodes <paramref name="path"/>, or returns <see langword="null"/> when
	/// the file is unreadable, not valid base64, or does not decode to exactly
	/// <paramref name="expectedLength"/> bytes.</summary>
	public static async Task<byte[]?> ReadBase64Async(string path,
		int expectedLength,
		CancellationToken cancellationToken = default)
	{
		string text;
		try
		{
			text = await File.ReadAllTextAsync(path, cancellationToken);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return null;
		}

		return Base64Material.TryDecode(text, expectedLength);
	}

	/// <summary>
	/// Writes <paramref name="rawPrivateKey"/> as base64 to a brand-new file at <paramref name="path"/>,
	/// refusing to overwrite an existing one. On platforms other than Windows the file is created readable
	/// and writable by its owner only. <paramref name="rawPrivateKey"/> is zeroed once written, regardless
	/// of outcome.
	/// </summary>
	public static async Task<bool> WriteNewPrivateKeyFileAsync(string path,
		byte[] rawPrivateKey,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(rawPrivateKey);

		var content = Encoding.UTF8.GetBytes(Convert.ToBase64String(rawPrivateKey) + "\n");
		try
		{
			var directory = Path.GetDirectoryName(Path.GetFullPath(path));
			if (!string.IsNullOrEmpty(directory))
			{
				Directory.CreateDirectory(directory);
			}

			var options = new FileStreamOptions
			{
				Mode = FileMode.CreateNew,
				Access = FileAccess.Write,
				Share = FileShare.None
			};
			if (!OperatingSystem.IsWindows())
			{
				options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
			}

			await using var stream = new FileStream(path, options);
			await stream.WriteAsync(content, cancellationToken);
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return false;
		}
		finally
		{
			CryptographicOperations.ZeroMemory(rawPrivateKey);
			CryptographicOperations.ZeroMemory(content);
		}
	}

	/// <summary>Writes <paramref name="content"/> to a brand-new file at <paramref name="path"/>, refusing
	/// to overwrite an existing one. For public material - certificates, signatures - that carries no
	/// confidentiality requirement, unlike <see cref="WriteNewPrivateKeyFileAsync"/>.</summary>
	public static async Task<bool> WriteNewFileAsync(string path,
		byte[] content,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(content);

		try
		{
			var directory = Path.GetDirectoryName(Path.GetFullPath(path));
			if (!string.IsNullOrEmpty(directory))
			{
				Directory.CreateDirectory(directory);
			}

			await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
			await stream.WriteAsync(content, cancellationToken);
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return false;
		}
	}
}
