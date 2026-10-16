using System.Text.Json;
using System.Text.RegularExpressions;
using MacroDeckHost.Application.Store.Model;

namespace MacroDeckHost.Infrastructure.Store;

internal static partial class StoreRegistryDependencies
{
	public const int MaxFrameworks = 32;
	public const int MaxPackages = 5000;
	public const string UnknownEcosystem = "unknown";

	public static StoreDependencyList? Parse(RegistryDependenciesDocument? document)
	{
		if (document?.Frameworks is not { ValueKind: JsonValueKind.Array } frameworks
			|| frameworks.GetArrayLength() > MaxFrameworks)
		{
			return null;
		}

		var valid = frameworks.EnumerateArray()
			.Select(ToFramework)
			.OfType<StoreDependencyFramework>()
			.ToList();
		if (valid.Count == 0 && frameworks.GetArrayLength() > 0)
		{
			return null;
		}

		return new StoreDependencyList
		{
			Ecosystem = Ecosystem(document.Ecosystem),
			SdkVersion = Text(document.SdkVersion, SdkVersionPattern()),
			Frameworks = valid
		};
	}

	private static StoreDependencyFramework? ToFramework(JsonElement element)
	{
		if (element.ValueKind != JsonValueKind.Object
			|| Text(Property(element, "framework"), FrameworkPattern()) is not { } framework
			|| Property(element, "packages") is not { ValueKind: JsonValueKind.Array } packages
			|| packages.GetArrayLength() > MaxPackages)
		{
			return null;
		}

		return new StoreDependencyFramework
		{
			Framework = framework,
			Packages = packages.EnumerateArray()
				.Select(ToPackage)
				.OfType<StoreDependencyPackage>()
				.ToList()
		};
	}

	private static StoreDependencyPackage? ToPackage(JsonElement element)
	{
		if (element.ValueKind != JsonValueKind.Object
			|| Text(Property(element, "id"), IdPattern()) is not { } id
			|| Text(Property(element, "version"), VersionPattern()) is not { } version
			|| Property(element, "direct") is not { ValueKind: JsonValueKind.True or JsonValueKind.False } direct)
		{
			return null;
		}

		var requested = Property(element, "requestedVersion");
		var requestedVersion = Text(requested, RequestedVersionPattern());
		if (requested is { ValueKind: not JsonValueKind.Null } && requestedVersion is null)
		{
			return null;
		}

		return new StoreDependencyPackage
		{
			Id = id,
			Version = version,
			RequestedVersion = requestedVersion,
			Direct = direct.GetBoolean()
		};
	}

	// A declared but unreadable ecosystem must not fall back to null, which clients read as NuGet.
	private static string? Ecosystem(JsonElement? ecosystem)
	{
		return ecosystem is null or { ValueKind: JsonValueKind.Null }
			? null
			: Text(ecosystem, EcosystemPattern()) ?? UnknownEcosystem;
	}

	private static JsonElement? Property(JsonElement element, string name)
	{
		return element.TryGetProperty(name, out var value) ? value : null;
	}

	private static string? Text(JsonElement? value, Regex pattern)
	{
		return value is { ValueKind: JsonValueKind.String } element
			&& element.GetString() is { } text
			&& pattern.IsMatch(text)
				? text
				: null;
	}

	[GeneratedRegex(@"\A[a-z0-9][a-z0-9-]{0,31}\z")]
	private static partial Regex EcosystemPattern();

	[GeneratedRegex(@"\A[0-9A-Za-z.+-]{1,64}\z")]
	private static partial Regex SdkVersionPattern();

	[GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9.+-]{0,63}\z")]
	private static partial Regex FrameworkPattern();

	[GeneratedRegex(@"\A[A-Za-z0-9_][A-Za-z0-9._-]{0,99}\z")]
	private static partial Regex IdPattern();

	[GeneratedRegex(@"\A[0-9][0-9A-Za-z.+-]{0,63}\z")]
	private static partial Regex VersionPattern();

	[GeneratedRegex(@"\A[0-9A-Za-z.+*,()\[\] -]{1,128}\z")]
	private static partial Regex RequestedVersionPattern();
}
