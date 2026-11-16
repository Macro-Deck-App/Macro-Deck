using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests.Support;

internal static class CorruptArtifacts
{
	private const uint LocalFileHeaderSignature = 0x04034b50;
	private const int LocalFileHeaderLength = 30;

	public static async Task CorruptFirstEntryAsync(string artifactPath)
	{
		string name;
		long compressedLength;

		await using (var archive = await ZipFile.OpenReadAsync(artifactPath))
		{
			var entry = archive.Entries.First(candidate => !candidate.FullName.EndsWith('/'));
			name = entry.FullName;
			compressedLength = entry.CompressedLength;
		}

		var bytes = await File.ReadAllBytesAsync(artifactPath);
		var nameBytes = Encoding.UTF8.GetBytes(name);

		for (var offset = 0; offset + LocalFileHeaderLength <= bytes.Length; offset++)
		{
			if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)) != LocalFileHeaderSignature)
			{
				continue;
			}

			var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 26));
			var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 28));

			if (!bytes.AsSpan(offset + LocalFileHeaderLength, nameLength).SequenceEqual(nameBytes))
			{
				continue;
			}

			bytes.AsSpan(offset + LocalFileHeaderLength + nameLength + extraLength, (int)compressedLength).Fill(0xFF);
			await File.WriteAllBytesAsync(artifactPath, bytes);

			return;
		}

		throw new InvalidOperationException($"No local header for '{name}' in '{artifactPath}'.");
	}
}
