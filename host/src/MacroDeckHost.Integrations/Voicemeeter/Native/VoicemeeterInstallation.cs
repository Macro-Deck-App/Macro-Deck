using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MacroDeckHost.Integrations.Voicemeeter.Native;

internal static class VoicemeeterInstallation
{
	private const string UninstallKey =
		@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VB:Voicemeeter {17359A74-1236-5467}";

	private const string UninstallValue = "UninstallString";

	private static readonly string[] _fallbackFolders =
	[
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VB", "Voicemeeter"),
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VB", "Voicemeeter")
	];

	private static string LibraryFileName
		=> Environment.Is64BitProcess ? "VoicemeeterRemote64.dll" : "VoicemeeterRemote.dll";

	[SupportedOSPlatform("windows")]
	public static string? FindRemoteLibrary()
	{
		foreach (var folder in CandidateFolders())
		{
			var candidate = Path.Combine(folder, LibraryFileName);
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}

		return null;
	}

	[SupportedOSPlatform("windows")]
	private static IEnumerable<string> CandidateFolders()
	{
		if (ReadInstallFolder(RegistryView.Registry32) is { } from32)
		{
			yield return from32;
		}

		if (ReadInstallFolder(RegistryView.Registry64) is { } from64)
		{
			yield return from64;
		}

		foreach (var folder in _fallbackFolders)
		{
			if (!string.IsNullOrEmpty(folder))
			{
				yield return folder;
			}
		}
	}

	[SupportedOSPlatform("windows")]
	private static string? ReadInstallFolder(RegistryView view)
	{
		try
		{
			using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
			using var key = baseKey.OpenSubKey(UninstallKey);
			if (key?.GetValue(UninstallValue) is not string uninstallString)
			{
				return null;
			}

			var executable = ExtractExecutablePath(uninstallString);
			return string.IsNullOrEmpty(executable) ? null : Path.GetDirectoryName(executable);
		}
		catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
		{
			return null;
		}
	}

	internal static string ExtractExecutablePath(string uninstallString)
	{
		var value = uninstallString.Trim();
		if (value.StartsWith('"'))
		{
			var closing = value.IndexOf('"', 1);
			return closing > 0 ? value[1..closing] : value.Trim('"');
		}

		var extension = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
		return extension >= 0 ? value[..(extension + 4)] : value;
	}
}
