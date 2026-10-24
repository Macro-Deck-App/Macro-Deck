using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace MacroDeckHost.Integrations.AutoHotkey.Native;

internal enum AutoHotkeyRegistryScope
{
	Machine,
	User
}

internal sealed record AutoHotkeyFileVersion(int Major, string Display);

internal interface IAutoHotkeyMachine
{
	bool Is64BitOperatingSystem { get; }

	string? ProgramFilesFolder { get; }

	string? LocalAppDataFolder { get; }

	string? ReadRegistryString(AutoHotkeyRegistryScope scope, string keyPath, string valueName);

	bool FileExists(string path);

	AutoHotkeyFileVersion? ReadFileVersion(string path);

	Task<bool> CanRunAsync(string executablePath, CancellationToken cancellationToken);
}

internal sealed class AutoHotkeyMachine : IAutoHotkeyMachine
{
	private static readonly TimeSpan _runCheckTimeout = TimeSpan.FromSeconds(5);

	public bool Is64BitOperatingSystem => Environment.Is64BitOperatingSystem;

	public string? ProgramFilesFolder => NonEmpty(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));

	public string? LocalAppDataFolder
		=> NonEmpty(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

	public string? ReadRegistryString(AutoHotkeyRegistryScope scope, string keyPath, string valueName)
	{
		if (!OperatingSystem.IsWindows())
		{
			return null;
		}

		return scope == AutoHotkeyRegistryScope.Machine
			? Read(RegistryHive.LocalMachine, RegistryView.Registry64, keyPath, valueName)
			?? Read(RegistryHive.LocalMachine, RegistryView.Registry32, keyPath, valueName)
			: Read(RegistryHive.CurrentUser, RegistryView.Default, keyPath, valueName);
	}

	public bool FileExists(string path)
	{
		return File.Exists(path);
	}

	public AutoHotkeyFileVersion? ReadFileVersion(string path)
	{
		try
		{
			var info = FileVersionInfo.GetVersionInfo(path);
			var display = string.IsNullOrWhiteSpace(info.ProductVersion)
				? $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}"
				: info.ProductVersion.Trim();
			return new AutoHotkeyFileVersion(info.FileMajorPart, display);
		}
		catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	// Validating an empty script proves the interpreter starts and understands the version 2 switches.
	public async Task<bool> CanRunAsync(string executablePath, CancellationToken cancellationToken)
	{
		var folder = Path.Combine(Path.GetTempPath(), "MacroDeck", "autohotkey-check");
		var script = Path.Combine(folder, $"{Guid.NewGuid():N}.ahk");
		try
		{
			Directory.CreateDirectory(folder);
			await File.WriteAllTextAsync(script, string.Empty, cancellationToken).ConfigureAwait(false);

			var startInfo = new ProcessStartInfo(executablePath)
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			startInfo.ArgumentList.Add("/ErrorStdOut=UTF-8");
			startInfo.ArgumentList.Add("/Validate");
			startInfo.ArgumentList.Add(script);

			using var process = Process.Start(startInfo);
			if (process is null)
			{
				return false;
			}

			_ = process.StandardOutput.ReadToEndAsync(cancellationToken);
			_ = process.StandardError.ReadToEndAsync(cancellationToken);

			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(_runCheckTimeout);
			try
			{
				await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				process.Kill(false);
				cancellationToken.ThrowIfCancellationRequested();
				return false;
			}

			return process.ExitCode == 0;
		}
		catch (Exception ex) when (ex is IOException
			or UnauthorizedAccessException
			or InvalidOperationException
			or Win32Exception)
		{
			return false;
		}
		finally
		{
			try
			{
				File.Delete(script);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
			}
		}
	}

	private static string? NonEmpty(string value)
	{
		return string.IsNullOrEmpty(value) ? null : value;
	}

	[SupportedOSPlatform("windows")]
	private static string? Read(RegistryHive hive, RegistryView view, string keyPath, string valueName)
	{
		try
		{
			using var baseKey = RegistryKey.OpenBaseKey(hive, view);
			using var key = baseKey.OpenSubKey(keyPath);
			return key?.GetValue(valueName) is string { Length: > 0 } value ? value : null;
		}
		catch (Exception ex) when (ex is UnauthorizedAccessException
			or IOException
			or ArgumentException
			or SecurityException)
		{
			return null;
		}
	}
}
