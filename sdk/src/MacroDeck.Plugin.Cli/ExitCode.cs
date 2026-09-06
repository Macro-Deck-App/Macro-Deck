namespace MacroDeck.Plugin.Cli;

/// <summary>
/// The exit codes every <c>macrodeck-plugin</c> command uses. Distinguishing <see cref="InputUnreadable" />
/// from <see cref="SubjectInvalid" /> is what makes the tool usable in CI: a missing file or a corrupt
/// archive is an environment problem worth retrying or fixing differently than a plugin author's actual
/// manifest mistake, so the two are never collapsed into one "failed" code.
/// </summary>
internal static class ExitCode
{
	/// <summary>The subject is valid, or the conformance run was conformant.</summary>
	public const int Success = 0;

	/// <summary>The subject is wrong: validation failed, or a required conformance check failed.</summary>
	public const int SubjectInvalid = 1;

	/// <summary>Bad arguments, or an unknown check id.</summary>
	public const int UsageError = 2;

	/// <summary>The input could not be read at all: a missing file, something that is not a ZIP, or a
	/// permissions failure.</summary>
	public const int InputUnreadable = 3;

	/// <summary>The operation was cancelled (Ctrl-C).</summary>
	public const int Cancelled = 4;

	/// <summary>An error the command did not anticipate.</summary>
	public const int InternalError = 70;
}
