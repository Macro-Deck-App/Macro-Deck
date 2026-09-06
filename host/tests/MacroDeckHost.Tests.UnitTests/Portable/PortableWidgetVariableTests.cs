using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Portable;

namespace MacroDeckHost.Tests.UnitTests.Portable;

[TestFixture]
public class PortableWidgetVariableTests
{
	private PortabilityTestHarness _harness = null!;

	[SetUp]
	public void SetUp() => _harness = new PortabilityTestHarness();

	[TearDown]
	public void TearDown() => _harness.Dispose();

	[Test]
	public async Task Widgets_RoundTrip_PreservesNameTypeValueAndDecimalPlaces_UnderTheNewWidgetId()
	{
		var (profile, sourceFolder, widget) = await _harness.SeedProfile(null);
		var target = await _harness.AddFolder(profile.Id, "Target");
		await AddUserVariables(widget.Id);

		var export = await _harness.WidgetService.Export(sourceFolder.Id,
			[widget.Id],
			PortableExportOptions.Default,
			CancellationToken.None);
		Assert.That(export.Success, Is.True);

		var import = await _harness.WidgetService.Import(target.Id, 0, 0, export.Data!, null, CancellationToken.None);
		Assert.That(import.Success, Is.True);
		var newWidgetId = import.Data!.Single().Id;

		await AssertUserVariablesRoundTripped(newWidgetId);
	}

	[Test]
	public async Task Folder_RoundTrip_PreservesNameTypeValueAndDecimalPlaces_UnderTheNewWidgetId()
	{
		var (profile, _, _) = await _harness.SeedProfile(null);
		var source = await _harness.AddFolder(profile.Id, "Lights");
		var widget = _harness.AddWidget(source.Id, "{\"label\":\"a\"}");
		await AddUserVariables(widget.Id);

		var export = await _harness.FolderService.Export(source.Id,
			PortableExportOptions.Default,
			CancellationToken.None);
		Assert.That(export.Success, Is.True);

		var import = await _harness.FolderService.Import(profile.Id, null, export.Data!, null, CancellationToken.None);
		Assert.That(import.Success, Is.True);
		var newWidgetId = import.Data!.Single().Widgets.Single().Id;

		await AssertUserVariablesRoundTripped(newWidgetId);
	}

	[Test]
	public async Task Profile_RoundTrip_PreservesNameTypeValueAndDecimalPlaces_UnderTheNewWidgetId()
	{
		var (source, _, widget) = await _harness.SeedProfile("{\"label\":\"a\"}");
		await AddUserVariables(widget.Id);

		var export = await _harness.ProfileService.Export(source.Id,
			PortableExportOptions.Default,
			CancellationToken.None);
		Assert.That(export.Success, Is.True);

		var import = await _harness.ProfileService.Import(export.Data!, null, CancellationToken.None);
		Assert.That(import.Success, Is.True);
		var newWidgetId = _harness.FolderCache.GetFoldersByProfileId(import.Data!.Id).Single().Widgets.Single().Id;

		await AssertUserVariablesRoundTripped(newWidgetId);
	}

	[Test]
	public async Task OnlyTheUserClassifiedWidgetVariable_TravelsThroughExportAndImport()
	{
		var (profile, sourceFolder, widget) = await _harness.SeedProfile(null);
		var target = await _harness.AddFolder(profile.Id, "Target");
		await _harness.AddWidgetVariable(widget.Id, "mine", VariableClassification.User, value: "user-value");
		await _harness.AddWidgetVariable(widget.Id, "derived", VariableClassification.Widget, value: "widget-value");
		await _harness.AddWidgetVariable(widget.Id,
			"external",
			VariableClassification.Integration,
			value: "integration-value");

		var export = await _harness.WidgetService.Export(sourceFolder.Id,
			[widget.Id],
			PortableExportOptions.Default,
			CancellationToken.None);
		var import = await _harness.WidgetService.Import(target.Id, 0, 0, export.Data!, null, CancellationToken.None);
		Assert.That(import.Success, Is.True);
		var newWidgetId = import.Data!.Single().Id;

		var imported = await _harness.Variables.GetByScope(VariableScope.Widget, newWidgetId.ToString());

		Assert.Multiple(() =>
		{
			Assert.That(imported, Has.Count.EqualTo(1));
			Assert.That(imported[0].Name, Is.EqualTo("mine"));
			Assert.That(imported[0].Classification, Is.EqualTo(VariableClassification.User));
			Assert.That(imported[0].Value, Is.EqualTo("user-value"));
		});
	}

	[Test]
	public async Task ImportingTheSameArchiveTwice_YieldsIndependentCopies()
	{
		var (profile, sourceFolder, widget) = await _harness.SeedProfile(null);
		var targetA = await _harness.AddFolder(profile.Id, "A");
		var targetB = await _harness.AddFolder(profile.Id, "B");
		await _harness.AddWidgetVariable(widget.Id,
			"counter",
			VariableClassification.User,
			VariableType.Numeric,
			"1",
			0);

		var export = await _harness.WidgetService.Export(sourceFolder.Id,
			[widget.Id],
			PortableExportOptions.Default,
			CancellationToken.None);

		var importA =
			await _harness.WidgetService.Import(targetA.Id, 0, 0, export.Data!, null, CancellationToken.None);
		var importB =
			await _harness.WidgetService.Import(targetB.Id, 0, 0, export.Data!, null, CancellationToken.None);
		var widgetA = importA.Data!.Single().Id;
		var widgetB = importB.Data!.Single().Id;

		var variableA = (await _harness.Variables.GetByScope(VariableScope.Widget, widgetA.ToString())).Single();
		await _harness.Variables.SetValue(variableA.Id, 42);

		var variableB = (await _harness.Variables.GetByScope(VariableScope.Widget, widgetB.ToString())).Single();
		var reloadedA = await _harness.Variables.GetById(variableA.Id);

		Assert.Multiple(() =>
		{
			Assert.That(variableA.Id, Is.Not.EqualTo(variableB.Id));
			Assert.That(reloadedA!.Value, Is.EqualTo("42"));
			Assert.That(variableB.Value, Is.EqualTo("1"));
		});
	}

	[Test]
	public async Task ArchiveWithoutVariablesOrSourceIds_ImportsCleanly()
	{
		var (profile, sourceFolder, widget) = await _harness.SeedProfile("{\"label\":\"a\"}");
		var target = await _harness.AddFolder(profile.Id, "Target");
		await _harness.AddWidgetVariable(widget.Id, "mine", VariableClassification.User);

		var export = await _harness.WidgetService.Export(sourceFolder.Id,
			[widget.Id],
			PortableExportOptions.Default,
			CancellationToken.None);

		// Simulate an archive written before this change: no SourceId on the widget and no Variables at all.
		var (manifest, content, icons) = ReadRaw(export.Data!);
		content.Widgets!.Single().SourceId = null;
		content.Variables.Clear();
		var rewritten = PortableArchive.Write(manifest, content, icons, password: null);

		var import = await _harness.WidgetService.Import(target.Id, 0, 0, rewritten, null, CancellationToken.None);

		Assert.That(import.Success, Is.True);
		var newWidgetId = import.Data!.Single().Id;
		Assert.That(await _harness.Variables.GetByScope(VariableScope.Widget, newWidgetId.ToString()), Is.Empty);
	}

	[Test]
	public async Task HandCraftedArchive_WithAnUnknownOrLiveLocalWidgetId_AttachesNothing()
	{
		var (profile, sourceFolder, widget) = await _harness.SeedProfile("{\"label\":\"a\"}");
		var target = await _harness.AddFolder(profile.Id, "Target");
		var liveLocalWidget = _harness.AddWidget(target.Id, "{\"label\":\"live\"}", x: 4, y: 2);

		var export = await _harness.WidgetService.Export(sourceFolder.Id,
			[widget.Id],
			PortableExportOptions.Default,
			CancellationToken.None);

		var (manifest, content, icons) = ReadRaw(export.Data!);
		content.Variables.Add(new PortableVariable
		{
			WidgetId = Guid.NewGuid(),
			Name = "orphan",
			Type = VariableType.Text,
			Value = "x"
		});
		content.Variables.Add(new PortableVariable
		{
			WidgetId = liveLocalWidget.Id,
			Name = "hijacked",
			Type = VariableType.Text,
			Value = "y"
		});
		var rewritten = PortableArchive.Write(manifest, content, icons, password: null);

		var import = await _harness.WidgetService.Import(target.Id, 0, 0, rewritten, null, CancellationToken.None);
		Assert.That(import.Success, Is.True);
		var newWidgetId = import.Data!.Single().Id;

		Assert.Multiple(async () =>
		{
			Assert.That(await _harness.Variables.GetByScope(VariableScope.Widget, newWidgetId.ToString()), Is.Empty);
			Assert.That(await _harness.Variables.GetByScope(VariableScope.Widget, liveLocalWidget.Id.ToString()),
				Is.Empty);
		});
	}

	[Test]
	public async Task ArchiveVariable_WithExtraFieldsBeyondThePortableContract_ImportsAsAPlainUserVariable()
	{
		var (profile, sourceFolder, widget) = await _harness.SeedProfile("{\"label\":\"a\"}");
		var target = await _harness.AddFolder(profile.Id, "Target");

		var export = await _harness.WidgetService.Export(sourceFolder.Id,
			[widget.Id],
			PortableExportOptions.Default,
			CancellationToken.None);

		using var zip = new ZipArchive(new MemoryStream(export.Data!), ZipArchiveMode.Read);
		var entries = zip.Entries
			.Select(entry =>
			{
				using var stream = entry.Open();
				using var memory = new MemoryStream();
				stream.CopyTo(memory);
				return (entry.FullName, Bytes: memory.ToArray());
			})
			.ToList();

		var contentIndex = entries.FindIndex(e => e.FullName == "content.json");
		var node = JsonNode.Parse(Encoding.UTF8.GetString(entries[contentIndex].Bytes))!.AsObject();
		var sourceWidgetId = widget.Id.ToString();
		node["variables"] = new JsonArray(new JsonObject
		{
			["widgetId"] = sourceWidgetId,
			["name"] = "smuggled",
			["type"] = "Text",
			["value"] = "payload",
			["classification"] = "Integration",
			["ownerIntegrationId"] = "app.macro-deck.obs",
			["definitionId"] = "some-definition",
			["presentation"] = new JsonObject()
		});
		entries[contentIndex] = ("content.json", Encoding.UTF8.GetBytes(node.ToJsonString()));

		using var output = new MemoryStream();
		using (var outZip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
		{
			foreach (var (name, bytes) in entries)
			{
				var outEntry = outZip.CreateEntry(name, CompressionLevel.NoCompression);
				using var entryStream = outEntry.Open();
				entryStream.Write(bytes);
			}
		}

		var import = await _harness.WidgetService.Import(target.Id,
			0,
			0,
			output.ToArray(),
			null,
			CancellationToken.None);
		Assert.That(import.Success, Is.True);
		var newWidgetId = import.Data!.Single().Id;

		var imported = (await _harness.Variables.GetByScope(VariableScope.Widget, newWidgetId.ToString())).Single();

		Assert.Multiple(() =>
		{
			Assert.That(imported.Name, Is.EqualTo("smuggled"));
			Assert.That(imported.Classification, Is.EqualTo(VariableClassification.User));
			Assert.That(imported.OwnerIntegrationId, Is.Null);
			Assert.That(imported.DefinitionId, Is.Null);
			Assert.That(imported.Presentation, Is.Null);
		});
	}

	[Test]
	public async Task WidgetVariable_SharingItsNameWithAGlobalVariable_LeavesBothInPlace()
	{
		var (profile, sourceFolder, widget) = await _harness.SeedProfile(null);
		var target = await _harness.AddFolder(profile.Id, "Target");
		var global = await _harness.Variables.CreateUserVariable("shared",
			VariableScope.Global,
			null,
			VariableType.Text,
			"global-value",
			null);
		Assert.That(global.Success, Is.True);
		await _harness.AddWidgetVariable(widget.Id, "shared", VariableClassification.User, value: "widget-value");

		var export = await _harness.WidgetService.Export(sourceFolder.Id,
			[widget.Id],
			PortableExportOptions.Default,
			CancellationToken.None);
		var import = await _harness.WidgetService.Import(target.Id, 0, 0, export.Data!, null, CancellationToken.None);
		Assert.That(import.Success, Is.True);
		var newWidgetId = import.Data!.Single().Id;

		var resolved = await _harness.Variables.Resolve("shared", VariableScope.Widget, newWidgetId.ToString());
		var globalStillThere = await _harness.Variables.GetById(global.Data!.Id);

		Assert.Multiple(() =>
		{
			Assert.That(resolved!.Value, Is.EqualTo("widget-value"));
			Assert.That(resolved.Scope, Is.EqualTo(VariableScope.Widget));
			Assert.That(globalStillThere!.Value, Is.EqualTo("global-value"));
		});
	}

	[Test]
	public async Task ImportedUserVariableNamedState_ShadowsAHostDerivedStateVariableOnTheSameWidget()
	{
		var newWidgetId = Guid.NewGuid();
		await _harness.Variables.UpsertWidgetVariable(VariableScope.Widget,
			newWidgetId.ToString(),
			"state",
			VariableType.Text,
			"off");

		await _harness.VariableCloner.Restore(newWidgetId,
			[new MacroDeckHost.Application.Variables.WidgetVariableSnapshot("state", VariableType.Text, "on", null)]);

		var variables = await _harness.Variables.GetByScope(VariableScope.Widget, newWidgetId.ToString());

		Assert.Multiple(() =>
		{
			Assert.That(variables, Has.Count.EqualTo(1));
			Assert.That(variables[0].Classification, Is.EqualTo(VariableClassification.User));
			Assert.That(variables[0].Value, Is.EqualTo("on"));
		});
	}

	[Test]
	public async Task TwoWidgetsInOneArchive_DoNotCrossAssignTheirVariables()
	{
		var (profile, sourceFolder, widgetA) = await _harness.SeedProfile("{\"label\":\"a\"}");
		var widgetB = _harness.AddWidget(sourceFolder.Id, "{\"label\":\"b\"}", x: 1, y: 0);
		var target = await _harness.AddFolder(profile.Id, "Target");
		await _harness.AddWidgetVariable(widgetA.Id, "only_a", VariableClassification.User, value: "a-value");
		await _harness.AddWidgetVariable(widgetB.Id, "only_b", VariableClassification.User, value: "b-value");

		var export = await _harness.WidgetService.Export(sourceFolder.Id,
			[widgetA.Id, widgetB.Id],
			PortableExportOptions.Default,
			CancellationToken.None);
		var import = await _harness.WidgetService.Import(target.Id, 0, 0, export.Data!, null, CancellationToken.None);
		Assert.That(import.Success, Is.True);

		var created = import.Data!;
		var newA = created.Single(w => w.PositionX == 0).Id;
		var newB = created.Single(w => w.PositionX == 1).Id;

		var varsOnA = await _harness.Variables.GetByScope(VariableScope.Widget, newA.ToString());
		var varsOnB = await _harness.Variables.GetByScope(VariableScope.Widget, newB.ToString());

		Assert.Multiple(() =>
		{
			Assert.That(varsOnA.Select(v => v.Name), Is.EqualTo(new[] { "only_a" }));
			Assert.That(varsOnB.Select(v => v.Name), Is.EqualTo(new[] { "only_b" }));
		});
	}

	private async Task AddUserVariables(Guid widgetId)
	{
		await _harness.AddWidgetVariable(widgetId,
			"amount",
			VariableClassification.User,
			VariableType.Numeric,
			"1.50",
			2);
		await _harness.AddWidgetVariable(widgetId,
			"enabled",
			VariableClassification.User,
			VariableType.Boolean,
			false);
		await _harness.AddWidgetVariable(widgetId, "code", VariableClassification.User, VariableType.Text, "007");
	}

	private async Task AssertUserVariablesRoundTripped(Guid newWidgetId)
	{
		var variables = await _harness.Variables.GetByScope(VariableScope.Widget, newWidgetId.ToString());
		var byName = variables.ToDictionary(v => v.Name);

		Assert.Multiple(() =>
		{
			Assert.That(variables, Has.Count.EqualTo(3));
			Assert.That(byName["amount"].Type, Is.EqualTo(VariableType.Numeric));
			Assert.That(byName["amount"].Value, Is.EqualTo("1.50"));
			Assert.That(byName["amount"].DecimalPlaces, Is.EqualTo(2));
			Assert.That(byName["enabled"].Type, Is.EqualTo(VariableType.Boolean));
			Assert.That(byName["enabled"].Value, Is.EqualTo("false"));
			Assert.That(byName["code"].Type, Is.EqualTo(VariableType.Text));
			Assert.That(byName["code"].Value, Is.EqualTo("007"));
			Assert.That(variables.All(v => v.Classification == VariableClassification.User), Is.True);
		});
	}

	private static (PortableArchiveManifest Manifest, PortableContent Content, List<PortableIconFile> Icons) ReadRaw(
		byte[] archiveBytes)
	{
		var outcome = PortableArchive.Read(archiveBytes, password: null);
		return (outcome.Manifest!, outcome.Content!, outcome.Icons.ToList());
	}
}
