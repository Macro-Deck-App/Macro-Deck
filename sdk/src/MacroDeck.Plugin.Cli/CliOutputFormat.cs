namespace MacroDeck.Plugin.Cli;

/// <summary>How <c>validate</c> and <c>inspect</c> render their result. Named to avoid colliding with
/// <c>Json.Schema.OutputFormat</c>, which this project also references.</summary>
internal enum CliOutputFormat
{
	Text,
	Json
}
