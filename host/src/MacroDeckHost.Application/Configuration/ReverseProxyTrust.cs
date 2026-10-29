namespace MacroDeckHost.Application.Configuration;

public static class ReverseProxyTrust
{
	private static volatile bool _enabled;

	public static bool Enabled
	{
		get => _enabled;
		set => _enabled = value;
	}
}
