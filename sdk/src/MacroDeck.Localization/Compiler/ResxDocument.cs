using System.Xml.Linq;

namespace MacroDeck.Localization.Compiler;

/// <summary>One <c>&lt;data&gt;</c> entry of a resource file.</summary>
internal sealed class ResxEntry
{
	public ResxEntry(string name, string value, string? comment)
	{
		Name = name;
		Value = value;
		Comment = comment;
	}

	/// <summary>The key, for example <c>Configuration.Title</c>.</summary>
	public string Name { get; }

	/// <summary>The template, for example <c>Connected as {userName}</c>.</summary>
	public string Value { get; }

	/// <summary>The translator note, which may be prefixed by parameter declarations.</summary>
	public string? Comment { get; }
}

/// <summary>Reads the <c>&lt;data&gt;</c> entries of a <c>.resx</c> file.</summary>
/// <remarks>
/// Hand-parsed with <c>System.Xml.Linq</c> rather than <c>ResXResourceReader</c>, which does not exist on
/// the netstandard2.0 surface a Roslyn component is limited to. Only string resources are of interest, so
/// entries carrying a <c>type</c> or <c>mimetype</c> - a serialized object or an embedded file - are
/// skipped rather than rejected.
/// </remarks>
internal static class ResxDocument
{
	/// <summary>Parses resource entries out of <paramref name="xml" />. Returns an empty list when the
	/// document is not well-formed; a malformed file is the compiler's problem to report, not this
	/// reader's to throw over.</summary>
	public static List<ResxEntry> Parse(string? xml)
	{
		var entries = new List<ResxEntry>();

		if (string.IsNullOrWhiteSpace(xml))
		{
			return entries;
		}

		XDocument document;
		try
		{
			document = XDocument.Parse(xml);
		}
		catch (System.Xml.XmlException)
		{
			return entries;
		}

		if (document.Root == null)
		{
			return entries;
		}

		foreach (var data in document.Root.Elements("data"))
		{
			var name = (string?)data.Attribute("name");
			if (string.IsNullOrEmpty(name))
			{
				continue;
			}

			if (data.Attribute("type") != null || data.Attribute("mimetype") != null)
			{
				continue;
			}

			var value = data.Element("value");
			entries.Add(new ResxEntry(name!, value?.Value ?? string.Empty, data.Element("comment")?.Value));
		}

		return entries;
	}

	/// <summary>Whether a file name is a resource file this compiler owns, and what culture it declares.
	/// <c>Strings.resx</c> is the default language; <c>Strings.de.resx</c> declares <c>de</c>.</summary>
	/// <param name="fileName">The bare file name, without directories.</param>
	/// <param name="baseName">The resource set's name, shared by every culture of the set.</param>
	/// <param name="culture">The declared culture, or <c>null</c> for the default-language file. A
	/// malformed value is returned as declared so the caller can report MDLOC005 against it.</param>
	public static bool TrySplitFileName(string? fileName, out string baseName, out string? culture)
	{
		baseName = string.Empty;
		culture = null;

		if (string.IsNullOrEmpty(fileName) ||
			!fileName!.EndsWith(".resx", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		var stem = fileName.Substring(0, fileName.Length - ".resx".Length);
		var separator = stem.IndexOf('.');

		if (separator < 0)
		{
			baseName = stem;
			return baseName.Length > 0;
		}

		baseName = stem.Substring(0, separator);
		culture = stem.Substring(separator + 1);
		return baseName.Length > 0 && culture.Length > 0;
	}
}
