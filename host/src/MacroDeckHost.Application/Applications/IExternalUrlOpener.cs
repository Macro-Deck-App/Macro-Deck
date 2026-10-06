using MacroDeckHost.Domain.Common;

namespace MacroDeckHost.Application.Applications;

public enum ExternalUrlOpenError
{
	InvalidUrl,

	HostLocked,

	NotSupported
}

public interface IExternalUrlOpener
{
	Result<ExternalUrlOpenError> Open(string? url);
}

public static class ExternalUrls
{
	// A link from a calendar or a plugin is untrusted: only an absolute web URL may reach the system
	// browser, never a file, script or custom scheme the host would hand to the shell.
	public static bool TryNormalizeWebUrl(string? candidate, out string url)
	{
		url = string.Empty;

		if (string.IsNullOrWhiteSpace(candidate) ||
			!Uri.TryCreate(candidate.Trim(), UriKind.Absolute, out var uri) ||
			(uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
			string.IsNullOrEmpty(uri.Host))
		{
			return false;
		}

		url = uri.AbsoluteUri;
		return true;
	}
}
