namespace MacroDeck.Plugin.Cli.Rendering;

internal sealed class PreviewRenderException : Exception
{
	public PreviewRenderException(string code, string message)
		: base(message) => Code = code;

	public string Code { get; }
}
