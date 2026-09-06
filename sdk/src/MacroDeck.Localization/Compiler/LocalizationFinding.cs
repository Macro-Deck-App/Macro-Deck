namespace MacroDeck.Localization.Compiler;

/// <summary>One validation problem, carrying enough to anchor a Roslyn diagnostic at the offending file
/// and entry.</summary>
internal sealed class LocalizationFinding
{
	public LocalizationFinding(string id, string filePath, string? entryName, string message)
	{
		Id = id;
		FilePath = filePath;
		EntryName = entryName;
		Message = message;
	}

	/// <summary>The <c>MDLOC</c> id from <see cref="LocalizationDiagnosticIds" />.</summary>
	public string Id { get; }

	/// <summary>The resource file the problem is in.</summary>
	public string FilePath { get; }

	/// <summary>The entry the problem is in, when it belongs to one.</summary>
	public string? EntryName { get; }

	/// <summary>The message, already formatted with its arguments.</summary>
	public string Message { get; }
}
