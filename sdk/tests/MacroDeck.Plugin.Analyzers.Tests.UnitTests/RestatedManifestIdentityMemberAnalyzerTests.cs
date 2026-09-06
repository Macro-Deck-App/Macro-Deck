using MacroDeck.Plugin.Analyzers.Rules;
using MacroDeck.Plugin.Analyzers.Tests.UnitTests.Support;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests;

[TestFixture]
public class RestatedManifestIdentityMemberAnalyzerTests
{
	// The members every IPluginIntegration snippet below needs to actually implement the interface -
	// kept out of each test's own source so each test adds exactly the one member (or interface) under
	// test, and nothing else.
	private const string RequiredMembers = """
										   	public System.Collections.Generic.IReadOnlyList<MacroDeck.Sdk.Actions.IActionDefinition> Actions { get; } = [];
										   	public System.Threading.Tasks.Task InitializeAsync(MacroDeck.Sdk.IIntegrationContext context) => System.Threading.Tasks.Task.CompletedTask;
										   	public System.Threading.Tasks.Task ShutdownAsync() => System.Threading.Tasks.Task.CompletedTask;
										   """;

	[TestCase("public string Id => \"x\";")]
	// Both shapes: Name became LocalizedText when action metadata was localized, and the rule matches on
	// the declared type so it does not fire on an unrelated member that merely shares the name.
	[TestCase("public LocalizedText Name => \"x\";")]
	[TestCase("public string Name => \"x\";")]
	[TestCase("public string Version => \"x\";")]
	[TestCase("public bool IsInitialized => false;")]
	public async Task Fires_on_a_restated_identity_member(string member)
	{
		var source =
			$$"""
			  using MacroDeck.Localization;
			  using MacroDeck.Sdk;

			  internal sealed class MyIntegration : IPluginIntegration
			  {
			  	{{member}}
			  {{RequiredMembers}}
			  }
			  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source,
			new RestatedManifestIdentityMemberAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP1004"));
	}

	[Test]
	public async Task Fires_when_the_type_implements_IIntegrationIconProvider()
	{
		var source =
			$$"""
			  using MacroDeck.Localization;
			  using MacroDeck.Sdk;

			  internal sealed class MyIntegration : IPluginIntegration, IIntegrationIconProvider
			  {
			  {{RequiredMembers}}
			  	public string IconMimeType => "image/png";
			  	public byte[] GetIcon() => [];
			  }
			  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source,
			new RestatedManifestIdentityMemberAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP1004"));
		Assert.That(AnalyzerTestHarness.GetSquiggleText(diagnostics[0], source),
			Is.EqualTo("IIntegrationIconProvider"));
	}

	[Test]
	public async Task Does_not_fire_when_a_same_named_property_has_a_different_type()
	{
		// A plugin type legitimately owning a non-string Name or a non-string/non-bool Version is not
		// restating manifest data - the type is the false-positive fence, and this proves it holds even
		// when only the declared type, not the name, differs from what would restate identity.
		var source =
			$$"""
			  using MacroDeck.Localization;
			  using MacroDeck.Sdk;

			  internal sealed class Device
			  {
			  	public string Label { get; } = "";
			  }

			  internal sealed class MyIntegration : IPluginIntegration
			  {
			  	public Device Name { get; } = new();
			  	public int Version { get; } = 1;
			  {{RequiredMembers}}
			  }
			  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source,
			new RestatedManifestIdentityMemberAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}

	[Test]
	public async Task Does_not_fire_on_a_type_that_does_not_implement_IPluginIntegration()
	{
		// The exact shape a legitimate in-process IIntegration implementation takes - all four members,
		// plus IIntegrationIconProvider - which must never be flagged, since MDP1004 is specific to
		// IPluginIntegration and the in-process contract is unchanged.
		const string source = """
							  using System.Collections.Generic;
							  using System.Threading.Tasks;
							  using MacroDeck.Localization;
							  using MacroDeck.Sdk;
							  using MacroDeck.Sdk.Actions;

							  internal sealed class MyIntegration : IIntegration, IIntegrationIconProvider
							  {
							  	public string Id => "app.macro-deck.my-plugin.my-integration";
							  	public LocalizedText Name => "My Integration";
							  	public string Version => "1.0.0";
							  	public bool IsInitialized => false;
							  	public IReadOnlyList<IActionDefinition> Actions { get; } = [];
							  	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;
							  	public Task ShutdownAsync() => Task.CompletedTask;
							  	public string IconMimeType => "image/png";
							  	public byte[] GetIcon() => [];
							  }
							  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source,
			new RestatedManifestIdentityMemberAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}

	[Test]
	public async Task Does_not_fire_on_a_private_field_named_underscore_name()
	{
		var source =
			$$"""
			  using MacroDeck.Localization;
			  using MacroDeck.Sdk;

			  internal sealed class MyIntegration : IPluginIntegration
			  {
			  	private readonly string _name = "x";
			  {{RequiredMembers}}
			  }
			  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source,
			new RestatedManifestIdentityMemberAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}

	/// <summary>
	/// A partial type has one symbol and several declarations, and the merged member list is visible from
	/// every one of them - so the naive shape of this rule reported the same property once per part.
	/// </summary>
	[Test]
	public async Task Fires_once_for_a_member_of_a_partial_type()
	{
		var source =
			$$"""
			  using MacroDeck.Localization;
			  using MacroDeck.Sdk;

			  internal sealed partial class MyIntegration : IPluginIntegration
			  {
			  	public LocalizedText Name => "My Integration";
			  {{RequiredMembers}}
			  }

			  internal sealed partial class MyIntegration
			  {
			  	public int Unrelated => 1;
			  }
			  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source,
			new RestatedManifestIdentityMemberAnalyzer());

		Assert.That(diagnostics, Has.Length.EqualTo(1));
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP1004"));
	}

	/// <summary>The same, for the type-level icon-provider arm, which reports on the base list.</summary>
	[Test]
	public async Task Fires_once_for_an_icon_provider_on_a_partial_type()
	{
		var source =
			$$"""
			  using MacroDeck.Localization;
			  using MacroDeck.Sdk;

			  internal sealed partial class MyIntegration : IPluginIntegration, IIntegrationIconProvider
			  {
			  	public string IconMimeType => "image/png";
			  	public byte[] GetIcon() => [];
			  {{RequiredMembers}}
			  }

			  internal sealed partial class MyIntegration
			  {
			  	public int Unrelated => 1;
			  }
			  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source,
			new RestatedManifestIdentityMemberAnalyzer());

		Assert.That(diagnostics, Has.Length.EqualTo(1));
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP1004"));
	}

	/// <summary>
	/// The deliberate cost of the rule, pinned so it is a decision rather than a surprise: a plugin type
	/// with a public <c>string Name</c> of its own is flagged, because that is indistinguishable from the
	/// shape that used to satisfy <c>IIntegration</c> - name and declared type are all the rule can see.
	/// An author who genuinely wants one renames it or suppresses MDP1004 for that member.
	/// </summary>
	[Test]
	public async Task Fires_on_a_public_string_name_a_plugin_declares_for_its_own_use()
	{
		var source =
			$$"""
			  using MacroDeck.Localization;
			  using MacroDeck.Sdk;

			  internal sealed class MyIntegration : IPluginIntegration
			  {
			  	public LocalizedText Name => "whatever this plugin means by it";
			  {{RequiredMembers}}
			  }
			  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source,
			new RestatedManifestIdentityMemberAnalyzer());

		Assert.That(diagnostics, Has.Length.EqualTo(1));
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP1004"));
	}
}
