using System.Globalization;
using YamlDotNet.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Specifications;

/// <summary>
/// Loads the checked-in spec documents from the test output. Missing files fail loudly rather than
/// yielding an empty document: a drift test that silently ran against nothing would report green
/// while guarding nothing at all.
/// </summary>
internal static class SpecificationDocuments
{
	private static readonly Lazy<IReadOnlyDictionary<string, object?>> _openApi = new(() => Load("openapi.yaml"));

	private static readonly Lazy<IReadOnlyDictionary<string, object?>> _asyncApi = new(() => Load("asyncapi.yaml"));

	public static IReadOnlyDictionary<string, object?> OpenApi => _openApi.Value;

	public static IReadOnlyDictionary<string, object?> AsyncApi => _asyncApi.Value;

	/// <summary>Reads a nested mapping, e.g. <c>Map(doc, "components", "messages")</c>.</summary>
	public static IReadOnlyDictionary<string, object?> Map(IReadOnlyDictionary<string, object?> document,
		params string[] path)
	{
		var current = document;
		foreach (var key in path)
		{
			Assert.That(current.ContainsKey(key), Is.True, $"Expected key '{key}' in the specification document.");
			current = AsMap(current[key]);
		}

		return current;
	}

	public static IReadOnlyDictionary<string, object?> AsMap(object? node)
	{
		Assert.That(node, Is.InstanceOf<IDictionary<object, object>>());
		return ((IDictionary<object, object>)node!)
			.ToDictionary(entry => (string)entry.Key, entry => (object?)entry.Value, StringComparer.Ordinal);
	}

	public static IReadOnlyList<string> AsStrings(object? node)
	{
		Assert.That(node, Is.InstanceOf<IEnumerable<object>>());
		return [.. ((IEnumerable<object>)node!).Select(value => (string)value)];
	}

	// YamlDotNet's default deserializer types every scalar as string, so a numeric anchor arrives as
	// "262144" and would never compare equal to an int constant.
	public static long AsLong(object? node) => Convert.ToInt64(node, CultureInfo.InvariantCulture);

	private static IReadOnlyDictionary<string, object?> Load(string fileName)
	{
		var path = Path.Combine(AppContext.BaseDirectory, "Specifications", fileName);
		Assert.That(File.Exists(path),
			Is.True,
			$"Specification document '{fileName}' was not copied to the test output. Expected it at {path}.");

		var yaml = new DeserializerBuilder().Build().Deserialize(File.ReadAllText(path));
		return AsMap(yaml);
	}
}
