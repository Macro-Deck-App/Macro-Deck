using System.Buffers.Binary;
using System.Formats.Asn1;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using MacroDeckHost.Infrastructure.CompanionApp;

namespace MacroDeckHost.Tests.UnitTests.CompanionApp;

public class ApkSigningCertificatesTests
{
	[Test]
	public void The_certificate_of_every_signer_is_hashed()
	{
		byte[] first = [1, 2, 3], second = [4, 5];

		var hashes = Read(Apk([first, second]));

		Assert.That(hashes, Is.EqualTo(new[] { Hex(first), Hex(second) }));
	}

	[Test]
	public void An_apk_without_a_v2_or_v3_signature_has_no_signers()
	{
		Assert.That(Read(Apk([[1, 2, 3]], blockId: 0x42726577)), Is.Empty);
	}

	[Test]
	public void A_plain_zip_has_no_signers()
	{
		Assert.That(Read(Zip([])), Is.Empty);
	}

	[Test]
	public void The_jar_signature_certificates_are_hashed_for_android_6()
	{
		byte[] certificate = Certificate(7);

		using var stream = new MemoryStream(Apk([certificate], jarCertificates: [certificate]));

		Assert.That(ApkSigningCertificates.ReadJarSignerCertificateSha256(stream), Is.EqualTo(new[] { Hex(certificate) }));
	}

	[Test]
	public void An_unreadable_jar_signature_is_reported_as_such()
	{
		using var stream = new MemoryStream(Apk([Certificate(7)], jarSignature: [1, 2, 3]));

		Assert.That(ApkSigningCertificates.ReadJarSignerCertificateSha256(stream), Is.Null);
	}

	[Test]
	public void A_truncated_signer_is_rejected_rather_than_partly_read()
	{
		var apk = Apk([[1, 2, 3]], corruptSigner: true);

		Assert.That(Read(apk), Is.Empty);
	}

	internal static string Hex(byte[] certificate) => Convert.ToHexStringLower(SHA256.HashData(certificate));

	internal static byte[] Certificate(int serial)
	{
		var writer = new AsnWriter(AsnEncodingRules.DER);
		using (writer.PushSequence())
		{
			writer.WriteInteger(serial);
		}

		return writer.Encode();
	}

	internal static byte[] Apk(IReadOnlyList<byte[]> certificates,
		uint blockId = 0x7109871a,
		bool corruptSigner = false,
		IReadOnlyList<byte[]>? jarCertificates = null,
		byte[]? jarSignature = null)
	{
		var signers = new List<byte>();
		foreach (var certificate in certificates)
		{
			var signedData = Concat(Prefixed([]), Prefixed(Prefixed(certificate)));
			var signer = Prefixed(signedData);
			signers.AddRange(Prefixed(corruptSigner ? signer[..^1] : signer));
		}

		var value = Prefixed([.. signers]);
		var pair = new byte[12 + value.Length];
		BinaryPrimitives.WriteInt64LittleEndian(pair, 4 + value.Length);
		BinaryPrimitives.WriteUInt32LittleEndian(pair.AsSpan(8), blockId);
		value.CopyTo(pair, 12);

		var size = pair.Length + 24L;
		var block = new byte[8 + size];
		BinaryPrimitives.WriteInt64LittleEndian(block, size);
		pair.CopyTo(block, 8);
		BinaryPrimitives.WriteInt64LittleEndian(block.AsSpan(8 + pair.Length), size);
		Encoding.ASCII.GetBytes("APK Sig Block 42").CopyTo(block, 16 + pair.Length);
		var signature = jarSignature ?? (jarCertificates is null ? null : SignedData(jarCertificates));
		return Zip(block, signature);
	}

	private static byte[] Zip(byte[] signingBlock, byte[]? jarSignature = null)
	{
		using var buffer = new MemoryStream();
		using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
		{
			using (var manifest = archive.CreateEntry("AndroidManifest.xml").Open())
			{
				manifest.Write([1, 2, 3]);
			}

			if (jarSignature is not null)
			{
				using var entry = archive.CreateEntry("META-INF/CERT.RSA").Open();
				entry.Write(jarSignature);
			}
		}

		var zip = buffer.ToArray();
		var eocd = zip.Length - 22;
		var centralDirectory = (int)BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(eocd + 16));
		var result = Concat(zip[..centralDirectory], signingBlock, zip[centralDirectory..]);
		BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(result.Length - 22 + 16),
			(uint)(centralDirectory + signingBlock.Length));
		return result;
	}

	private static byte[] SignedData(IReadOnlyList<byte[]> certificates)
	{
		var writer = new AsnWriter(AsnEncodingRules.DER);
		using (writer.PushSequence())
		{
			writer.WriteObjectIdentifier("1.2.840.113549.1.7.2");
			using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
			using (writer.PushSequence())
			{
				writer.WriteInteger(1);
				using (writer.PushSetOf())
				{
				}

				using (writer.PushSequence())
				{
					writer.WriteObjectIdentifier("1.2.840.113549.1.7.1");
				}

				using (writer.PushSetOf(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
				{
					foreach (var certificate in certificates)
					{
						writer.WriteEncodedValue(certificate);
					}
				}

				using (writer.PushSetOf())
				{
				}
			}
		}

		return writer.Encode();
	}

	private static IReadOnlyList<string> Read(byte[] apk)
	{
		using var stream = new MemoryStream(apk);
		return ApkSigningCertificates.ReadSignerCertificateSha256(stream);
	}

	private static byte[] Prefixed(byte[] value)
	{
		var result = new byte[4 + value.Length];
		BinaryPrimitives.WriteUInt32LittleEndian(result, (uint)value.Length);
		value.CopyTo(result, 4);
		return result;
	}

	private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(part => part)];
}
