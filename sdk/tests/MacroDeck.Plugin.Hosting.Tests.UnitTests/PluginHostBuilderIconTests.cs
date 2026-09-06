using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Icons;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

/// <summary>
/// The plugin's icon is the file <c>manifest.json</c> names, loaded by the SDK at <c>Build</c> (#560).
/// What these pin down is the *timing* as much as the content: an icon a plugin cannot serve has to be a
/// build problem, alongside every other one, rather than an <c>AssetUploadException</c> minutes later in
/// a running plugin's log. <see cref="PluginHostBuilderManifestTests" /> already covers the manifest's
/// path handling (missing, blank, escaping the content root); these cover the file itself.
/// </summary>
[TestFixture]
public class PluginHostBuilderIconTests
{
	private const string ManifestWithIcon = """
											{
											  "manifestVersion": 1,
											  "id": "com.example.test",
											  "name": "Test",
											  "version": "1.0.0",
											  "icon": "assets/icon{0}"
											}
											""";

	private PluginManifestFixture? _fixture;

	[TearDown]
	public void TearDown() => _fixture?.Dispose();

	[Test]
	public void An_icon_of_an_unsupported_type_is_a_build_problem()
	{
		var builder = CreateBuilder(".gif");
		_fixture!.WriteFile("assets/icon.gif", [1, 2, 3]);

		var exception = Assert.Throws<PluginConfigurationException>(() => builder.Build());

		Assert.That(exception!.Problems, Has.One.Contains("assets/icon.gif"));
	}

	/// <summary>
	/// The counterexample to <see cref="An_icon_of_an_unsupported_type_is_a_build_problem" />: a validator
	/// that rejected everything would pass that test on its own. Both <c>.jpg</c> and <c>.jpeg</c> are
	/// listed because they are the one pair that shares an answer, and the one an incomplete table drops.
	/// </summary>
	[TestCase(".svg", "image/svg+xml")]
	[TestCase(".png", "image/png")]
	[TestCase(".jpg", "image/jpeg")]
	[TestCase(".jpeg", "image/jpeg")]
	[TestCase(".webp", "image/webp")]
	[TestCase(".PNG", "image/png")]
	public void A_supported_icon_builds_and_describes_the_media_type_its_extension_implies(
		string extension,
		string expectedMimeType)
	{
		var builder = CreateBuilder(extension);
		_fixture!.WriteFile($"assets/icon{extension}", [1, 2, 3, 4]);

		using var plugin = builder.Build();

		var describe = Describe(plugin);
		Assert.Multiple(() =>
		{
			Assert.That(describe.MimeType, Is.EqualTo(expectedMimeType));
			Assert.That(describe.ByteLength, Is.EqualTo(4));
			Assert.That(describe.ContentHash, Is.Not.Empty);
		});
	}

	[Test]
	public void An_empty_icon_file_is_a_build_problem()
	{
		var builder = CreateBuilder(".png");
		_fixture!.WriteFile("assets/icon.png", []);

		var exception = Assert.Throws<PluginConfigurationException>(() => builder.Build());

		Assert.That(exception!.Problems, Has.One.Contains("assets/icon.png"));
	}

	/// <summary>
	/// One byte over what <c>PluginAssetUploader</c> would have refused at connect. The point of the test
	/// is that the refusal moved to <c>Build</c>, not that a limit exists.
	/// </summary>
	[Test]
	public void An_icon_over_the_asset_limit_is_a_build_problem()
	{
		var builder = CreateBuilder(".png");
		_fixture!.WriteFile("assets/icon.png", new byte[ProtocolLimits.MaxAssetBytes + 1]);

		var exception = Assert.Throws<PluginConfigurationException>(() => builder.Build());

		Assert.That(exception!.Problems, Has.One.Contains("assets/icon.png"));
	}

	/// <summary>
	/// The other side of the boundary, against an off-by-one or a limit applied in the wrong unit: an icon
	/// exactly at the maximum is uploadable, so it must build.
	/// </summary>
	[Test]
	public void An_icon_exactly_at_the_asset_limit_builds()
	{
		var builder = CreateBuilder(".png");
		_fixture!.WriteFile("assets/icon.png", new byte[ProtocolLimits.MaxAssetBytes]);

		using var plugin = builder.Build();

		Assert.That(Describe(plugin).ByteLength, Is.EqualTo(ProtocolLimits.MaxAssetBytes));
	}

	/// <summary>
	/// <c>Build</c> promises every problem at once. An icon check that threw instead of appending would
	/// hide the rest and cost the author a second run to find them.
	/// </summary>
	[Test]
	public void An_icon_problem_is_reported_alongside_the_other_problems()
	{
		_fixture = new PluginManifestFixture("""
											 {
											   "manifestVersion": 1,
											   "id": "com.example.test",
											   "icon": "assets/icon.gif"
											 }
											 """);
		_fixture.WriteFile("assets/icon.gif", [1, 2, 3]);

		var exception = Assert.Throws<PluginConfigurationException>(() => _fixture.CreateBuilder().Build());

		Assert.Multiple(() =>
		{
			Assert.That(exception!.Problems, Has.One.Contains("assets/icon.gif"));
			Assert.That(exception.Problems, Has.One.Contains("name"));
			Assert.That(exception.Problems, Has.One.Contains("version"));
		});
	}

	/// <summary>
	/// The "nothing changed" half of #560: a plugin whose manifest names no icon must look exactly like
	/// one whose integrations declared no icon did before - no capability, and no handler registered to
	/// answer for one.
	/// </summary>
	[Test]
	public void A_manifest_without_an_icon_declares_no_icons_capability_and_registers_no_handler()
	{
		_fixture = new PluginManifestFixture("""
											 {
											   "manifestVersion": 1,
											   "id": "com.example.test",
											   "name": "Test",
											   "version": "1.0.0"
											 }
											 """);

		using var plugin = _fixture.CreateBuilder().Build();

		var handlers = plugin.Services.GetServices<ICapabilityHandler>().ToList();
		Assert.Multiple(() =>
		{
			Assert.That(plugin.Metadata.IconPath, Is.Null);
			Assert.That(handlers.Select(handler => handler.Kind), Has.None.EqualTo(CapabilityKinds.Icons));
			Assert.That(handlers.SelectMany(handler => handler.DeclareCapabilities())
					.Select(capability => capability.Kind),
				Has.None.EqualTo(CapabilityKinds.Icons));
		});
	}

	private PluginHostBuilder CreateBuilder(string extension)
	{
		_fixture = new PluginManifestFixture(ManifestWithIcon.Replace("{0}", extension, StringComparison.Ordinal));

		return _fixture.CreateBuilder();
	}

	private static IconsDescribePayload Describe(PluginApplication plugin)
	{
		var handler = plugin.Services.GetServices<ICapabilityHandler>()
			.Single(candidate => candidate.Kind == CapabilityKinds.Icons);

		var result = handler.InvokeAsync(new CapabilityInvocation
				{
					Kind = CapabilityKinds.Icons,
					LocalId = "icon",
					Operation = CapabilityOperations.Icons.Describe,
					CorrelationId = "correlation",
					Services = plugin.Services
				},
				CancellationToken.None)
			.GetAwaiter()
			.GetResult();

		Assert.That(result.IsFailure, Is.False, "icons/describe failed");
		return result.Data!.Value.Deserialize<IconsDescribePayload>(PluginProtocolJson.Options)!;
	}
}
