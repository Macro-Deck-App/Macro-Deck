using System.Globalization;
using System.Text.Json;
using MacroDeckHost.Integrations.Imap.Models;

namespace MacroDeckHost.Integrations.Imap.Configuration;

internal enum ImapSecurity
{
	SslTls,
	StartTls
}

internal sealed record ImapAccountSettings(
	string Host,
	int Port,
	ImapSecurity Security,
	string Username,
	string Password,
	string Folder)
{
	public const int DefaultPort = 993;
	public const string DefaultFolder = "INBOX";

	public string InboxId => ImapInboxId.For(Host, Port, Username, Folder);

	public string? Address => Username.Contains('@', StringComparison.Ordinal) ? Username : null;

	// The generated record text would include the password, and settings end up in log messages.
	public override string ToString()
	{
		return $"{Username} at {Host}:{Port}/{Folder}";
	}

	public static ImapSecurity ParseSecurity(string? value)
	{
		return value == ImapConfigKeys.SecurityStartTls ? ImapSecurity.StartTls : ImapSecurity.SslTls;
	}

	public static string FormatSecurity(ImapSecurity security)
	{
		return security == ImapSecurity.StartTls ? ImapConfigKeys.SecurityStartTls : ImapConfigKeys.SecuritySslTls;
	}

	public static int? ParsePort(object? value)
	{
		return value switch
		{
			null => DefaultPort,
			int number => number,
			long number => (int)number,
			double number when number == Math.Floor(number) => (int)number,
			string text when string.IsNullOrWhiteSpace(text) => DefaultPort,
			string text when int.TryParse(text.Trim(),
					NumberStyles.Integer,
					CultureInfo.InvariantCulture,
					out var parsed)
				=> parsed,
			JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt32(out var parsed) => parsed,
			JsonElement { ValueKind: JsonValueKind.String } element => ParsePort(element.GetString()),
			_ => null
		};
	}

	public static string NormalizeFolder(string? folder)
	{
		return string.IsNullOrWhiteSpace(folder) ? DefaultFolder : folder.Trim();
	}
}
