using MacroDeckHost.Application.Portable;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Portable;

namespace MacroDeckHost.Tests.UnitTests.Portable;

[TestFixture]
public class PortableScriptVariableTests
{
	private PortabilityTestHarness _harness = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new PortabilityTestHarness();
	}

	[TearDown]
	public void TearDown()
	{
		_harness.Dispose();
	}

	[Test]
	public async Task A_bundled_scripts_locals_travel_with_it_to_the_imported_copy()
	{
		var script = (await _harness.Scripts.Create("Counter", null, "[]")).Data!;
		await _harness.Variables.CreateUserVariable("count",
			VariableScope.Script,
			script.Id.ToString(),
			VariableType.Numeric,
			3m,
			1);
		var original = _harness.VariableRegistry.FindByName(VariableScope.Script, script.Id.ToString(), "count")!;
		var (source, _, _) = await _harness.SeedProfile(RunScriptWidgetData(script.Id));

		var export = await _harness.ProfileService.Export(source.Id,
			PortableExportOptions.Default,
			CancellationToken.None);
		var import = await _harness.ProfileService.Import(export.Data!, null, CancellationToken.None);

		var imported = _harness.Scripts.GetAll().Single(candidate => candidate.Id != script.Id);
		var copied = _harness.VariableRegistry.FindByName(VariableScope.Script, imported.Id.ToString(), "count");

		Assert.Multiple(() =>
		{
			Assert.That(import.Success, Is.True);
			Assert.That(copied, Is.Not.Null);
			Assert.That(copied!.Classification, Is.EqualTo(VariableClassification.User));
			Assert.That(copied.Type, Is.EqualTo(VariableType.Numeric));
			Assert.That(copied.Value, Is.EqualTo(original.Value));
			Assert.That(copied.DecimalPlaces, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task An_archive_written_before_script_locals_existed_imports_its_script_without_any()
	{
		var script = (await _harness.Scripts.Create("Counter", null, "[]")).Data!;
		await _harness.Variables.CreateUserVariable("count",
			VariableScope.Script,
			script.Id.ToString(),
			VariableType.Numeric,
			3m,
			null);
		var (source, _, _) = await _harness.SeedProfile(RunScriptWidgetData(script.Id));
		var export = await _harness.ProfileService.Export(source.Id,
			PortableExportOptions.Default,
			CancellationToken.None);

		var outcome = PortableArchive.Read(export.Data!, null);
		var content = outcome.Content!;
		content.ScriptVariables.Clear();
		var rewritten = PortableArchive.Write(outcome.Manifest!, content, outcome.Icons.ToList(), null);

		var import = await _harness.ProfileService.Import(rewritten, null, CancellationToken.None);

		var imported = _harness.Scripts.GetAll().Single(candidate => candidate.Id != script.Id);
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(import.Success, Is.True);
			Assert.That(await _harness.Variables.GetByScope(VariableScope.Script, imported.Id.ToString()), Is.Empty);
		});
	}

	[Test]
	public async Task Locals_of_a_script_the_import_did_not_create_are_dropped()
	{
		var script = (await _harness.Scripts.Create("Counter", null, "[]")).Data!;
		var (source, _, _) = await _harness.SeedProfile(RunScriptWidgetData(script.Id));
		var export = await _harness.ProfileService.Export(source.Id,
			PortableExportOptions.Default,
			CancellationToken.None);

		var outcome = PortableArchive.Read(export.Data!, null);
		var content = outcome.Content!;
		content.Scripts.Single().Name = " ";
		content.ScriptVariables.Add(Local(script.Id, "lost"));
		content.ScriptVariables.Add(Local(Guid.NewGuid(), "stray"));
		var rewritten = PortableArchive.Write(outcome.Manifest!, content, outcome.Icons.ToList(), null);

		await _harness.ProfileService.Import(rewritten, null, CancellationToken.None);

		var scriptLocals = (await _harness.Variables.GetAll())
			.Where(variable => variable.Scope == VariableScope.Script)
			.ToList();
		Assert.That(scriptLocals, Is.Empty);
	}

	private static PortableScriptVariable Local(Guid scriptId, string name)
	{
		return new PortableScriptVariable { ScriptId = scriptId, Name = name, Type = VariableType.Text, Value = "x" };
	}

	private static string RunScriptWidgetData(Guid scriptId)
	{
		return "{\"flows\":\"[{\\\"triggerType\\\":\\\"onShortPress\\\",\\\"children\\\":[{\\\"actionId\\\":"
			+ "\\\"run-script\\\",\\\"parameters\\\":[{\\\"name\\\":\\\"scriptId\\\",\\\"value\\\":\\\""
			+ scriptId
			+ "\\\"}]}]}]\"}";
	}
}
