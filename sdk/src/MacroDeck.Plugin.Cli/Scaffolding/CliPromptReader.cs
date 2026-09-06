using Spectre.Console;

namespace MacroDeck.Plugin.Cli.Scaffolding;

/// <summary>
/// Injectable stdin for <c>new</c>'s wizard, plus the injectable raw-key console its checkbox prompt needs.
/// The real console is touched in exactly one place - <see cref="Console" /> itself - so every other call
/// site, including every test, depends only on this object rather than on how the process happens to be
/// launched.
/// </summary>
internal sealed class CliPromptReader
{
	private readonly Func<string?> _readLine;
	private readonly Func<TextWriter, bool, IAnsiConsole?>? _interactiveConsole;

	public CliPromptReader(bool isInteractive,
		Func<string?> readLine,
		Func<TextWriter, bool, IAnsiConsole?>? interactiveConsole = null)
	{
		IsInteractive = isInteractive;
		_readLine = readLine;
		_interactiveConsole = interactiveConsole;
	}

	/// <summary>Whether the wizard should prompt at all. <c>false</c> in a redirected/non-tty stdin, matching
	/// how every other Unix and .NET CLI convention decides this.</summary>
	public bool IsInteractive { get; }

	public static CliPromptReader Console { get; } =
		new(!System.Console.IsInputRedirected, System.Console.In.ReadLine, SpectreConsole.TryCreate);

	public string? ReadLine() => _readLine();

	/// <summary><c>null</c> when this reader has no raw-key console seam, or when the seam itself decides
	/// the terminal cannot run one - either way the caller falls back to the line-based prompt.</summary>
	public IAnsiConsole? CreateInteractiveConsole(TextWriter output, bool noColor) =>
		_interactiveConsole?.Invoke(output, noColor);
}
