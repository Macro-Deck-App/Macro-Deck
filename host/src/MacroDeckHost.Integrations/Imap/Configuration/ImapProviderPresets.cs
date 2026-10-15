namespace MacroDeckHost.Integrations.Imap.Configuration;

internal sealed record ImapProviderPreset(string Id, string Name, string Host, int Port, ImapSecurity Security);

internal static class ImapProviderPresets
{
	// Ids are stored with each entry, so an id keeps its meaning once shipped.
	public static IReadOnlyList<ImapProviderPreset> All { get; } =
	[
		SslTls("gmail", "Gmail", "imap.gmail.com"),
		SslTls("icloud", "iCloud Mail", "imap.mail.me.com"),
		SslTls("yahoo", "Yahoo Mail", "imap.mail.yahoo.com"),
		SslTls("aol", "AOL Mail", "imap.aol.com"),
		SslTls("fastmail", "Fastmail", "imap.fastmail.com"),
		SslTls("zoho", "Zoho Mail", "imap.zoho.com"),
		SslTls("zoho-eu", "Zoho Mail (EU)", "imap.zoho.eu"),
		SslTls("gmx", "GMX", "imap.gmx.net"),
		SslTls("web-de", "WEB.DE", "imap.web.de"),
		SslTls("t-online", "T-Online", "secureimap.t-online.de"),
		SslTls("ionos", "IONOS", "imap.ionos.de"),
		SslTls("mailbox-org", "mailbox.org", "imap.mailbox.org"),
		SslTls("posteo", "Posteo", "posteo.de")
	];

	public static ImapProviderPreset? Find(string? id)
	{
		return All.FirstOrDefault(preset => string.Equals(preset.Id, id, StringComparison.Ordinal));
	}

	public static ImapProviderPreset? Matching(string host, int port, ImapSecurity security)
	{
		return All.FirstOrDefault(preset =>
			string.Equals(preset.Host, host.Trim(), StringComparison.OrdinalIgnoreCase)
			&& preset.Port == port
			&& preset.Security == security);
	}

	private static ImapProviderPreset SslTls(string id, string name, string host)
	{
		return new ImapProviderPreset(id, name, host, ImapAccountSettings.DefaultPort, ImapSecurity.SslTls);
	}
}
