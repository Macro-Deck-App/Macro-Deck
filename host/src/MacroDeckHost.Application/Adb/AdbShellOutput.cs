namespace MacroDeckHost.Application.Adb;

public sealed record AdbShellOutput(int ExitCode, string StandardOutput, string StandardError, bool Truncated);
