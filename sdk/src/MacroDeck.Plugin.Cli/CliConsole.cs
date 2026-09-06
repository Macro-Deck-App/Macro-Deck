namespace MacroDeck.Plugin.Cli;

/// <summary>An ANSI color a <see cref="CliConsole" /> can wrap text in.</summary>
internal enum CliColor
{
	Red = 31,
	Yellow = 33,
	Green = 32
}

/// <summary>
/// Writes a command's human-facing output, honouring <c>--verbosity</c> and <c>--no-color</c> so no
/// command has to check either flag itself.
/// </summary>
internal sealed class CliConsole
{
	private readonly TextWriter _output;
	private readonly TextWriter _error;
	private readonly bool _color;

	public CliConsole(Verbosity verbosity, bool noColor, TextWriter? output = null, TextWriter? error = null)
	{
		Verbosity = verbosity;
		_output = output ?? Console.Out;
		_error = error ?? Console.Error;
		_color = !noColor;
	}

	public Verbosity Verbosity { get; }

	public TextWriter Output => _output;

	public bool NoColor => !_color;

	public void WriteLine(string message = "") => _output.WriteLine(message);

	/// <summary>Writes without a trailing newline - for a wizard prompt that expects the answer on the same
	/// line. Ungated by <c>--verbosity</c>, like <see cref="WriteError" />: a prompt is part of the
	/// interaction itself, never narration that could be silenced.</summary>
	public void Write(string message) => _output.Write(message);

	public void WriteErrorLine(string message) => _error.WriteLine(message);

	/// <summary>Writes the unified <c>error &lt;code&gt;: &lt;message&gt;</c> diagnostic shape. Not gated by
	/// <c>--verbosity</c> - unlike <see cref="Trace" /> and <see cref="Info" />, a diagnostic is the
	/// command's result, not narration of what it is doing, so it is never eligible to be silenced by the
	/// <c>--verbosity</c> row documented in docs/src/content/docs/guides/packaging.md. Written to stderr, like
	/// <paramref name="detail" />, so <c>--output json</c> keeps stdout machine-parseable.</summary>
	public void WriteError(string code, string message, string? detail = null)
	{
		_error.WriteLine(Colorize($"error {code}: {message}", CliColor.Red));

		if (!string.IsNullOrWhiteSpace(detail))
		{
			_error.WriteLine(detail);
		}
	}

	/// <summary>Writes the unified <c>warning &lt;code&gt;: &lt;message&gt;</c> diagnostic shape. Not gated
	/// by <c>--verbosity</c> - see <see cref="WriteError" />. Written to stderr for the same reason.</summary>
	public void WriteWarning(string code, string message) =>
		_error.WriteLine(Colorize($"warning {code}: {message}", CliColor.Yellow));

	/// <summary>Written only at <see cref="Cli.Verbosity.Diagnostic" /> - a command's own narration of what
	/// it is doing, never required to interpret the result.</summary>
	public void Trace(string message)
	{
		if (Verbosity == Verbosity.Diagnostic)
		{
			_output.WriteLine(message);
		}
	}

	/// <summary>Written unless <see cref="Cli.Verbosity.Quiet" />.</summary>
	public void Info(string message)
	{
		if (Verbosity != Verbosity.Quiet)
		{
			_output.WriteLine(message);
		}
	}

	public string Colorize(string text, CliColor color) => _color ? $"[{(int)color}m{text}[0m" : text;
}
