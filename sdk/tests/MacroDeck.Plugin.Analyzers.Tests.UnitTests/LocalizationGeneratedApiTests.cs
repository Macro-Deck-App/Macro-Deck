using static MacroDeck.Plugin.Analyzers.Tests.UnitTests.Support.LocalizationGeneratorTestHarness;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests;

/// <summary>
/// The "a source generator generates strongly typed members and formatted resources generate
/// compile-time checked parameters" acceptance criteria.
///
/// <para>
/// Every case here compiles the generated source for real against MacroDeck.Localization, because the
/// requirement is not that an API exists but that the compiler checks it: a <c>Strings.Get(string key)</c>
/// façade, or a <c>params object?[]</c> signature, satisfies every positive assertion and none of the
/// negative ones.
/// </para>
/// </summary>
[TestFixture]
public class LocalizationGeneratedApiTests
{
	private const string Resources = """
									   <data name="Connect" xml:space="preserve"><value>Connect</value></data>
									   <data name="ConnectedAs" xml:space="preserve"><value>Connected as {userName}</value></data>
									   <data name="DeviceCount" xml:space="preserve"><value>{count} devices found</value><comment>[count:int]</comment></data>
									   <data name="Configuration.Title" xml:space="preserve"><value>Spotify</value></data>
									 """;

	private static string Generate()
	{
		var run = Run(new Dictionary<string, string> { [DefaultFileName] = Resx(Resources) });

		Assert.That(run.Ids, Is.Empty, "the fixture resources are meant to be valid");
		Assert.That(run.GeneratedSource, Is.Not.Null);

		return run.GeneratedSource!;
	}

	private static string[] Errors(string body)
		=> CompileErrors(Generate(),
			$$"""
			  namespace TestPlugin
			  {
			  	internal static class CallSite
			  	{
			  		public static void Use()
			  		{
			  			{{body}}
			  		}
			  	}
			  }
			  """);

	[Test]
	public void A_key_with_no_placeholders_generates_a_parameterless_member()
		=> Assert.That(Errors("var value = Strings.Connect();"), Is.Empty);

	[Test]
	public void A_dotted_key_generates_a_nested_class()
		=> Assert.That(Errors("var value = Strings.Configuration.Title();"), Is.Empty);

	[Test]
	public void A_placeholder_generates_a_parameter_named_after_it()
		=> Assert.That(Errors("var value = Strings.ConnectedAs(userName: \"ada\");"), Is.Empty);

	[Test]
	public void A_declared_int_parameter_generates_an_int_parameter()
		=> Assert.That(Errors("var value = Strings.DeviceCount(3);"), Is.Empty);

	// The four negatives below are the whole point: each one compiles cleanly against a stringly typed
	// or params-array API, and only a genuinely typed signature rejects it.

	[Test]
	public void A_missing_argument_is_a_compile_error()
		=> Assert.That(Errors("var value = Strings.ConnectedAs();"), Does.Contain("CS7036"));

	[Test]
	public void A_surplus_argument_is_a_compile_error()
		=> Assert.That(Errors("var value = Strings.ConnectedAs(\"ada\", \"extra\");"), Does.Contain("CS1501"));

	[Test]
	public void Passing_a_string_where_the_resource_declared_an_int_is_a_compile_error()
		=> Assert.That(Errors("var value = Strings.DeviceCount(\"3\");"), Does.Contain("CS1503"));

	[Test]
	public void A_key_that_does_not_exist_is_a_compile_error()
		=> Assert.That(Errors("var value = Strings.Conect();"), Does.Contain("CS0117"));

	[Test]
	public void The_generated_member_returns_a_deferred_reference_rather_than_resolved_text()
	{
		// If the generated member returned string, this assignment would compile. That it does not is
		// what keeps a language change from having to rebuild every producer's UI.
		var errors = Errors("string value = Strings.Connect();");

		Assert.That(errors, Does.Contain("CS0029"));
	}

	[Test]
	public void The_scope_comes_from_the_plugin_id_when_no_scope_property_is_set()
	{
		var run = Run(new Dictionary<string, string> { [DefaultFileName] = Resx(Resources) },
			scope: null,
			manifestJson: """{"manifestVersion":1,"id":"com.example.spotify","name":"Spotify","version":"1.0.0"}""");

		Assert.Multiple(() =>
		{
			Assert.That(run.Ids, Is.Empty);
			Assert.That(run.GeneratedSource,
				Does.Contain("LocalizationScope = \"plugin:com.example.spotify\""));
		});
	}

	[Test]
	public void Nothing_is_generated_when_neither_a_scope_property_nor_a_manifest_names_an_owner()
	{
		var run = Run(new Dictionary<string, string> { [DefaultFileName] = Resx(Resources) }, scope: null);

		Assert.Multiple(() =>
		{
			Assert.That(run.GeneratedSource, Is.Null);
			Assert.That(run.Ids, Is.Empty, "MDP1001 already reports a manifest with no identity");
		});
	}
}
