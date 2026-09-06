using MacroDeckHost.Application.Configuration;

namespace MacroDeckHost;

public static class HostEndpoints
{
	public static int PublicPort => ResolvedPublicEndpoints.Value.PublicPort;

	public static int? PublicHttpsPort => ResolvedPublicEndpoints.Value.HttpsPort;

	public static bool IsPublicPort(int port) => ResolvedPublicEndpoints.Value.IsPublicPort(port);

	public static bool TryGetLoopbackPort(out int port)
	{
		port = ResolvedLoopbackPort.Value ?? 0;
		return port > 0;
	}

	public const string LoopbackPortEnvironmentVariable = "MACRODECK_HOST_PORT";

	public const int DevelopmentLoopbackPort = 5191;

	public static string LoopbackPortFilePath { get; }
		= Path.Combine(Path.GetTempPath(), BuildConfig.LoopbackPortFileName);

	public static int ResolveLoopbackPort()
	{
		return ResolveLoopbackPort(Environment.GetEnvironmentVariable(LoopbackPortEnvironmentVariable));
	}

	public static int ResolveLoopbackPort(string? configured)
	{
		if (int.TryParse(configured, out var port) && port is > 0 and <= ushort.MaxValue)
		{
			return port;
		}

		return BuildConfig.Channel == BuildChannel.Development ? DevelopmentLoopbackPort : 0;
	}
}
