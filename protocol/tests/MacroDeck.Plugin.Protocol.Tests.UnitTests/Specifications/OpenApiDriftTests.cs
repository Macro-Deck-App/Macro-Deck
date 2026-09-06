using System.Globalization;
using System.Reflection;
using MacroDeck.Plugin.Protocol.Auth;
using MacroDeck.Plugin.Protocol.Compatibility;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Specifications;

/// <summary>
/// Asserts openapi.yaml and the C# contracts agree, in both directions. The document is hand-authored
/// because the endpoints it describes do not exist yet, so nothing but these tests stops it becoming
/// fiction.
/// </summary>
[TestFixture]
public class OpenApiDriftTests
{
	[Test]
	public void Document_is_openapi_3_1()
	{
		Assert.That(SpecificationDocuments.OpenApi["openapi"], Is.EqualTo("3.1.0"));
		Assert.That(SpecificationDocuments.OpenApi.ContainsKey("paths"), Is.True);
	}

	[Test]
	public void Protocol_versions_match_the_contracts()
	{
		var versions = SpecificationDocuments.AsMap(SpecificationDocuments.OpenApi["x-macrodeck-protocol-versions"]);

		Assert.Multiple(() =>
		{
			Assert.That(SpecificationDocuments.AsLong(versions["minimum"]), Is.EqualTo(ProtocolVersions.Minimum));
			Assert.That(SpecificationDocuments.AsLong(versions["current"]), Is.EqualTo(ProtocolVersions.Current));
			Assert.That(SpecificationDocuments.AsStrings(versions["supported"]).Select(long.Parse),
				Is.EquivalentTo(ProtocolVersions.Supported.Select(version => (long)version)));
		});
	}

	[Test]
	public void Every_limit_appears_with_the_same_value()
	{
		var documented = SpecificationDocuments.AsMap(SpecificationDocuments.OpenApi["x-macrodeck-limits"]);
		var declared = ConstantsOf(typeof(ProtocolLimits));

		Assert.That(documented.Keys, Is.EquivalentTo(declared.Keys));
		Assert.Multiple(() =>
		{
			foreach (var (name, value) in declared)
			{
				Assert.That(SpecificationDocuments.AsLong(documented[name]), Is.EqualTo(value), $"limit '{name}'");
			}
		});
	}

	[Test]
	public void Every_timeout_appears_in_milliseconds()
	{
		var documented = SpecificationDocuments.AsMap(SpecificationDocuments.OpenApi["x-macrodeck-timeouts"]);
		var declared = typeof(ProtocolTimeouts)
			.GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(field => field.FieldType == typeof(TimeSpan))
			.ToDictionary(field => CamelCase(field.Name) + "Ms",
				field => (long)((TimeSpan)field.GetValue(null)!).TotalMilliseconds,
				StringComparer.Ordinal);

		Assert.That(documented.Keys, Is.EquivalentTo(declared.Keys));
		Assert.Multiple(() =>
		{
			foreach (var (name, value) in declared)
			{
				Assert.That(SpecificationDocuments.AsLong(documented[name]), Is.EqualTo(value), $"timeout '{name}'");
			}
		});
	}

	[Test]
	public void Capability_kinds_and_error_codes_match_the_contracts()
	{
		Assert.Multiple(() =>
		{
			Assert.That(
				SpecificationDocuments.AsStrings(SpecificationDocuments.OpenApi["x-macrodeck-capability-kinds"]),
				Is.EquivalentTo(CapabilityKinds.All));
			Assert.That(SpecificationDocuments.AsStrings(SpecificationDocuments.OpenApi["x-macrodeck-error-codes"]),
				Is.EquivalentTo(ProtocolErrorCodes.All));
		});
	}

	/// <summary>
	/// The compatibility vocabularies are wire contracts a UI keys off, exactly like the capability kinds
	/// above - and the states additionally carry an order, since a plugin's state is the worst of what was
	/// found about it. EqualTo rather than EquivalentTo for the states, so a reordering that changes what
	/// "worst" means cannot pass unnoticed.
	/// </summary>
	[Test]
	public void Compatibility_vocabularies_match_the_contracts()
	{
		Assert.Multiple(() =>
		{
			Assert.That(
				SpecificationDocuments.AsStrings(SpecificationDocuments.OpenApi["x-macrodeck-compatibility-states"]),
				Is.EqualTo(PluginCompatibilityStates.All));
			Assert.That(
				SpecificationDocuments.AsStrings(
					SpecificationDocuments.OpenApi["x-macrodeck-compatibility-finding-sources"]),
				Is.EquivalentTo(CompatibilityFindingSources.All));
			Assert.That(
				SpecificationDocuments.AsStrings(
					SpecificationDocuments.OpenApi["x-macrodeck-compatibility-severities"]),
				Is.EquivalentTo(CompatibilitySeverities.All));
		});
	}

	[Test]
	public void The_error_code_schema_enumerates_every_code()
	{
		var schema = SpecificationDocuments.Map(SpecificationDocuments.OpenApi,
			"components",
			"schemas",
			"ProtocolErrorCode");

		Assert.That(SpecificationDocuments.AsStrings(schema["enum"]), Is.EquivalentTo(ProtocolErrorCodes.All));
	}

	[Test]
	public void Every_documented_path_is_a_declared_constant_and_the_reverse()
	{
		var documented = SpecificationDocuments.Map(SpecificationDocuments.OpenApi, "paths").Keys;

		Assert.Multiple(() =>
		{
			foreach (var path in documented)
			{
				// Path-parameterised operations extend a declared constant rather than equalling one.
				Assert.That(ProtocolConstants.All.Any(known => path.StartsWith(known, StringComparison.Ordinal)),
					Is.True,
					$"documented path '{path}' matches no ProtocolConstants entry");
			}

			foreach (var known in ProtocolConstants.All)
			{
				// ApiBasePath is a prefix, not an endpoint, so containment rather than an exact key.
				Assert.That(documented.Any(path => path.StartsWith(known, StringComparison.Ordinal)),
					Is.True,
					$"constant '{known}' appears nowhere in the document's paths");
			}
		});
	}

	[Test]
	public void Security_scheme_headers_use_the_declared_names()
	{
		var schemes = SpecificationDocuments.Map(SpecificationDocuments.OpenApi, "components", "securitySchemes");

		var headerNames = schemes.Values
			.Select(SpecificationDocuments.AsMap)
			.Where(scheme => (string?)scheme.GetValueOrDefault("in") == "header")
			.Select(scheme => (string)scheme["name"]!)
			.ToList();

		Assert.That(headerNames, Is.Not.Empty);
		Assert.That(headerNames, Is.SubsetOf(PluginAuthDefaults.All));
	}

	private static Dictionary<string, long> ConstantsOf(Type type)
		=> type.GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(field => field.FieldType == typeof(int) || field.FieldType == typeof(long))
			.ToDictionary(field => CamelCase(field.Name),
				field => Convert.ToInt64(field.GetValue(null), CultureInfo.InvariantCulture),
				StringComparer.Ordinal);

	private static string CamelCase(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
