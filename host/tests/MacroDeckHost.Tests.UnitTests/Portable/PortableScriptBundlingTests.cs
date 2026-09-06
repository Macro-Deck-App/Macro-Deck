using MacroDeckHost.Application.Portable;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Tests.UnitTests.Portable;

[TestFixture]
public class PortableScriptBundlingTests
{
	private PortabilityTestHarness _harness = null!;

	[SetUp]
	public void SetUp() => _harness = new PortabilityTestHarness();

	[TearDown]
	public void TearDown() => _harness.Dispose();

	[Test]
	public async Task Export_ThenImport_RecreatesTheScriptAndRepointsTheWidget()
	{
		var script = (await _harness.Scripts.Create("Start stream", "Opens OBS", "[]")).Data!;
		var (source, _, _) = await _harness.SeedProfile(RunScriptWidgetData(script.Id));

		var export = await _harness.ProfileService.Export(source.Id,
			PortableExportOptions.Default,
			CancellationToken.None);
		var import = await _harness.ProfileService.Import(export.Data!, null, CancellationToken.None);

		var importedWidget = _harness.FolderCache.GetFoldersByProfileId(import.Data!.Id)
			.Single()
			.Widgets.Single();
		var imported = _harness.Scripts.GetAll().Single(s => s.Id != script.Id);

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Scripts.GetAll(), Has.Count.EqualTo(2));
			Assert.That(imported.Name, Is.EqualTo("Start stream"));
			Assert.That(importedWidget.Data, Does.Not.Contain(script.Id.ToString()));
			Assert.That(importedWidget.Data, Does.Contain(imported.Id.ToString()));
		});
	}

	[Test]
	public async Task Export_BundlesScriptsAScriptItselfRuns()
	{
		var inner = (await _harness.Scripts.Create("Inner", null, "[]")).Data!;
		var outer = (await _harness.Scripts.Create("Outer", null, RunScriptWidgetData(inner.Id))).Data!;
		var (source, _, _) = await _harness.SeedProfile(RunScriptWidgetData(outer.Id));

		var export = await _harness.ProfileService.Export(source.Id,
			PortableExportOptions.Default,
			CancellationToken.None);
		var import = await _harness.ProfileService.Import(export.Data!, null, CancellationToken.None);

		Assert.That(import.Success, Is.True);
		var imported = _harness.Scripts.GetAll().Where(s => s.Id != inner.Id && s.Id != outer.Id).ToList();
		var importedInner = imported.Single(s => s.Name == "Inner");
		var importedOuter = imported.Single(s => s.Name == "Outer");

		Assert.Multiple(() =>
		{
			Assert.That(imported, Has.Count.EqualTo(2));
			Assert.That(importedOuter.Flows, Does.Contain(importedInner.Id.ToString()));
			Assert.That(importedOuter.Flows, Does.Not.Contain(inner.Id.ToString()));
		});
	}

	[Test]
	public async Task Export_BundlesAnIconThatOnlyABundledScriptReferences()
	{
		var pack = await _harness.Icons.CreatePack("Source Pack");
		var icon = await _harness.AddReadyIcon(pack.Id, "star", sizes: [128]);
		var script = (await _harness.Scripts.Create("Notify", null, $"{{\"iconId\":\"{icon.Id}\"}}")).Data!;
		var (source, _, _) = await _harness.SeedProfile(RunScriptWidgetData(script.Id));

		var export = await _harness.ProfileService.Export(source.Id,
			PortableExportOptions.Default,
			CancellationToken.None);
		await _harness.ProfileService.Import(export.Data!, null, CancellationToken.None);

		var imported = _harness.Scripts.GetAll().Single(s => s.Id != script.Id);
		var referencedIcons = GuidReferences.ExtractAll(imported.Flows)
			.Where(id => _harness.Icons.Cache.GetIconById(id) is not null)
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(referencedIcons, Has.Count.EqualTo(1));
			Assert.That(referencedIcons[0], Is.EqualTo(icon.Id));
		});
	}

	[Test]
	public async Task Export_LeavesUnreferencedScriptsOutOfTheArchive()
	{
		await _harness.Scripts.Create("Unrelated", null, "[]");
		var (source, _, _) = await _harness.SeedProfile("{\"label\":\"plain\"}");

		var export = await _harness.ProfileService.Export(source.Id,
			PortableExportOptions.Default,
			CancellationToken.None);
		await _harness.ProfileService.Import(export.Data!, null, CancellationToken.None);

		Assert.That(_harness.Scripts.GetAll(), Has.Count.EqualTo(1));
	}

	[Test]
	public async Task Export_ThenImport_KeepsTheDeclarationsAndTheUnrenderedCallerValue()
	{
		var script = (await _harness.Scripts.Create("Start stream",
			null,
			"[]",
			null,
			[
				new ScriptInput
				{
					Name = "scene",
					Type = ScriptInputType.Text,
					Label = "Scene",
					Required = true,
					DefaultValue = "stream"
				}
			])).Data!;
		var (source, _, _) = await _harness.SeedProfile(RunScriptWidgetData(script.Id, "{{ vars.next_scene }}"));

		var export = await _harness.ProfileService.Export(source.Id,
			PortableExportOptions.Default,
			CancellationToken.None);
		var import = await _harness.ProfileService.Import(export.Data!, null, CancellationToken.None);

		var imported = _harness.Scripts.GetAll().Single(s => s.Id != script.Id);
		var importedWidget = _harness.FolderCache.GetFoldersByProfileId(import.Data!.Id)
			.Single()
			.Widgets.Single();
		var declared = imported.Inputs.Single();

		Assert.Multiple(() =>
		{
			Assert.That(declared.Name, Is.EqualTo("scene"));
			Assert.That(declared.Type, Is.EqualTo(ScriptInputType.Text));
			Assert.That(declared.Label, Is.EqualTo("Scene"));
			Assert.That(declared.Required, Is.True);
			Assert.That(declared.DefaultValue, Is.EqualTo("stream"));
			Assert.That(importedWidget.Data, Does.Contain("{{ vars.next_scene }}"));
			Assert.That(importedWidget.Data, Does.Contain(imported.Id.ToString()));
			Assert.That(importedWidget.Data, Does.Not.Contain(script.Id.ToString()));
		});
	}

	private static string RunScriptWidgetData(Guid scriptId) =>
		"{\"flows\":\"[{\\\"triggerType\\\":\\\"onShortPress\\\",\\\"children\\\":[{\\\"actionId\\\":" +
		"\\\"run-script\\\",\\\"parameters\\\":[{\\\"name\\\":\\\"scriptId\\\",\\\"value\\\":\\\"" +
		scriptId +
		"\\\"}]}]}]\"}";

	private static string RunScriptWidgetData(Guid scriptId, string sceneValue) =>
		"{\"flows\":\"[{\\\"triggerType\\\":\\\"onShortPress\\\",\\\"children\\\":[{\\\"actionId\\\":" +
		"\\\"run-script\\\",\\\"parameters\\\":[{\\\"name\\\":\\\"scriptId\\\",\\\"value\\\":\\\"" +
		scriptId +
		"\\\"},{\\\"name\\\":\\\"input:scene\\\",\\\"value\\\":\\\"" +
		sceneValue +
		"\\\"}]}]}]\"}";
}
