using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using MacroDeckHost.Application.Persistence.Profiles;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Infrastructure.Portable;

namespace MacroDeckHost.Tests.UnitTests.Portable;

[TestFixture]
public class PortableArchiveTests
{
	private static PortableContent SampleContent() => new()
	{
		Kind = PortableArchiveKind.Profile,
		Profile = new ProfileFile
		{
			Id = Guid.NewGuid(),
			Name = "Sample",
			Folders =
			[
				new ProfileFolder { Id = Guid.NewGuid(), Name = "Root" }
			]
		},
		Icons =
		[
			new PortableIcon { Id = Guid.NewGuid(), Name = "star", AvailableSizes = [128] }
		]
	};

	private static List<PortableIconFile> SampleIconFiles(PortableContent content) =>
	[
		new(content.Icons[0].Id, "master", [1, 2, 3]),
		new(content.Icons[0].Id, "128", [4])
	];

	[Test]
	public void Write_ThenRead_Plain_RoundTripsContentAndIcons()
	{
		var content = SampleContent();
		var manifest = new PortableArchiveManifest { Kind = PortableArchiveKind.Profile };

		var bytes = PortableArchive.Write(manifest, content, SampleIconFiles(content), password: null);
		var outcome = PortableArchive.Read(bytes, password: null);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Status, Is.EqualTo(PortableReadStatus.Success));
			Assert.That(outcome.Manifest!.Encryption, Is.Null);
			Assert.That(outcome.Content!.Profile!.Name, Is.EqualTo("Sample"));
			Assert.That(outcome.Content.Icons, Has.Count.EqualTo(1));
			Assert.That(outcome.Icons, Has.Count.EqualTo(2));
			Assert.That(outcome.Icons.First(f => f.Variant == "master").Bytes, Is.EqualTo(new byte[] { 1, 2, 3 }));
		});
	}

	[Test]
	public void Write_Plain_DeclaresEveryEntryWithItsRealDigestAndSize()
	{
		var content = SampleContent();
		var manifest = new PortableArchiveManifest { Kind = PortableArchiveKind.Profile };

		var bytes = PortableArchive.Write(manifest, content, SampleIconFiles(content), password: null);
		var declared = PortableArchive.ReadManifest(bytes)!.Files;

		Assert.That(declared, Is.Not.Null);
		using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
		Assert.Multiple(() =>
		{
			Assert.That(declared!.Select(f => f.Path),
				Is.EquivalentTo(new[]
				{
					"content.json", $"icons/{content.Icons[0].Id}/master.webp", $"icons/{content.Icons[0].Id}/128.webp"
				}));
			Assert.That(declared.Select(f => f.Path), Does.Not.Contain("manifest.json"));

			foreach (var file in declared)
			{
				var entryBytes = ReadEntryBytes(zip, file.Path);
				Assert.That(file.Size, Is.EqualTo(entryBytes.Length));
				Assert.That(file.Sha256,
					Is.EqualTo("sha256:" + Convert.ToHexStringLower(SHA256.HashData(entryBytes))));
			}
		});
	}

	[Test]
	public void Write_Plain_TwiceForIdenticalContent_ProducesAnIdenticallyOrderedDeclaredList()
	{
		var content = SampleContent();

		var first = PortableArchive.Write(new PortableArchiveManifest { Kind = PortableArchiveKind.Profile },
			content,
			SampleIconFiles(content),
			password: null);
		var second = PortableArchive.Write(new PortableArchiveManifest { Kind = PortableArchiveKind.Profile },
			content,
			SampleIconFiles(content),
			password: null);

		var firstPaths = PortableArchive.ReadManifest(first)!.Files!.Select(f => f.Path).ToList();
		var secondPaths = PortableArchive.ReadManifest(second)!.Files!.Select(f => f.Path).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(secondPaths, Is.EqualTo(firstPaths), "declared order must be deterministic");
			Assert.That(firstPaths, Is.Ordered.Using<string>(StringComparer.Ordinal));
		});
	}

	[Test]
	public void Write_Encrypted_DeclaresNoFiles()
	{
		// The payload does not exist until after the manifest bytes are used as encryption associated
		// data, so an encrypted archive cannot declare files[] the way an unencrypted one does.
		var content = SampleContent();
		var manifest = new PortableArchiveManifest { Kind = PortableArchiveKind.Profile };

		var bytes = PortableArchive.Write(manifest, content, SampleIconFiles(content), password: "pw");
		var declared = PortableArchive.ReadManifest(bytes)!.Files;

		Assert.That(declared, Is.Null);
	}

	[Test]
	public void Read_Encrypted_AssociatedDataIsTheManifestExactlyAsWritten()
	{
		// A released Macro Deck authenticates the ciphertext against the on-disk manifest.json bytes
		// verbatim, so any edit to any manifest field must break decryption - and an archive written by
		// this build must decrypt when read back through the same path.
		var content = SampleContent();
		var bytes = PortableArchive.Write(new PortableArchiveManifest { Kind = PortableArchiveKind.Profile },
			content,
			SampleIconFiles(content),
			password: "pw");

		var tamperedManifest = JsonNode.Parse(ReadEntryText(bytes, "manifest.json"))!.AsObject();
		tamperedManifest["appVersion"] = "9.9.9";
		var tampered = ReplaceEntry(bytes, "manifest.json", Encoding.UTF8.GetBytes(tamperedManifest.ToJsonString()));

		var tamperedOutcome = PortableArchive.Read(tampered, password: "pw");
		var roundTrippedOutcome = PortableArchive.Read(bytes, password: "pw");

		Assert.Multiple(() =>
		{
			Assert.That(tamperedOutcome.Status, Is.EqualTo(PortableReadStatus.WrongPassword));
			Assert.That(roundTrippedOutcome.Status, Is.EqualTo(PortableReadStatus.Success));
			Assert.That(roundTrippedOutcome.Content!.Profile!.Name, Is.EqualTo("Sample"));
		});
	}

	[Test]
	public void Read_ArchiveCarryingCertificateEntries_StillImports()
	{
		var content = SampleContent();
		var bytes = PortableArchive.Write(new PortableArchiveManifest { Kind = PortableArchiveKind.Profile },
			content,
			SampleIconFiles(content),
			password: null);

		using var withCertificate = new MemoryStream();
		using (var source = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
		using (var target = new ZipArchive(withCertificate, ZipArchiveMode.Create, leaveOpen: true))
		{
			foreach (var entry in source.Entries)
			{
				var copy = target.CreateEntry(entry.FullName, CompressionLevel.NoCompression);
				using var from = entry.Open();
				using var to = copy.Open();
				from.CopyTo(to);
			}

			using (var stream = target.CreateEntry("certificate.json").Open())
			{
				stream.Write("{}"u8);
			}

			using (var stream = target.CreateEntry("certificate.sig").Open())
			{
				stream.Write([1, 2, 3]);
			}
		}

		var outcome = PortableArchive.Read(withCertificate.ToArray(), password: null);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Status, Is.EqualTo(PortableReadStatus.Success));
			Assert.That(outcome.Content!.Profile!.Name, Is.EqualTo("Sample"));
			Assert.That(outcome.Icons, Has.Count.EqualTo(2));
		});
	}

	[Test]
	public void Read_ArchiveWrittenBeforeFilesExisted_StillImportsUnchanged()
	{
		var content = SampleContent();
		var bytes = PortableArchive.Write(new PortableArchiveManifest { Kind = PortableArchiveKind.Profile },
			content,
			SampleIconFiles(content),
			password: null);

		var manifestText = ReadEntryText(bytes, "manifest.json");
		var strippedManifest = JsonNode.Parse(manifestText)!.AsObject();
		strippedManifest.Remove("files");
		var legacy = ReplaceEntry(bytes, "manifest.json", Encoding.UTF8.GetBytes(strippedManifest.ToJsonString()));

		var outcome = PortableArchive.Read(legacy, password: null);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Status, Is.EqualTo(PortableReadStatus.Success));
			Assert.That(outcome.Manifest!.Files, Is.Null);
			Assert.That(outcome.Content!.Profile!.Name, Is.EqualTo("Sample"));
		});
	}

	private static byte[] ReadEntryBytes(ZipArchive zip, string entryName)
	{
		using var stream = zip.GetEntry(entryName)!.Open();
		using var buffer = new MemoryStream();
		stream.CopyTo(buffer);
		return buffer.ToArray();
	}

	[Test]
	public void Read_AnOlderFormatVersion_IsStillAccepted()
	{
		// Grid inheritance (issue #246) bumped the version because a profile archive may now omit an
		// inherited folder's rows/columns; archives written before that must keep importing.
		var content = SampleContent();
		var manifest = new PortableArchiveManifest { Kind = PortableArchiveKind.Profile, FormatVersion = 1 };

		var bytes = PortableArchive.Write(manifest, content, SampleIconFiles(content), password: null);

		Assert.That(PortableArchive.Read(bytes, password: null).Status, Is.EqualTo(PortableReadStatus.Success));
	}

	[Test]
	public void Read_ANewerFormatVersion_IsRefusedRatherThanMisread()
	{
		var content = SampleContent();
		var manifest = new PortableArchiveManifest
		{
			Kind = PortableArchiveKind.Profile,
			FormatVersion = PortableArchiveManifest.CurrentFormatVersion + 1
		};

		var bytes = PortableArchive.Write(manifest, content, SampleIconFiles(content), password: null);

		Assert.That(PortableArchive.Read(bytes, password: null).Status,
			Is.EqualTo(PortableReadStatus.UnsupportedVersion));
	}

	[Test]
	public void Write_Encrypted_ManifestIsReadableWithoutPassword()
	{
		var content = SampleContent();
		var manifest = new PortableArchiveManifest { Kind = PortableArchiveKind.Profile, IncludesSecrets = true };

		var bytes = PortableArchive.Write(manifest, content, SampleIconFiles(content), password: "pw");
		var readManifest = PortableArchive.ReadManifest(bytes);

		Assert.Multiple(() =>
		{
			Assert.That(readManifest, Is.Not.Null);
			Assert.That(readManifest!.Encryption, Is.Not.Null);
			Assert.That(readManifest.IncludesSecrets, Is.True);
		});
	}

	[Test]
	public void Read_Encrypted_WithoutPassword_ReturnsPasswordRequired()
	{
		var content = SampleContent();
		var bytes = PortableArchive.Write(new PortableArchiveManifest { Kind = PortableArchiveKind.Profile },
			content,
			SampleIconFiles(content),
			password: "pw");

		var outcome = PortableArchive.Read(bytes, password: null);

		Assert.That(outcome.Status, Is.EqualTo(PortableReadStatus.PasswordRequired));
	}

	[Test]
	public void Read_Encrypted_WithWrongPassword_ReturnsWrongPassword()
	{
		var content = SampleContent();
		var bytes = PortableArchive.Write(new PortableArchiveManifest { Kind = PortableArchiveKind.Profile },
			content,
			SampleIconFiles(content),
			password: "pw");

		var outcome = PortableArchive.Read(bytes, password: "nope");

		Assert.That(outcome.Status, Is.EqualTo(PortableReadStatus.WrongPassword));
	}

	[Test]
	public void Read_Encrypted_WithCorrectPassword_RoundTrips()
	{
		var content = SampleContent();
		var bytes = PortableArchive.Write(new PortableArchiveManifest { Kind = PortableArchiveKind.Profile },
			content,
			SampleIconFiles(content),
			password: "pw");

		var outcome = PortableArchive.Read(bytes, password: "pw");

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Status, Is.EqualTo(PortableReadStatus.Success));
			Assert.That(outcome.Content!.Profile!.Name, Is.EqualTo("Sample"));
			Assert.That(outcome.Icons, Has.Count.EqualTo(2));
		});
	}

	[Test]
	public void Read_Garbage_ReturnsInvalidArchive()
		=> Assert.That(PortableArchive.Read([1, 2, 3, 4], password: null).Status,
			Is.EqualTo(PortableReadStatus.InvalidArchive));

	[Test]
	public void Write_Encrypted_ManifestCarriesParametersButNoVerifier()
	{
		var content = SampleContent();
		var bytes = PortableArchive.Write(new PortableArchiveManifest { Kind = PortableArchiveKind.Profile },
			content,
			SampleIconFiles(content),
			password: "pw");

		var json = ReadEntryText(bytes, "manifest.json");

		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Not.Contain("passwordHash").IgnoreCase);
			Assert.That(json, Does.Contain("\"cipher\": \"AES-256-GCM\""));
			Assert.That(json, Does.Contain("\"id\": \"PBKDF2-HMAC-SHA256\""));
			Assert.That(json, Does.Contain("\"iterations\": 600000"));
		});
	}

	[Test]
	public void Read_Encrypted_WithEditedManifest_DoesNotOpen()
	{
		var content = SampleContent();
		var bytes = PortableArchive.Write(new PortableArchiveManifest
				{ Kind = PortableArchiveKind.Profile, IncludesSecrets = true },
			content,
			SampleIconFiles(content),
			password: "pw");

		// Flipping the secrets flag must not go unnoticed: the manifest is the key wrap's associated data.
		var tampered = ReplaceEntry(bytes,
			"manifest.json",
			Encoding.UTF8.GetBytes(ReadEntryText(bytes, "manifest.json")
				.Replace("\"includesSecrets\": true", "\"includesSecrets\": false")));

		var outcome = PortableArchive.Read(tampered, password: "pw");

		Assert.That(outcome.Status, Is.EqualTo(PortableReadStatus.WrongPassword));
	}

	[Test]
	public void Read_Encrypted_WithRewrittenIterationCount_DoesNotOpen()
	{
		var content = SampleContent();
		var bytes = PortableArchive.Write(new PortableArchiveManifest { Kind = PortableArchiveKind.Profile },
			content,
			SampleIconFiles(content),
			password: "pw");

		var tampered = ReplaceEntry(bytes,
			"manifest.json",
			Encoding.UTF8.GetBytes(ReadEntryText(bytes, "manifest.json")
				.Replace("\"iterations\": 600000", "\"iterations\": 100000")));

		var outcome = PortableArchive.Read(tampered, password: "pw");

		Assert.That(outcome.Status, Is.EqualTo(PortableReadStatus.WrongPassword));
	}

	[Test]
	public void Read_Encrypted_WithUnknownCipher_ReturnsUnsupportedVersion()
	{
		var content = SampleContent();
		var bytes = PortableArchive.Write(new PortableArchiveManifest { Kind = PortableArchiveKind.Profile },
			content,
			SampleIconFiles(content),
			password: "pw");

		var tampered = ReplaceEntry(bytes,
			"manifest.json",
			Encoding.UTF8.GetBytes(ReadEntryText(bytes, "manifest.json")
				.Replace("AES-256-GCM", "ChaCha20-Poly1305")));

		var outcome = PortableArchive.Read(tampered, password: "pw");

		Assert.That(outcome.Status, Is.EqualTo(PortableReadStatus.UnsupportedVersion));
	}

	[Test]
	public void Read_Encrypted_WithoutPayloadEntry_ReturnsInvalidArchive()
	{
		var content = SampleContent();
		var bytes = PortableArchive.Write(new PortableArchiveManifest { Kind = PortableArchiveKind.Profile },
			content,
			SampleIconFiles(content),
			password: "pw");

		var stripped = ReplaceEntry(bytes, "payload.enc", null);

		Assert.That(PortableArchive.Read(stripped, password: "pw").Status,
			Is.EqualTo(PortableReadStatus.InvalidArchive));
	}

	[Test]
	public void Read_WithOversizedContentEntry_ReturnsInvalidArchive()
	{
		using var buffer = new MemoryStream();
		using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
		{
			var manifest = zip.CreateEntry("manifest.json");
			using (var stream = manifest.Open())
			{
				stream.Write("{\"formatVersion\":1,\"kind\":\"Profile\"}"u8);
			}

			var entry = zip.CreateEntry("content.json", CompressionLevel.Optimal);
			using var content = entry.Open();
			var chunk = new byte[1024 * 1024];
			Array.Fill(chunk, (byte)' ');
			for (var written = 0; written < 70; written++)
			{
				content.Write(chunk);
			}
		}

		Assert.That(PortableArchive.Read(buffer.ToArray(), password: null).Status,
			Is.EqualTo(PortableReadStatus.InvalidArchive));
	}

	private static string ReadEntryText(byte[] archiveBytes, string entryName)
	{
		using var zip = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read);
		using var stream = zip.GetEntry(entryName)!.Open();
		using var reader = new StreamReader(stream);
		return reader.ReadToEnd();
	}

	private static byte[] ReplaceEntry(byte[] archiveBytes, string entryName, byte[]? replacement)
	{
		using var source = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read);
		using var buffer = new MemoryStream();
		using (var target = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
		{
			foreach (var entry in source.Entries)
			{
				if (entry.FullName == entryName)
				{
					continue;
				}

				var copy = target.CreateEntry(entry.FullName, CompressionLevel.NoCompression);
				using var from = entry.Open();
				using var to = copy.Open();
				from.CopyTo(to);
			}

			if (replacement is not null)
			{
				var created = target.CreateEntry(entryName, CompressionLevel.NoCompression);
				using var stream = created.Open();
				stream.Write(replacement);
			}
		}

		return buffer.ToArray();
	}
}
