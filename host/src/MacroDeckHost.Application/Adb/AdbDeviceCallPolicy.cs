using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeckHost.Application.HostLocking;

namespace MacroDeckHost.Application.Adb;

// ADR 0092: the limits on a device call that hold for plugins and built-in integrations alike. Only the
// consent gate (ADB for plugins allowed, host:adb declared) is the plugin path's own.
public sealed class AdbDeviceCallPolicy(IHostLockState lockState)
{
	// Leaves room for the envelope inside ProtocolLimits.MaxMessageBytes.
	public const int ShellResultBudgetBytes = 192 * 1024;

	// ADR 0092: only the open-ended operations wait for an unlock. The typed ones (key, tap, reboot and the
	// rest) run while locked for every caller, so deck buttons keep working.
	private static readonly HashSet<string> _refusedWhileLocked = new(StringComparer.Ordinal)
	{
		HostOperations.Adb.Shell,
		HostOperations.Adb.Push,
		HostOperations.Adb.Pull,
		HostOperations.Adb.Install,
		HostOperations.Adb.Uninstall,
		HostOperations.Adb.Connect
	};

	public bool IsRefusedWhileLocked(string operation)
	{
		return lockState.IsLocked && _refusedWhileLocked.Contains(operation);
	}

	public static AdbShellResultDto FitToBudget(AdbShellOutput output)
	{
		var dto = new AdbShellResultDto
		{
			ExitCode = output.ExitCode,
			StandardOutput = output.StandardOutput,
			StandardError = output.StandardError,
			Truncated = output.Truncated
		};

		while (JsonSerializer.SerializeToUtf8Bytes(dto, PluginProtocolJson.Options).Length > ShellResultBudgetBytes)
		{
			dto = dto.StandardOutput.Length >= dto.StandardError.Length
				? dto with { StandardOutput = dto.StandardOutput[..(dto.StandardOutput.Length / 2)], Truncated = true }
				: dto with { StandardError = dto.StandardError[..(dto.StandardError.Length / 2)], Truncated = true };
		}

		return dto;
	}
}
