using System.Net.Sockets;

namespace MacroDeckHost.Integrations.Obs;

internal static class ObsReachabilityProbe
{
	public static async Task<bool> IsReachableAsync(
		string url,
		TimeSpan timeout,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var uri = new Uri(url);
			using var probe = new TcpClient();
			using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			cts.CancelAfter(timeout);
			await probe.ConnectAsync(uri.Host, uri.Port, cts.Token).ConfigureAwait(false);
			return probe.Connected;
		}
		catch (Exception)
		{
			return false;
		}
	}
}
