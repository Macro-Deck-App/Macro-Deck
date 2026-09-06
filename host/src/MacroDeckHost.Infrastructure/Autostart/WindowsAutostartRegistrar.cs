using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MacroDeckHost.Infrastructure.Autostart;

[SupportedOSPlatform("windows")]
public sealed class WindowsAutostartRegistrar : IAutostartRegistrar
{
	private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

	public bool IsSupported => true;

	public AutostartRegistration? Read()
	{
		using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
		return AutostartEntryContent.ParseCommandLine(
			key?.GetValue(AutostartEntryContent.WindowsRunValueName) as string);
	}

	public void Write(AutostartRegistration registration)
	{
		using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
		key.SetValue(AutostartEntryContent.WindowsRunValueName,
			AutostartEntryContent.BuildCommandLine(registration));
	}

	public void Remove()
	{
		using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
		key?.DeleteValue(AutostartEntryContent.WindowsRunValueName, false);
	}
}
