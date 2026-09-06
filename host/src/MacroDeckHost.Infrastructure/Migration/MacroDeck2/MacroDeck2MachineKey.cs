using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MacroDeckHost.Infrastructure.Migration.MacroDeck2;

/// <summary>
/// The passphrase Macro Deck 2 encrypts plugin credentials with: the Windows installation's
/// <c>MachineGuid</c>, read from the 64-bit registry view exactly as its <c>StringCipher.GetMachineGuid</c>
/// does.
/// </summary>
/// <remarks>
/// Because the key belongs to the machine and not to the data, a directory copied off another computer
/// cannot be decrypted here even on Windows. That is not a failure to recover from silently - the caller
/// asks the user for the key instead.
/// </remarks>
internal interface IMacroDeck2MachineKeyReader
{
	string? TryRead();
}

internal sealed class MacroDeck2MachineKeyReader : IMacroDeck2MachineKeyReader
{
	private const string KeyPath = @"SOFTWARE\Microsoft\Cryptography";

	private const string ValueName = "MachineGuid";

	public string? TryRead()
	{
		if (!OperatingSystem.IsWindows())
		{
			return null;
		}

		return ReadFromRegistry();
	}

	[SupportedOSPlatform("windows")]
	private static string? ReadFromRegistry()
	{
		try
		{
			using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
			using var key = localMachine.OpenSubKey(KeyPath);
			var value = key?.GetValue(ValueName) as string;
			return string.IsNullOrWhiteSpace(value) ? null : value;
		}
		catch (Exception ex) when (
			ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
		{
			return null;
		}
	}
}
