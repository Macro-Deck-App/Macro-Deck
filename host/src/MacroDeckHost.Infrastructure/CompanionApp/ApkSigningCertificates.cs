using System.Buffers.Binary;
using System.Formats.Asn1;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace MacroDeckHost.Infrastructure.CompanionApp;

internal static partial class ApkSigningCertificates
{
	private const string SignedDataOid = "1.2.840.113549.1.7.2";
	private const uint EndOfCentralDirectoryMagic = 0x06054b50;
	private const int EndOfCentralDirectorySize = 22;
	private const int MaxCommentLength = 0xFFFF;
	private const uint SignatureSchemeV2 = 0x7109871a;
	private const uint SignatureSchemeV3 = 0xf05368c0;
	private const long MaxSigningBlockSize = 16 * 1024 * 1024;

	private static readonly byte[] SigningBlockMagic = "APK Sig Block 42"u8.ToArray();

	// Reads the signer certificates without verifying the signatures: Android does that at install.
	public static IReadOnlyList<string> ReadSignerCertificateSha256(Stream apk)
	{
		var pairs = ReadSigningBlock(apk);
		if (pairs is null)
		{
			return [];
		}

		var certificates = new List<string>();
		var offset = 0;
		while (offset < pairs.Length)
		{
			if (pairs.Length - offset < 12)
			{
				return [];
			}

			var length = BinaryPrimitives.ReadInt64LittleEndian(pairs.AsSpan(offset));
			if (length < 4 || length > pairs.Length - offset - 8)
			{
				return [];
			}

			var id = BinaryPrimitives.ReadUInt32LittleEndian(pairs.AsSpan(offset + 8));
			var value = pairs.AsMemory(offset + 12, (int)length - 4);
			if (id is SignatureSchemeV2 or SignatureSchemeV3)
			{
				if (!TryReadSignerCertificates(value.Span, certificates))
				{
					return [];
				}
			}

			offset += 8 + (int)length;
		}

		return certificates;
	}

	// Android 6 ignores the v2 block and verifies only the JAR signature, so its signer has to match too.
	// Null means a signature file that could not be read.
	public static IReadOnlyList<string>? ReadJarSignerCertificateSha256(Stream apk)
	{
		try
		{
			apk.Seek(0, SeekOrigin.Begin);
			using var archive = new ZipArchive(apk, ZipArchiveMode.Read, leaveOpen: true);
			var certificates = new List<string>();
			foreach (var entry in archive.Entries.Where(entry => JarSignatureRegex().IsMatch(entry.FullName)))
			{
				if (entry.Length > MaxSigningBlockSize)
				{
					return null;
				}

				using var stream = entry.Open();
				using var buffer = new MemoryStream();
				stream.CopyTo(buffer);
				if (!TryReadSignedDataCertificates(buffer.ToArray(), certificates))
				{
					return null;
				}
			}

			return certificates;
		}
		catch (Exception ex) when (ex is InvalidDataException or IOException)
		{
			return null;
		}
	}

	private static bool TryReadSignedDataCertificates(byte[] pkcs7, List<string> certificates)
	{
		try
		{
			var contentInfo = new AsnReader(pkcs7, AsnEncodingRules.BER).ReadSequence();
			if (contentInfo.ReadObjectIdentifier() != SignedDataOid)
			{
				return false;
			}

			var signedData = contentInfo.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0)).ReadSequence();
			signedData.ReadInteger();
			signedData.ReadSetOf(skipSortOrderValidation: true);
			signedData.ReadSequence();
			var tag = new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true);
			if (!signedData.HasData || !signedData.PeekTag().HasSameClassAndValue(tag))
			{
				return false;
			}

			var set = signedData.ReadSetOf(skipSortOrderValidation: true, tag);
			var found = false;
			while (set.HasData)
			{
				certificates.Add(Convert.ToHexStringLower(SHA256.HashData(set.ReadEncodedValue().Span)));
				found = true;
			}

			return found;
		}
		catch (AsnContentException)
		{
			return false;
		}
	}

	private static byte[]? ReadSigningBlock(Stream apk)
	{
		var length = apk.Length;
		if (length < EndOfCentralDirectorySize)
		{
			return null;
		}

		var tailLength = (int)Math.Min(length, MaxCommentLength + EndOfCentralDirectorySize);
		var tail = new byte[tailLength];
		apk.Seek(length - tailLength, SeekOrigin.Begin);
		apk.ReadExactly(tail);

		var eocd = -1;
		for (var index = tailLength - EndOfCentralDirectorySize; index >= 0; index--)
		{
			if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(index)) == EndOfCentralDirectoryMagic &&
				BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(index + 20)) ==
				tailLength - EndOfCentralDirectorySize - index)
			{
				eocd = index;
				break;
			}
		}

		if (eocd < 0)
		{
			return null;
		}

		long centralDirectory = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(eocd + 16));
		if (centralDirectory < 32 || centralDirectory > length)
		{
			return null;
		}

		var footer = new byte[24];
		apk.Seek(centralDirectory - 24, SeekOrigin.Begin);
		apk.ReadExactly(footer);
		if (!footer.AsSpan(8).SequenceEqual(SigningBlockMagic))
		{
			return null;
		}

		var blockSize = BinaryPrimitives.ReadInt64LittleEndian(footer);
		if (blockSize < 24 || blockSize > MaxSigningBlockSize || blockSize > centralDirectory - 8)
		{
			return null;
		}

		var blockStart = centralDirectory - blockSize - 8;
		var block = new byte[blockSize + 8];
		apk.Seek(blockStart, SeekOrigin.Begin);
		apk.ReadExactly(block);
		if (BinaryPrimitives.ReadInt64LittleEndian(block) != blockSize)
		{
			return null;
		}

		return block[8..^24];
	}

	private static bool TryReadSignerCertificates(ReadOnlySpan<byte> scheme, List<string> certificates)
	{
		if (!TryReadPrefixed(ref scheme, out var signers) || signers.IsEmpty)
		{
			return false;
		}

		while (!signers.IsEmpty)
		{
			if (!TryReadPrefixed(ref signers, out var signer) ||
				!TryReadPrefixed(ref signer, out var signedData) ||
				!TryReadPrefixed(ref signedData, out _) ||
				!TryReadPrefixed(ref signedData, out var certificateList) ||
				!TryReadPrefixed(ref certificateList, out var certificate) ||
				certificate.IsEmpty)
			{
				return false;
			}

			certificates.Add(Convert.ToHexStringLower(SHA256.HashData(certificate)));
		}

		return true;
	}

	private static bool TryReadPrefixed(ref ReadOnlySpan<byte> source, out ReadOnlySpan<byte> value)
	{
		value = default;
		if (source.Length < 4)
		{
			return false;
		}

		var length = BinaryPrimitives.ReadUInt32LittleEndian(source);
		if (length > source.Length - 4)
		{
			return false;
		}

		value = source.Slice(4, (int)length);
		source = source[(4 + (int)length)..];
		return true;
	}

	[GeneratedRegex(@"^META-INF/[^/]+\.(RSA|DSA|EC)$", RegexOptions.IgnoreCase)]
	private static partial Regex JarSignatureRegex();
}
