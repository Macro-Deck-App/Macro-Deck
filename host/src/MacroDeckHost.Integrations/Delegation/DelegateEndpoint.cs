using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MacroDeckHost.Integrations.Delegation;

internal static class DelegateEndpoint
{
	public const string ExampleUrl = "http://192.168.1.50:8193";

	public static Uri? TryBuild(string? baseUrl)
	{
		var value = (baseUrl ?? string.Empty).Trim();
		var cut = value.IndexOfAny(['?', '#']);
		if (cut >= 0)
		{
			value = value[..cut].Trim();
		}

		if (value.Length == 0)
		{
			return null;
		}

		if (!value.Contains("://", StringComparison.Ordinal))
		{
			value = "http://" + value;
		}

		if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) || string.IsNullOrEmpty(parsed.Host))
		{
			return null;
		}

		if (parsed.Scheme is not ("http" or "https"))
		{
			return null;
		}

		var builder = new UriBuilder(parsed.Scheme, parsed.Host, parsed.IsDefaultPort ? -1 : parsed.Port);
		return builder.Uri;
	}

	public static bool IsThisMachine(Uri baseUrl, string? remoteMachineName)
		=> !string.IsNullOrEmpty(remoteMachineName) &&
			string.Equals(remoteMachineName, Environment.MachineName, StringComparison.OrdinalIgnoreCase) &&
			IsLocalAddress(baseUrl);

	private static bool IsLocalAddress(Uri uri)
	{
		if (uri.IsLoopback)
		{
			return true;
		}

		if (IPAddress.TryParse(uri.Host, out var address))
		{
			return OwnsAddress(address);
		}

		// A hostname rather than a literal address: resolve it best-effort. A DNS failure here just
		// means the check cannot confirm "this machine" - it must not be read as confirming the
		// opposite, so it falls through to false rather than throwing.
		try
		{
			return Dns.GetHostAddresses(uri.Host).Any(OwnsAddress);
		}
		catch (Exception ex) when (ex is SocketException or ArgumentException)
		{
			return false;
		}
	}

	private static bool OwnsAddress(IPAddress address)
	{
		try
		{
			return NetworkInterface.GetAllNetworkInterfaces()
				.SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
				.Any(unicast => unicast.Address.Equals(address));
		}
		catch (NetworkInformationException)
		{
			return false;
		}
	}
}
