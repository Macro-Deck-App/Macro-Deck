using System.Xml;
using System.Xml.Linq;

namespace MacroDeckHost.Infrastructure.Icons.AppIcons;

internal static class PropertyListIconParser
{
	private const string KeyElementName = "key";
	private const string StringElementName = "string";
	private const string IconFileKey = "CFBundleIconFile";
	private const string IconNameKey = "CFBundleIconName";

	public static string? TryReadIconFileName(string xml)
	{
		if (string.IsNullOrWhiteSpace(xml))
		{
			return null;
		}

		var document = TryParse(xml);
		if (document is null)
		{
			return null;
		}

		string? iconFile = null;
		string? iconName = null;
		foreach (var key in document.Descendants())
		{
			if (!string.Equals(key.Name.LocalName, KeyElementName, StringComparison.Ordinal))
			{
				continue;
			}

			var value = TryReadStringValue(key);
			if (value is null)
			{
				continue;
			}

			switch (key.Value.Trim())
			{
				case IconFileKey:
					iconFile ??= value;
					break;
				case IconNameKey:
					iconName ??= value;
					break;
			}
		}

		return iconFile ?? iconName;
	}

	private static string? TryReadStringValue(XElement key)
	{
		if (key.ElementsAfterSelf().FirstOrDefault() is not { } value)
		{
			return null;
		}

		if (!string.Equals(value.Name.LocalName, StringElementName, StringComparison.Ordinal))
		{
			return null;
		}

		var text = value.Value.Trim();
		return text.Length == 0 ? null : text;
	}

	private static XDocument? TryParse(string xml)
	{
		var settings = new XmlReaderSettings
		{
			DtdProcessing = DtdProcessing.Ignore,
			XmlResolver = null,
			CloseInput = true
		};

		try
		{
			using var reader = XmlReader.Create(new StringReader(xml), settings);
			return XDocument.Load(reader);
		}
		catch (XmlException)
		{
			return null;
		}
		catch (ArgumentException)
		{
			return null;
		}
	}
}
