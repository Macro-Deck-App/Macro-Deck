using System.Text;
using MacroDeck.Signing.Keys;

namespace MacroDeck.Signing.Tests.UnitTests;

/// <summary>
/// The base64 key-file format <c>keygen</c> writes and <c>sign</c> reads back: one line of base64, exactly
/// one trailing newline, no BOM, refuses to overwrite, and on Unix a private key file is created readable
/// and writable by its owner only.
/// </summary>
[TestFixture]
internal sealed class SigningKeyFileTests
{
	private string _directory = null!;

	[SetUp]
	public void SetUp()
	{
		_directory = Directory.CreateTempSubdirectory("macrodeck-signing-tests-").FullName;
	}

	[TearDown]
	public void TearDown()
	{
		if (Directory.Exists(_directory))
		{
			Directory.Delete(_directory, recursive: true);
		}
	}

	[Test]
	public async Task A_written_private_key_round_trips_through_ReadBase64Async()
	{
		var (privateKey, _) = Ed25519KeyPair.Create();
		var expected = (byte[])privateKey.Clone();
		var path = Path.Combine(_directory, "key.private");

		var written = await SigningKeyFile.WriteNewPrivateKeyFileAsync(path, privateKey);
		Assert.That(written, Is.True);

		var read = await SigningKeyFile.ReadBase64Async(path, Ed25519KeyPair.PrivateKeyLength);
		Assert.That(read, Is.EqualTo(expected));
	}

	[Test]
	public async Task A_written_key_file_is_base64_with_exactly_one_trailing_newline_no_BOM_no_CRLF()
	{
		var (privateKey, _) = Ed25519KeyPair.Create();
		var path = Path.Combine(_directory, "key.private");
		await SigningKeyFile.WriteNewPrivateKeyFileAsync(path, privateKey);

		var bytes = await File.ReadAllBytesAsync(path);

		Assert.Multiple(() =>
		{
			Assert.That(bytes, Has.Length.GreaterThan(0));
			Assert.That(bytes[0], Is.Not.EqualTo(0xEF), "must not start with a UTF-8 BOM byte");
			Assert.That(bytes.Count(b => b == (byte)'\n'), Is.EqualTo(1));
			Assert.That(bytes.Count(b => b == (byte)'\r'), Is.EqualTo(0));
			Assert.That(bytes[^1], Is.EqualTo((byte)'\n'));

			var text = Encoding.UTF8.GetString(bytes, 0, bytes.Length - 1);
			Assert.DoesNotThrow(() => Convert.FromBase64String(text));
		});
	}

	[Test]
	public async Task Reading_tolerates_a_missing_trailing_newline()
	{
		var (privateKey, _) = Ed25519KeyPair.Create();
		var path = Path.Combine(_directory, "key.private");
		await File.WriteAllTextAsync(path, Convert.ToBase64String(privateKey));

		var read = await SigningKeyFile.ReadBase64Async(path, Ed25519KeyPair.PrivateKeyLength);

		Assert.That(read, Is.EqualTo(privateKey));
	}

	[Test]
	public async Task Reading_a_file_of_the_wrong_decoded_length_fails()
	{
		var path = Path.Combine(_directory, "key.private");
		await File.WriteAllTextAsync(path, Convert.ToBase64String(new byte[16]));

		var read = await SigningKeyFile.ReadBase64Async(path, Ed25519KeyPair.PrivateKeyLength);

		Assert.That(read, Is.Null);
	}

	[Test]
	public async Task Reading_non_base64_content_fails()
	{
		var path = Path.Combine(_directory, "key.private");
		await File.WriteAllTextAsync(path, "not valid base64 !!! ***");

		var read = await SigningKeyFile.ReadBase64Async(path, Ed25519KeyPair.PrivateKeyLength);

		Assert.That(read, Is.Null);
	}

	[Test]
	public async Task Reading_an_empty_file_fails()
	{
		var path = Path.Combine(_directory, "key.private");
		await File.WriteAllTextAsync(path, string.Empty);

		var read = await SigningKeyFile.ReadBase64Async(path, Ed25519KeyPair.PrivateKeyLength);

		Assert.That(read, Is.Null);
	}

	/// <summary>The three failure inputs (wrong length, malformed base64, empty) are distinguishable from
	/// each other only in the sense that each is independently reproducible and each returns the same
	/// "unreadable" signal - <see cref="SigningKeyFile.ReadBase64Async" /> itself carries no richer error
	/// type, so a caller distinguishes them (if it needs to) by re-deriving the reason, not by inspecting a
	/// result this method returns. This test pins that every one of the three inputs is independently
	/// rejected, rather than only the first being exercised.</summary>
	[Test]
	public async Task Wrong_length_malformed_and_empty_are_each_independently_rejected()
	{
		var wrongLengthPath = Path.Combine(_directory, "wrong-length.private");
		var malformedPath = Path.Combine(_directory, "malformed.private");
		var emptyPath = Path.Combine(_directory, "empty.private");

		await File.WriteAllTextAsync(wrongLengthPath, Convert.ToBase64String(new byte[10]));
		await File.WriteAllTextAsync(malformedPath, "%%%not-base64%%%");
		await File.WriteAllTextAsync(emptyPath, string.Empty);

		var wrongLengthResult = await SigningKeyFile.ReadBase64Async(wrongLengthPath, Ed25519KeyPair.PrivateKeyLength);
		var malformedResult = await SigningKeyFile.ReadBase64Async(malformedPath, Ed25519KeyPair.PrivateKeyLength);
		var emptyResult = await SigningKeyFile.ReadBase64Async(emptyPath, Ed25519KeyPair.PrivateKeyLength);

		Assert.Multiple(() =>
		{
			Assert.That(wrongLengthResult, Is.Null);
			Assert.That(malformedResult, Is.Null);
			Assert.That(emptyResult, Is.Null);
		});
	}

	[Test]
	public async Task Writing_a_private_key_never_overwrites_an_existing_file()
	{
		var path = Path.Combine(_directory, "key.private");
		await File.WriteAllTextAsync(path, "pre-existing content");

		var (privateKey, _) = Ed25519KeyPair.Create();
		var written = await SigningKeyFile.WriteNewPrivateKeyFileAsync(path, privateKey);
		var contentAfter = await File.ReadAllTextAsync(path);

		Assert.Multiple(() =>
		{
			Assert.That(written, Is.False);
			Assert.That(contentAfter, Is.EqualTo("pre-existing content"));
		});
	}

	[Test]
	public async Task Writing_public_material_never_overwrites_an_existing_file()
	{
		var path = Path.Combine(_directory, "key.public");
		await File.WriteAllTextAsync(path, "pre-existing content");

		var written = await SigningKeyFile.WriteNewFileAsync(path, "new content"u8.ToArray());
		var contentAfter = await File.ReadAllTextAsync(path);

		Assert.Multiple(() =>
		{
			Assert.That(written, Is.False);
			Assert.That(contentAfter, Is.EqualTo("pre-existing content"));
		});
	}

	/// <summary>Even under a permissive umask, the private key file is created with exactly owner
	/// read/write - a stray 022 or 002 umask must never leave it group- or world-readable.</summary>
	[Test]
	[Platform(Exclude = "Win")]
	public async Task On_Unix_the_private_key_file_is_created_owner_read_write_only_even_under_a_permissive_umask()
	{
		if (OperatingSystem.IsWindows())
		{
			return;
		}

		var path = Path.Combine(_directory, "key.private");
		var (privateKey, _) = Ed25519KeyPair.Create();

		// 000: everything the OS would otherwise allow is allowed, isolating the library's own mode.
		var originalUmask = LibC.umask(0);
		try
		{
			var written = await SigningKeyFile.WriteNewPrivateKeyFileAsync(path, privateKey);
			Assert.That(written, Is.True);

			var mode = File.GetUnixFileMode(path);
			Assert.That(mode, Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
		}
		finally
		{
			_ = LibC.umask(originalUmask);
		}
	}

	private static class LibC
	{
		[System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
		public static extern int umask(int mask);
	}
}
