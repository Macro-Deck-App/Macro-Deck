using System.Reflection;

namespace MacroDeck.Sdk.Http;

/// <summary>
/// The <see cref="IHostHttp" /> of a context that cannot ask Macro Deck: <see cref="UserAgent" /> is
/// <c>MacroDeck/</c> followed by the SDK version.
/// </summary>
public sealed class DefaultHostHttp : IHostHttp
{
	public static readonly DefaultHostHttp Instance = new();

	private DefaultHostHttp()
	{
	}

	public string UserAgent { get; } = "MacroDeck/" + SdkVersion();

	private static string SdkVersion()
	{
		var assembly = typeof(DefaultHostHttp).Assembly;
		var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
		if (string.IsNullOrWhiteSpace(informational))
		{
			return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
		}

		var metadata = informational.IndexOf('+', StringComparison.Ordinal);
		return metadata > 0 ? informational[..metadata] : informational;
	}
}
