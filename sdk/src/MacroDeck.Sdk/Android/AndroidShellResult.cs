namespace MacroDeck.Sdk.Android;

/// <param name="Truncated">True when Macro Deck cut the output to fit one protocol message.</param>
public sealed record AndroidShellResult(int ExitCode, string StandardOutput, string StandardError, bool Truncated);
