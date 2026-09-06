namespace MacroDeckHost.Application.Configuration;

public static class ResolvedPublicEndpoints
{
	private static PublicEndpointSet? _value;

	public static PublicEndpointSet Value
		=> Volatile.Read(ref _value) ?? PublicEndpointSet.HttpOnly(BuildConfig.PublicPort);

	public static void Set(PublicEndpointSet endpoints)
	{
		ArgumentNullException.ThrowIfNull(endpoints);

		var previous = Interlocked.CompareExchange(ref _value, endpoints, null);
		if (previous is not null && previous != endpoints)
		{
			throw new InvalidOperationException(
				$"The public listeners were already resolved to {Describe(previous)} and cannot be changed to " +
				$"{Describe(endpoints)}.");
		}
	}

	internal static void ResetForTests()
	{
		Volatile.Write(ref _value, null);
	}

	private static string Describe(PublicEndpointSet endpoints)
		=> endpoints.Endpoints.Count == 0
			? "no public listener"
			: string.Join(", ", endpoints.Endpoints.Select(e => $"{(e.Ssl ? "https" : "http")}:{e.Port}"));
}
