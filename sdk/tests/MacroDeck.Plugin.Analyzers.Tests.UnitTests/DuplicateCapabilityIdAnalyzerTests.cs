using MacroDeck.Plugin.Analyzers.Rules;
using MacroDeck.Plugin.Analyzers.Tests.UnitTests.Support;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests;

/// <summary>
/// MDP2001 fires on two independent shapes - two <c>IActionDefinition.Id</c> implementations, and two
/// <c>DeclaredCapability</c> object-creations - each exercising a different code path, so both get their
/// own positive/near-miss pair.
/// </summary>
[TestFixture]
public class DuplicateCapabilityIdAnalyzerTests
{
	[Test]
	public async Task Fires_when_two_action_definitions_share_a_constant_id()
	{
		const string source = """
							  using MacroDeck.Localization;
							  using MacroDeck.Sdk.Actions;
							  using System.Collections.Generic;

							  internal sealed class FirstAction : IActionDefinition
							  {
							  	public string Id => "play";
							  	public LocalizedText Name => "Play";
							  	public LocalizedText Description => "Plays";
							  	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
							  	public IActionExecutor CreateExecutor() => throw new System.NotImplementedException();
							  }

							  internal sealed class SecondAction : IActionDefinition
							  {
							  	public string Id => "play";
							  	public LocalizedText Name => "Also play";
							  	public LocalizedText Description => "Also plays";
							  	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
							  	public IActionExecutor CreateExecutor() => throw new System.NotImplementedException();
							  }
							  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, new DuplicateCapabilityIdAnalyzer());

		Assert.That(diagnostics, Has.Length.EqualTo(2));
		Assert.That(diagnostics, Has.All.Property("Id").EqualTo("MDP2001"));
	}

	[Test]
	public async Task Does_not_fire_when_two_action_definitions_have_different_ids()
	{
		const string source = """
							  using MacroDeck.Localization;
							  using MacroDeck.Sdk.Actions;
							  using System.Collections.Generic;

							  internal sealed class FirstAction : IActionDefinition
							  {
							  	public string Id => "play";
							  	public LocalizedText Name => "Play";
							  	public LocalizedText Description => "Plays";
							  	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
							  	public IActionExecutor CreateExecutor() => throw new System.NotImplementedException();
							  }

							  internal sealed class SecondAction : IActionDefinition
							  {
							  	public string Id => "pause";
							  	public LocalizedText Name => "Pause";
							  	public LocalizedText Description => "Pauses";
							  	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
							  	public IActionExecutor CreateExecutor() => throw new System.NotImplementedException();
							  }
							  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, new DuplicateCapabilityIdAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}

	[Test]
	public async Task Fires_when_two_DeclaredCapability_creations_share_kind_and_local_id()
	{
		const string source = """
							  using MacroDeck.Plugin.Protocol.Handshake;
							  using MacroDeck.Plugin.Protocol.Versioning;

							  internal static class Factory
							  {
							  	private static readonly CapabilityVersionRange _version = new() { Minimum = 1, Maximum = 1 };

							  	public static DeclaredCapability First() => new DeclaredCapability
							  	{
							  		Kind = CapabilityKinds.Events,
							  		LocalId = "changed",
							  		VersionRange = _version
							  	};

							  	public static DeclaredCapability Second() => new DeclaredCapability
							  	{
							  		Kind = CapabilityKinds.Events,
							  		LocalId = "changed",
							  		VersionRange = _version
							  	};
							  }
							  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, new DuplicateCapabilityIdAnalyzer());

		Assert.That(diagnostics, Has.Length.EqualTo(2));
		Assert.That(diagnostics, Has.All.Property("Id").EqualTo("MDP2001"));
	}

	/// <summary>
	/// The same local id under two different kinds is a legal collision - the host qualifies a capability
	/// by (kind, local id), so this must never be flagged.
	/// </summary>
	[Test]
	public async Task Does_not_fire_when_two_DeclaredCapability_creations_share_a_local_id_but_not_the_kind()
	{
		const string source = """
							  using MacroDeck.Plugin.Protocol.Handshake;
							  using MacroDeck.Plugin.Protocol.Versioning;

							  internal static class Factory
							  {
							  	private static readonly CapabilityVersionRange _version = new() { Minimum = 1, Maximum = 1 };

							  	public static DeclaredCapability First() => new DeclaredCapability
							  	{
							  		Kind = CapabilityKinds.Events,
							  		LocalId = "shared",
							  		VersionRange = _version
							  	};

							  	public static DeclaredCapability Second() => new DeclaredCapability
							  	{
							  		Kind = CapabilityKinds.Variables,
							  		LocalId = "shared",
							  		VersionRange = _version
							  	};
							  }
							  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, new DuplicateCapabilityIdAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}
}
