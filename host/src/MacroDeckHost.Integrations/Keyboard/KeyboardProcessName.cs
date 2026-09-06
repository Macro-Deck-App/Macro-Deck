using System.ComponentModel;
using System.Diagnostics;

namespace MacroDeckHost.Integrations.Keyboard;

internal static class KeyboardProcessName
{
	public static bool Matches(string? actual, string configured)
		=> !string.IsNullOrWhiteSpace(actual) &&
			string.Equals(Normalize(actual), Normalize(configured), StringComparison.OrdinalIgnoreCase);

	public static string? ForPid(int pid)
	{
		try
		{
			using var process = Process.GetProcessById(pid);
			return process.ProcessName;
		}
		catch (ArgumentException)
		{
			return null;
		}
		catch (InvalidOperationException)
		{
			return null;
		}
	}

	public static IReadOnlyList<Process> Find(string configured)
	{
		var normalized = Normalize(configured);
		if (normalized.Length == 0)
		{
			return [];
		}

		try
		{
			return Process.GetProcessesByName(normalized);
		}
		catch (InvalidOperationException)
		{
			return [];
		}
		catch (Win32Exception)
		{
			return [];
		}
	}

	private static string Normalize(string name)
	{
		var trimmed = name.Trim();
		foreach (var suffix in _strippedSuffixes)
		{
			if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
			{
				trimmed = trimmed[..^suffix.Length];
				break;
			}
		}

		return trimmed;
	}

	private static readonly string[] _strippedSuffixes = [".exe", ".app"];
}
