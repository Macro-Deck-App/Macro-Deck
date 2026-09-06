using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Services;

[TestFixture]
public class ScriptServiceTests
{
	private static readonly string[] _expectedOrder = ["alpha", "Mike", "Zulu"];
	private static readonly string[] _sceneOnly = ["scene"];
	private static readonly string[] _sceneAndExtra = ["scene", "extra"];

	private InMemoryScriptStore _store = null!;
	private ScriptCache _cache = null!;
	private RecordingMediator _mediator = null!;
	private ScriptService _service = null!;

	[SetUp]
	public async Task SetUp()
	{
		_store = new InMemoryScriptStore();
		_cache = new ScriptCache(_store, new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		_mediator = new RecordingMediator();
		_service = new ScriptService(_cache, _mediator);
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task Create_PersistsScriptAndAnnouncesIt()
	{
		var result = await _service.Create("  Start stream  ", " Opens OBS ", "[]");

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Name, Is.EqualTo("Start stream"));
			Assert.That(result.Data.Description, Is.EqualTo("Opens OBS"));
			Assert.That(_store.Get(result.Data.Id), Is.Not.Null);
			Assert.That(_mediator.Published.OfType<ScriptCreatedNotification>().Count(), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Create_RejectsBlankName()
	{
		var result = await _service.Create("   ", null, null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(ScriptError.ValidationError));
			Assert.That(_store.SaveCount, Is.Zero);
		});
	}

	[Test]
	public async Task Create_UsesTheGivenIdWhenOneIsSupplied()
	{
		var id = Guid.NewGuid();

		var result = await _service.Create("Imported", null, "[]", id);

		Assert.That(result.Data!.Id, Is.EqualTo(id));
	}

	[Test]
	public async Task Update_LeavesOmittedFieldsUntouched()
	{
		var created = await _service.Create("Original", "Description", "[{\"triggerType\":\"onRun\"}]");

		var result = await _service.Update(created.Data!.Id, "Renamed", null, null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Name, Is.EqualTo("Renamed"));
			Assert.That(result.Data.Description, Is.EqualTo("Description"));
			Assert.That(result.Data.Flows, Is.EqualTo("[{\"triggerType\":\"onRun\"}]"));
			Assert.That(_mediator.Published.OfType<ScriptUpdatedNotification>().Count(), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Update_ReportsNotFoundForAnUnknownScript()
	{
		var result = await _service.Update(Guid.NewGuid(), "Renamed", null, null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(ScriptError.NotFound));
		});
	}

	[Test]
	public async Task Duplicate_CopiesTheFlowVerbatimUnderANewId()
	{
		var created = await _service.Create("Scene switch", "Switches", "[{\"triggerType\":\"onRun\"}]");

		var copy = await _service.Duplicate(created.Data!.Id);

		Assert.Multiple(() =>
		{
			Assert.That(copy.Data!.Id, Is.Not.EqualTo(created.Data.Id));
			Assert.That(copy.Data.Name, Is.EqualTo("Scene switch (copy)"));
			Assert.That(copy.Data.Flows, Is.EqualTo(created.Data.Flows));
		});
	}

	[Test]
	public async Task Duplicate_NumbersFurtherCopiesInsteadOfRepeatingTheName()
	{
		var created = await _service.Create("Scene switch", null, "[]");
		await _service.Duplicate(created.Data!.Id);

		var second = await _service.Duplicate(created.Data.Id);

		Assert.That(second.Data!.Name, Is.EqualTo("Scene switch (copy) 2"));
	}

	[Test]
	public async Task Delete_RemovesTheFileAndAnnouncesIt()
	{
		var created = await _service.Create("Temporary", null, "[]");

		var result = await _service.Delete(created.Data!.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_store.Get(created.Data.Id), Is.Null);
			Assert.That(_mediator.Published.OfType<ScriptDeletedNotification>().Count(), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task GetAll_OrdersByNameSoTheRailIsStable()
	{
		await _service.Create("Zulu", null, "[]");
		await _service.Create("alpha", null, "[]");
		await _service.Create("Mike", null, "[]");

		Assert.That(_service.GetAll().Select(script => script.Name), Is.EqualTo(_expectedOrder));
	}

	[TestCase("Scene")]
	[TestCase("1scene")]
	[TestCase("my-scene")]
	[TestCase("my scene")]
	[TestCase("")]
	public async Task Create_RejectsAnInputNameAScriptCouldNotReadUnderVars(string name)
	{
		var result = await _service.Create("Script", null, "[]", null, [Input(name)]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(ScriptError.ValidationError));
			Assert.That(_store.SaveCount, Is.Zero);
		});
	}

	[TestCase("scene")]
	[TestCase("scene_2")]
	[TestCase("s")]
	public async Task Create_AcceptsAValidInputName(string name)
	{
		var result = await _service.Create("Script", null, "[]", null, [Input(name)]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Data!.Inputs.Single().Name, Is.EqualTo(name));
		});
	}

	[Test]
	public async Task Create_RejectsTheSameInputNameTwice()
	{
		var result = await _service.Create("Script", null, "[]", null, [Input("scene"), Input("scene")]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(ScriptError.ValidationError));
			Assert.That(_store.SaveCount, Is.Zero);
		});
	}

	[Test]
	public async Task Update_RejectsAnInvalidDeclarationWithoutChangingTheStoredScript()
	{
		var created = await _service.Create("Script", null, "[]", null, [Input("scene")]);
		var savesAfterCreate = _store.SaveCount;

		var result = await _service.Update(created.Data!.Id, "Renamed", null, null, [Input("Scene")]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(ScriptError.ValidationError));
			Assert.That(_store.SaveCount, Is.EqualTo(savesAfterCreate));
			Assert.That(_store.Get(created.Data.Id)!.Name, Is.EqualTo("Script"));
			Assert.That(_store.Get(created.Data.Id)!.Inputs.Single().Name, Is.EqualTo("scene"));
		});
	}

	[Test]
	public async Task Update_WithNullInputsLeavesTheDeclarationsUntouched()
	{
		var created = await _service.Create("Script", null, "[]", null, [Input("scene")]);

		var result = await _service.Update(created.Data!.Id, "Renamed", null, null);

		Assert.That(result.Data!.Inputs.Single().Name, Is.EqualTo("scene"));
	}

	[Test]
	public async Task Declarations_SurviveSaveAndReload()
	{
		var created = await _service.Create("Script",
			null,
			"[]",
			null,
			[
				new ScriptInput
				{
					Name = "volume",
					Type = ScriptInputType.Numeric,
					Label = "Volume",
					Required = true,
					DefaultValue = "70"
				}
			]);

		using var reloaded = new ScriptCache(_store, new LoggerConfiguration().CreateLogger());
		await reloaded.InitializeCache();

		var declared = reloaded.GetById(created.Data!.Id)!.Inputs.Single();
		Assert.Multiple(() =>
		{
			Assert.That(declared.Name, Is.EqualTo("volume"));
			Assert.That(declared.Type, Is.EqualTo(ScriptInputType.Numeric));
			Assert.That(declared.Label, Is.EqualTo("Volume"));
			Assert.That(declared.Required, Is.True);
			Assert.That(declared.DefaultValue, Is.EqualTo("70"));
		});
	}

	[Test]
	public async Task Duplicate_CopiesTheDeclarationsWithoutSharingTheList()
	{
		var created = await _service.Create("Script", null, "[]", null, [Input("scene")]);

		var copy = await _service.Duplicate(created.Data!.Id);
		copy.Data!.Inputs.Add(Input("extra"));

		Assert.Multiple(() =>
		{
			Assert.That(copy.Data.Inputs.Select(input => input.Name), Is.EqualTo(_sceneAndExtra));
			Assert.That(created.Data.Inputs.Select(input => input.Name), Is.EqualTo(_sceneOnly));
		});
	}

	private static ScriptInput Input(string name) => new() { Name = name, Type = ScriptInputType.Text };
}
