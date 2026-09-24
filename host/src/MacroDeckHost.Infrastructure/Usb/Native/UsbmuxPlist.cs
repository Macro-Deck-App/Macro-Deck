using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace MacroDeckHost.Infrastructure.Usb.Native;

internal static class UsbmuxPlist
{
	private const string Header = """
		<?xml version="1.0" encoding="UTF-8"?>
		<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
		""";

	public static byte[] Write(IReadOnlyDictionary<string, object> values)
	{
		var plist = new XElement("plist", new XAttribute("version", "1.0"), WriteValue(values));
		return Encoding.UTF8.GetBytes(Header + "\n" + plist.ToString(SaveOptions.None) + "\n");
	}

	public static Dictionary<string, object> Read(ReadOnlySpan<byte> payload)
	{
		var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
		using var stream = new MemoryStream(payload.ToArray());
		using var reader = XmlReader.Create(stream, settings);
		var root = XDocument.Load(reader).Root;
		if (root?.Name != "plist" || root.Elements().FirstOrDefault() is not { Name.LocalName: "dict" } dict)
		{
			throw new FormatException("Not a property list with a dictionary at its root.");
		}

		return ReadDict(dict);
	}

	private static XElement WriteValue(object value)
		=> value switch
		{
			string text => new XElement("string", text),
			bool flag => new XElement(flag ? "true" : "false"),
			int or long or uint or ushort => new XElement("integer",
				Convert.ToString(value, CultureInfo.InvariantCulture)),
			byte[] data => new XElement("data", Convert.ToBase64String(data)),
			IReadOnlyDictionary<string, object> dict => new XElement("dict",
				dict.SelectMany(entry => new[] { new XElement("key", entry.Key), WriteValue(entry.Value) })),
			IEnumerable<object> items => new XElement("array", items.Select(WriteValue)),
			_ => throw new ArgumentException($"Unsupported property list value {value.GetType().Name}.", nameof(value))
		};

	private static Dictionary<string, object> ReadDict(XElement dict)
	{
		var values = new Dictionary<string, object>(StringComparer.Ordinal);
		string? key = null;
		foreach (var element in dict.Elements())
		{
			if (element.Name.LocalName == "key")
			{
				key = element.Value;
				continue;
			}

			if (key is null)
			{
				throw new FormatException("A property list value without a key.");
			}

			values[key] = ReadValue(element);
			key = null;
		}

		return values;
	}

	private static object ReadValue(XElement element)
		=> element.Name.LocalName switch
		{
			"dict" => ReadDict(element),
			"array" => element.Elements().Select(ReadValue).ToList(),
			"string" => element.Value,
			"integer" => long.Parse(element.Value, NumberStyles.Integer, CultureInfo.InvariantCulture),
			"true" => true,
			"false" => false,
			"data" => Convert.FromBase64String(string.Concat(element.Value.Where(c => !char.IsWhiteSpace(c)))),
			"real" => double.Parse(element.Value, NumberStyles.Float, CultureInfo.InvariantCulture),
			"date" => DateTimeOffset.Parse(element.Value, CultureInfo.InvariantCulture),
			_ => throw new FormatException($"Unsupported property list element {element.Name.LocalName}.")
		};
}
