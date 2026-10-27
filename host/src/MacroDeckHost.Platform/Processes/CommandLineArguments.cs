using System.Text;

namespace MacroDeckHost.Platform.Processes;

// Splits ProcessStartInfo.Arguments exactly as Process.Start does on Unix, so arguments a user typed
// reach a detached program the way they reached it before.
internal static class CommandLineArguments
{
	public static List<string> Split(string? arguments)
	{
		var results = new List<string>();
		if (string.IsNullOrEmpty(arguments))
		{
			return results;
		}

		var index = 0;
		while (true)
		{
			while (index < arguments.Length && arguments[index] is ' ' or '\t')
			{
				index++;
			}

			if (index == arguments.Length)
			{
				return results;
			}

			results.Add(ReadArgument(arguments, ref index));
		}
	}

	private static string ReadArgument(string arguments, ref int index)
	{
		var current = new StringBuilder();
		var quoted = false;
		while (index < arguments.Length)
		{
			var backslashes = 0;
			while (index < arguments.Length && arguments[index] == '\\')
			{
				index++;
				backslashes++;
			}

			if (backslashes > 0)
			{
				if (index >= arguments.Length || arguments[index] != '"')
				{
					current.Append('\\', backslashes);
				}
				else
				{
					current.Append('\\', backslashes / 2);
					if (backslashes % 2 != 0)
					{
						current.Append('"');
						index++;
					}
				}

				continue;
			}

			var character = arguments[index];
			if (character == '"')
			{
				if (quoted && index + 1 < arguments.Length && arguments[index + 1] == '"')
				{
					current.Append('"');
					index++;
				}
				else
				{
					quoted = !quoted;
				}

				index++;
				continue;
			}

			if (character is ' ' or '\t' && !quoted)
			{
				break;
			}

			current.Append(character);
			index++;
		}

		return current.ToString();
	}
}
