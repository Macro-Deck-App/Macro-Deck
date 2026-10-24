using System.Security.Cryptography;
using System.Text;
using MacroDeck.Localization;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Integrations.AutoHotkey.Providers;

internal enum AutoHotkeyTargetKind
{
	Managed,
	File,
	Inline
}

internal enum AutoHotkeyRunPolicy
{
	StartAnother,
	Ignore,
	Restart,
	Fail
}

internal enum AutoHotkeyRunState
{
	Starting,
	Running,
	Exited,
	FailedToStart,
	TimedOut
}

internal static class AutoHotkeyRunErrorCodes
{
	public const string ExitCode = "EXIT_CODE";
	public const string ProcessStartFailed = "PROCESS_START_FAILED";
}

internal static class AutoHotkeyFailureReasons
{
	public const string StartFailed = "start_failed";
	public const string LoadError = "load_error";
	public const string RuntimeError = "runtime_error";
	public const string Timeout = "timeout";
}

internal sealed record AutoHotkeyRunRequest
{
	public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);
	public static readonly TimeSpan MaxTimeout = TimeSpan.FromMinutes(9);

	public required AutoHotkeyTargetKind Kind { get; init; }

	public Guid? ScriptId { get; init; }

	public string? FilePath { get; init; }

	public string? Code { get; init; }

	public string? SourceOverride { get; init; }

	public IReadOnlyDictionary<string, string> Parameters { get; init; } =
		new Dictionary<string, string>(StringComparer.Ordinal);

	public IReadOnlyList<string> Arguments { get; init; } = [];

	public string? WorkingDirectory { get; init; }

	public bool? IncludeHelper { get; init; }

	public AutoHotkeyRunPolicy Policy { get; init; } = AutoHotkeyRunPolicy.Ignore;

	public bool Wait { get; init; }

	public TimeSpan Timeout { get; init; } = DefaultTimeout;

	public UserVariableContext VariableContext { get; init; } = new();
}

internal enum AutoHotkeyRunStatus
{
	Started,
	Ignored,
	Completed,
	Failed
}

internal sealed record AutoHotkeyRunResult(AutoHotkeyRunStatus Status)
{
	public static AutoHotkeyRunResult Ignored { get; } = new(AutoHotkeyRunStatus.Ignored);

	public Guid? RunId { get; init; }

	public string? ErrorCode { get; init; }

	public LocalizedText ErrorMessage { get; init; }

	public int? ExitCode { get; init; }

	public string? Result { get; init; }

	public string? Output { get; init; }

	public static AutoHotkeyRunResult Failed(string code, LocalizedText message, Guid? runId = null)
	{
		return new AutoHotkeyRunResult(AutoHotkeyRunStatus.Failed)
		{
			ErrorCode = code, ErrorMessage = message, RunId = runId
		};
	}
}

internal sealed record AutoHotkeyScriptError(string Message, int? Line);

internal sealed record AutoHotkeySyntaxResult(bool IsValid, string? Message, int? Line);

internal sealed record AutoHotkeyRunSnapshot(
	Guid RunId,
	Guid? ScriptId,
	string DisplayName,
	AutoHotkeyRunState State,
	DateTimeOffset StartedAt,
	DateTimeOffset? EndedAt,
	int? ExitCode,
	string? Result,
	string Output,
	long OutputVersion,
	AutoHotkeyScriptError? Error)
{
	public TimeSpan? Duration => EndedAt is { } ended ? ended - StartedAt : null;

	public bool IsActive => State is AutoHotkeyRunState.Starting or AutoHotkeyRunState.Running;
}

internal static class AutoHotkeyIdentity
{
	public static string Managed(Guid scriptId)
	{
		return "script:" + scriptId.ToString("N");
	}

	public static string File(string path)
	{
		return "file:" + Path.GetFullPath(path).ToUpperInvariant();
	}

	public static string Inline(UserVariableContext context, string code)
	{
		var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
		return $"inline:{context.OwnerWidgetId}|{context.ScriptId}|{context.AutomationId}|{hash}";
	}
}
