using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Automations;

[TestFixture]
public class AutomationServiceTests
{
	private const string Flows = """[{"triggerId":"t1","triggerType":"onEvent","children":[]}]""";

	private static readonly string[] _namesInOrder = ["Alpha", "mike", "zulu"];

	private InMemoryAutomationStore _store = null!;
	private AutomationCache _cache = null!;
	private RecordingMediator _mediator = null!;
	private AutomationService _service = null!;

	[SetUp]
	public async Task SetUp()
	{
		_store = new InMemoryAutomationStore();
		_cache = new AutomationCache(_store, new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		_mediator = new RecordingMediator();
		_service = new AutomationService(_cache, _mediator);
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task Create_stores_an_enabled_automation_and_publishes_it()
	{
		var result = await _service.Create("Stream start", " lights on ", Flows);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Data!.Name, Is.EqualTo("Stream start"));
			Assert.That(result.Data.Description, Is.EqualTo("lights on"));
			Assert.That(result.Data.Enabled, Is.True);
			Assert.That(result.Data.Flows, Is.EqualTo(Flows));
			Assert.That(_store.Get(result.Data.Id), Is.Not.Null);
			Assert.That(_mediator.Published.OfType<AutomationCreatedNotification>().Count(), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Create_rejects_a_blank_name()
	{
		var result = await _service.Create("   ", null, null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(AutomationError.ValidationError));
			Assert.That(_mediator.Published, Is.Empty);
		});
	}

	[Test]
	public async Task Create_rejects_an_over_long_name()
	{
		var result = await _service.Create(new string('x', 101), null, null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(AutomationError.ValidationError));
		});
	}

	[Test]
	public async Task Create_rejects_an_over_long_description()
	{
		var result = await _service.Create("Name", new string('x', 501), null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(AutomationError.ValidationError));
		});
	}

	[Test]
	public async Task Update_with_only_enabled_leaves_the_other_fields_alone()
	{
		var created = await _service.Create("Name", "Description", Flows);

		var result = await _service.Update(created.Data!.Id, null, null, null, enabled: false);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Data!.Enabled, Is.False);
			Assert.That(result.Data.Name, Is.EqualTo("Name"));
			Assert.That(result.Data.Description, Is.EqualTo("Description"));
			Assert.That(result.Data.Flows, Is.EqualTo(Flows));
		});
	}

	[Test]
	public async Task Update_publishes_the_updated_automation()
	{
		var created = await _service.Create("Name", null, null);
		_mediator.Published.Clear();

		await _service.Update(created.Data!.Id, null, null, Flows, null);

		var published = _mediator.Published.OfType<AutomationUpdatedNotification>().Single();
		Assert.That(published.Automation.Flows, Is.EqualTo(Flows));
	}

	[Test]
	public async Task Update_of_an_unknown_automation_reports_not_found()
	{
		var result = await _service.Update(Guid.NewGuid(), "Name", null, null, null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(AutomationError.NotFound));
		});
	}

	[Test]
	public async Task Update_rejects_a_blank_name_without_storing_anything()
	{
		var created = await _service.Create("Name", null, Flows);

		var result = await _service.Update(created.Data!.Id, "  ", null, null, null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(_service.GetById(created.Data.Id)!.Name, Is.EqualTo("Name"));
		});
	}

	[Test]
	public async Task Duplicate_copies_the_flow_verbatim_but_arrives_disabled()
	{
		var created = await _service.Create("Stream start", "Description", Flows);

		var result = await _service.Duplicate(created.Data!.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Data!.Id, Is.Not.EqualTo(created.Data.Id));
			Assert.That(result.Data.Name, Is.EqualTo("Stream start (copy)"));
			Assert.That(result.Data.Description, Is.EqualTo("Description"));
			Assert.That(result.Data.Flows, Is.EqualTo(Flows));
			Assert.That(result.Data.Enabled, Is.False);
		});
	}

	[Test]
	public async Task Duplicate_numbers_further_copies()
	{
		var created = await _service.Create("Stream start", null, null);
		await _service.Duplicate(created.Data!.Id);

		var second = await _service.Duplicate(created.Data.Id);

		Assert.That(second.Data!.Name, Is.EqualTo("Stream start (copy) 2"));
	}

	[Test]
	public async Task Duplicate_of_an_unknown_automation_reports_not_found()
	{
		var result = await _service.Duplicate(Guid.NewGuid());

		Assert.That(result.Error, Is.EqualTo(AutomationError.NotFound));
	}

	[Test]
	public async Task Delete_removes_the_file_and_publishes_the_deletion()
	{
		var created = await _service.Create("Name", null, Flows);

		var result = await _service.Delete(created.Data!.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_service.GetById(created.Data.Id), Is.Null);
			Assert.That(_store.Get(created.Data.Id), Is.Null);
			Assert.That(_mediator.Published.OfType<AutomationDeletedNotification>().Single().AutomationId,
				Is.EqualTo(created.Data.Id));
		});
	}

	[Test]
	public async Task Delete_of_an_unknown_automation_reports_not_found()
	{
		var result = await _service.Delete(Guid.NewGuid());

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(AutomationError.NotFound));
		});
	}

	[Test]
	public async Task GetAll_orders_by_name_case_insensitively()
	{
		await _service.Create("zulu", null, null);
		await _service.Create("Alpha", null, null);
		await _service.Create("mike", null, null);

		Assert.That(_service.GetAll().Select(automation => automation.Name),
			Is.EqualTo(_namesInOrder));
	}
}
