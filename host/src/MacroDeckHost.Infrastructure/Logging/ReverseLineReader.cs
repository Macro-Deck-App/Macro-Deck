using System.Text;

namespace MacroDeckHost.Infrastructure.Logging;

public readonly record struct LogFileLine(long Offset, string Text);

public static class ReverseLineReader
{
	public const int ChunkBytes = 64 * 1024;

	public const int MaxLineBytes = 1024 * 1024;

	public static IEnumerable<LogFileLine> Read(string path, long endOffset)
	{
		using var stream = OpenShared(path);

		var position = Math.Min(endOffset, stream.Length);
		var carry = Array.Empty<byte>();
		var buffer = new byte[ChunkBytes];

		while (position > 0)
		{
			var size = (int)Math.Min(ChunkBytes, position);
			position -= size;

			stream.Seek(position, SeekOrigin.Begin);
			ReadExactly(stream, buffer, size);

			var block = new byte[size + carry.Length];
			Array.Copy(buffer, block, size);
			Array.Copy(carry, 0, block, size, carry.Length);

			var lineEnd = block.Length;
			for (var i = block.Length - 1; i >= 0; i--)
			{
				if (block[i] != (byte)'\n')
				{
					continue;
				}

				if (lineEnd > i + 1)
				{
					yield return new LogFileLine(position + i + 1,
						Decode(block, i + 1, lineEnd - i - 1, atFileStart: false));
				}

				lineEnd = i;
			}

			carry = new byte[lineEnd];
			Array.Copy(block, carry, lineEnd);

			if (carry.Length > MaxLineBytes)
			{
				yield return new LogFileLine(position, Decode(carry, 0, carry.Length, position == 0));
				carry = [];
			}
		}

		if (carry.Length > 0)
		{
			yield return new LogFileLine(0, Decode(carry, 0, carry.Length, atFileStart: true));
		}
	}

	public static FileStream OpenShared(string path)
		=> new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

	public static long EndOfLastCompleteLine(string path)
	{
		using var stream = OpenShared(path);

		var position = stream.Length;
		var buffer = new byte[ChunkBytes];
		while (position > 0)
		{
			var size = (int)Math.Min(ChunkBytes, position);
			position -= size;

			stream.Seek(position, SeekOrigin.Begin);
			ReadExactly(stream, buffer, size);

			for (var i = size - 1; i >= 0; i--)
			{
				if (buffer[i] == (byte)'\n')
				{
					return position + i + 1;
				}
			}
		}

		return 0;
	}

	private static void ReadExactly(Stream stream, byte[] buffer, int count)
	{
		var read = 0;
		while (read < count)
		{
			var chunk = stream.Read(buffer, read, count - read);
			if (chunk == 0)
			{
				break;
			}

			read += chunk;
		}
	}

	private static string Decode(byte[] buffer, int start, int length, bool atFileStart)
	{
		// Cuts only ever land on '\n', which never appears inside a multi-byte UTF-8 sequence, so a
		// chunk boundary cannot split a character.
		var text = Encoding.UTF8.GetString(buffer, start, length).TrimEnd('\r');

		return atFileStart && text.StartsWith('﻿') ? text[1..] : text;
	}
}
