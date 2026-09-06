using MacroDeck.Plugin.Analyzers.Tests.UnitTests.Support;
using MacroDeck.Plugin.Protocol.Limits;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests;

/// <summary>
/// <see cref="SdkUsageManifestGenerator" />: issue #418's central rule that old-SDK usage must never be
/// presented as confirmed obsolete-API usage when exact usage is unknown. The generator is the mechanism
/// that makes "confirmed" possible at all - it emits [assembly: MacroDeckSdkUsage] recording exactly
/// which deprecated APIs a plugin assembly references, including an explicit empty list when it
/// references none, so the host never has to fall back to inferring from a version number alone.
/// </summary>
[TestFixture]
public class SdkUsageManifestGeneratorTests
{
	[Test]
	public void Emits_MacroDeckSdkUsage_listing_the_deprecated_APIs_a_compilation_referencing_the_SDK_uses()
	{
		const string source = """
							  using System;
							  using MacroDeck.Sdk.Deprecation;

							  internal static class LegacyHelpers
							  {
							  	[Obsolete("Use NewHelper instead.")]
							  	[MacroDeckDeprecated("3.1.0", "4.0.0", "Use NewHelper instead.")]
							  	public static void OldHelper()
							  	{
							  	}
							  }

							  internal static class Usage
							  {
							  	public static void Call() => LegacyHelpers.OldHelper();
							  }
							  """;

		var generated = GeneratorTestHarness.RunAndGetGeneratedSource(source, "PluginUnderTest", referenceSdk: true);

		Assert.That(generated, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(generated, Does.Contain("MacroDeckSdkUsageAttribute"));
			Assert.That(generated,
				Does.Contain("\"M:LegacyHelpers.OldHelper\""),
				"lists the used deprecated API's documentation comment id");
		});
	}

	/// <summary>
	/// The state the requirement calls out explicitly: an empty-but-present array is what lets the host
	/// report "confirmed clean" rather than "unknown". A generator that emitted nothing here, or that
	/// omitted the attribute for a clean compilation, would collapse that distinction back into the
	/// inference issue #418 exists to remove.
	/// </summary>
	[Test]
	public void Emits_MacroDeckSdkUsage_with_an_empty_array_for_a_compilation_that_uses_no_deprecated_API()
	{
		const string source = """
							  internal static class NothingDeprecatedHere
							  {
							  	public static void DoWork()
							  	{
							  	}
							  }
							  """;

		var generated = GeneratorTestHarness.RunAndGetGeneratedSource(source, "PluginUnderTest", referenceSdk: true);

		Assert.That(generated, Is.Not.Null);

		var arrayStart = generated!.IndexOf("new string[] {", StringComparison.Ordinal);
		Assert.That(arrayStart, Is.GreaterThanOrEqualTo(0), "generated source declares the DeprecatedApis array");

		var contentStart = arrayStart + "new string[] {".Length;
		var arrayEnd = generated.IndexOf('}', contentStart);
		var arrayContent = generated.Substring(contentStart, arrayEnd - contentStart).Trim();

		Assert.That(arrayContent, Is.Empty, "no deprecated API was used, so the array is empty but still present");
	}

	[Test]
	public void Emits_nothing_for_a_compilation_that_does_not_reference_the_SDK()
	{
		const string source = """
							  internal static class PlainLibrary
							  {
							  	public static void DoWork()
							  	{
							  	}
							  }
							  """;

		var generated = GeneratorTestHarness.RunAndGetGeneratedSource(source, "PluginUnderTest", referenceSdk: false);

		Assert.That(generated, Is.Null);
	}

	/// <summary>
	/// SdkUsageManifestGenerator.MaxReportedApis/MaxApiIdLength are duplicated from
	/// ProtocolLimits.MaxReportedDeprecatedApis/MaxDeprecatedApiIdLength because this netstandard2.0
	/// analyzer project cannot reference the net10.0 protocol package. Pinning them equal here is what
	/// keeps the duplication from silently drifting.
	/// </summary>
	[Test]
	public void Generator_caps_are_pinned_to_the_protocol_limits_they_mirror()
	{
		Assert.Multiple(() =>
		{
			Assert.That(SdkUsageManifestGenerator.MaxReportedApis,
				Is.EqualTo(ProtocolLimits.MaxReportedDeprecatedApis));
			Assert.That(SdkUsageManifestGenerator.MaxApiIdLength,
				Is.EqualTo(ProtocolLimits.MaxDeprecatedApiIdLength));
		});
	}
}
