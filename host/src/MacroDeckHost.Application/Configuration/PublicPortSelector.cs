namespace MacroDeckHost.Application.Configuration;

public enum PublicPortSource
{
	Environment,

	Preference,

	Default
}

public record PublicPortSelection(int Port, PublicPortSource Source);

public static class PublicPortSelector
{
	public const int MinimumConfigurablePort = 1024;

	public const int MaximumConfigurablePort = ushort.MaxValue;

	public static bool IsConfigurable(int port)
		=> port is >= MinimumConfigurablePort and <= MaximumConfigurablePort;

	public static PublicPortSelection Resolve(string? environmentValue, string? persistedValue, int loopbackPort)
	{
		if (int.TryParse(environmentValue, out var fromEnvironment) &&
			fromEnvironment is > 0 and <= MaximumConfigurablePort)
		{
			return new PublicPortSelection(fromEnvironment, PublicPortSource.Environment);
		}

		if (int.TryParse(persistedValue, out var fromPreference) &&
			IsConfigurable(fromPreference) &&
			fromPreference != loopbackPort)
		{
			return new PublicPortSelection(fromPreference, PublicPortSource.Preference);
		}

		return new PublicPortSelection(BuildConfig.DefaultPublicPort, PublicPortSource.Default);
	}
}
