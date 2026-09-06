namespace MacroDeck.Localization.Compiler;

/// <summary>One resource file of a set, already parsed.</summary>
internal sealed class LocalizationResourceFile
{
	public LocalizationResourceFile(string path, string fileName, string? culture, List<ResxEntry> entries)
	{
		Path = path;
		FileName = fileName;
		Culture = culture;
		Entries = entries;
	}

	/// <summary>The file's path, used only to anchor diagnostics.</summary>
	public string Path { get; }

	/// <summary>The bare file name.</summary>
	public string FileName { get; }

	/// <summary>The declared culture, or <c>null</c> for the default-language file.</summary>
	public string? Culture { get; }

	/// <summary>Whether this is the default-language file every other culture is checked against.</summary>
	public bool IsDefault => Culture == null;

	/// <summary>The entries as written, duplicates included - detecting those is MDLOC003's job.</summary>
	public List<ResxEntry> Entries { get; }
}

/// <summary>Every culture of one resource set, in one scope.</summary>
internal sealed class LocalizationResourceSet
{
	public LocalizationResourceSet(string scope, string baseName)
	{
		Scope = scope;
		BaseName = baseName;
		Files = new List<LocalizationResourceFile>();
	}

	/// <summary>The scope these resources belong to.</summary>
	public string Scope { get; }

	/// <summary>The set's name, shared by every culture - <c>Strings</c> for <c>Strings.de.resx</c>.</summary>
	public string BaseName { get; }

	/// <summary>The files making up the set.</summary>
	public List<LocalizationResourceFile> Files { get; }

	/// <summary>The default-language file, or <c>null</c> when the set has none.</summary>
	public LocalizationResourceFile? Default
	{
		get
		{
			foreach (var file in Files)
			{
				if (file.IsDefault)
				{
					return file;
				}
			}

			return null;
		}
	}
}
