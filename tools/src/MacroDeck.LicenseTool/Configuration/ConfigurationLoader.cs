using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MacroDeck.LicenseTool.Configuration;

internal sealed record LoadedConfiguration(
	ToolConfiguration Tool,
	OverridesFile Overrides,
	AttributionsFile Attributions);

internal static class ConfigurationLoader
{
	public const string DirectoryName = "third-party";

	private static readonly IDeserializer Deserializer = new DeserializerBuilder()
		.WithNamingConvention(CamelCaseNamingConvention.Instance)
		.Build();

	public static LoadedConfiguration Load(string repositoryRoot)
	{
		var directory = Path.Combine(repositoryRoot, DirectoryName);
		return new LoadedConfiguration(
			Read<ToolConfiguration>(Path.Combine(directory, "config.yml")),
			Read<OverridesFile>(Path.Combine(directory, "overrides.yml")),
			Read<AttributionsFile>(Path.Combine(directory, "attributions.yml")));
	}

	private static T Read<T>(string path) where T : new()
	{
		if (!File.Exists(path))
		{
			throw new ToolException($"missing configuration file {path}");
		}

		try
		{
			return Deserializer.Deserialize<T?>(File.ReadAllText(path)) ?? new T();
		}
		catch (YamlException exception)
		{
			throw new ToolException($"{path}: {exception.Message}");
		}
	}
}
