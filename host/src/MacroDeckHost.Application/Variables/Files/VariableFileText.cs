using System.Globalization;
using System.Text;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Variables.Files;

public static class VariableFileText
{
	public const int MaxBytes = 256 * 1024;

	private static readonly Encoding _utf8WithoutBom = new UTF8Encoding(false);

	public static bool IsValidPath(string? path)
		=> !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && !Directory.Exists(path);

	public static bool TryToValue(VariableType type, int? decimalPlaces, string content, out string value)
	{
		switch (type)
		{
			case VariableType.Text:
				value = VariableValueSerializer.Serialize(type, WithoutTrailingLineBreak(content), decimalPlaces);
				return true;

			case VariableType.Numeric
				when decimal.TryParse(content.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number):
				value = VariableValueSerializer.Serialize(type, number, decimalPlaces);
				return true;

			case VariableType.Boolean when TryParseBoolean(content.Trim(), out var flag):
				value = VariableValueSerializer.Serialize(type, flag, decimalPlaces);
				return true;

			default:
				value = string.Empty;
				return false;
		}
	}

	// In place rather than temp file plus rename: that keeps symlinks, hard links, ownership and
	// permissions of a file another application owns.
	public static async Task WriteAsync(string path, string content, CancellationToken cancellationToken = default)
	{
		await using var stream = new FileStream(path,
			FileMode.Create,
			FileAccess.Write,
			FileShare.Read,
			4096,
			FileOptions.Asynchronous);
		var bytes = _utf8WithoutBom.GetBytes(content);
		await stream.WriteAsync(bytes, cancellationToken);
	}

	private static string WithoutTrailingLineBreak(string content)
	{
		if (content.EndsWith("\r\n", StringComparison.Ordinal))
		{
			return content[..^2];
		}

		return content.EndsWith('\n') ? content[..^1] : content;
	}

	private static bool TryParseBoolean(string text, out bool value)
	{
		switch (text.ToLowerInvariant())
		{
			case "true":
			case "1":
				value = true;
				return true;
			case "false":
			case "0":
				value = false;
				return true;
			default:
				value = false;
				return false;
		}
	}
}
