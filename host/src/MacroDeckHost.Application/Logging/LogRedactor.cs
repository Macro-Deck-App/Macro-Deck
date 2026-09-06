using System.Text.RegularExpressions;

namespace MacroDeckHost.Application.Logging;

public static partial class LogRedactor
{
	public const string Placeholder = "***";

	public static string Redact(string? text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return text ?? string.Empty;
		}

		var result = UserPathRedactor.Current.Redact(text);
		result = SensitivePairRegex().Replace(result, "${key}${separator}${quote}" + Placeholder);
		result = UrlUserInfoRegex().Replace(result, $"${{scheme}}{Placeholder}:{Placeholder}@");
		result = AuthorizationSchemeRegex().Replace(result, $"${{scheme}} {Placeholder}");
		result = JwtRegex().Replace(result, Placeholder);
		result = PrivateKeyPemRegex().Replace(result, Placeholder);
		return result;
	}

	[GeneratedRegex(
		"""(?<![A-Za-z0-9_])(?:(?<key>access_token|refresh_token|id_token|client_secret|private_key|api_key|api_token|apikey|apitoken|pluginSecret|sessionToken|enrollmentToken|ticket|authorization|credentials|credential|signature|password|passwd|token|secret|jwt|auth|pwd|key)(?<separator>["']?\s*[:=]\s*)|(?<key>code)(?<separator>["']?\s*=\s*))(?<quote>["']?)(?:(?:Bearer|Basic|Digest)\s+)?[^\s&"'{}\\]+""",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex SensitivePairRegex();

	[GeneratedRegex("""(?<scheme>[A-Za-z][A-Za-z0-9+.\-]*://)[^/\s:@]+:[^/\s@]*@""",
		RegexOptions.CultureInvariant)]
	private static partial Regex UrlUserInfoRegex();

	[GeneratedRegex("""(?<![A-Za-z0-9])(?<scheme>Bearer|Basic|Digest)\s+[A-Za-z0-9\-._~+/=]{8,}""",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex AuthorizationSchemeRegex();

	[GeneratedRegex("""(?<![A-Za-z0-9_\-])eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]*""",
		RegexOptions.CultureInvariant)]
	private static partial Regex JwtRegex();

	[GeneratedRegex(
		"""-----BEGIN (?:RSA |EC |ENCRYPTED )?PRIVATE KEY-----.*?-----END (?:RSA |EC |ENCRYPTED )?PRIVATE KEY-----""",
		RegexOptions.CultureInvariant | RegexOptions.Singleline)]
	private static partial Regex PrivateKeyPemRegex();
}
