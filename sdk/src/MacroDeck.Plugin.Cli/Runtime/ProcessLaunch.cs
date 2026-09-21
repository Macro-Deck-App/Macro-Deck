namespace MacroDeck.Plugin.Cli.Runtime;

internal sealed record ProcessLaunch(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory);
