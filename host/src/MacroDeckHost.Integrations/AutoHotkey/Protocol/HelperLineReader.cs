using System.Runtime.CompilerServices;
using System.Text;

namespace MacroDeckHost.Integrations.AutoHotkey.Protocol;

internal readonly record struct HelperLine(string Text, bool Complete, bool Truncated);

internal static class HelperLineReader
{
	public const char RecordSeparator = '\u001e';

	public static async IAsyncEnumerable<HelperLine> ReadAsync(
		TextReader reader,
		int maxMessageLength,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var buffer = new char[4096];
		var line = new StringBuilder();
		var messageStart = -1;
		var discarding = false;

		while (true)
		{
			var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
			if (read == 0)
			{
				break;
			}

			for (var index = 0; index < read; index++)
			{
				var character = buffer[index];
				if (character == '\n')
				{
					yield return new HelperLine(TrimCarriageReturn(line), true, discarding);
					line.Clear();
					messageStart = -1;
					discarding = false;
					continue;
				}

				if (discarding)
				{
					continue;
				}

				if (character == RecordSeparator && messageStart < 0)
				{
					messageStart = line.Length;
				}

				line.Append(character);
				if (messageStart < 0 && line.Length >= maxMessageLength)
				{
					yield return new HelperLine(line.ToString(), false, false);
					line.Clear();
				}
				else if (messageStart >= 0 && line.Length - messageStart > maxMessageLength)
				{
					discarding = true;
				}
			}
		}

		if (line.Length > 0 || discarding)
		{
			yield return new HelperLine(TrimCarriageReturn(line), true, discarding);
		}
	}

	private static string TrimCarriageReturn(StringBuilder line)
	{
		var length = line.Length > 0 && line[^1] == '\r' ? line.Length - 1 : line.Length;
		return line.ToString(0, length);
	}
}
