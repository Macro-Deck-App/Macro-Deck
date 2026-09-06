using MacroDeckHost.Application.Events;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using VariableWriteCapability = MacroDeck.Sdk.Variables.VariableWriteCapability;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class VariableServiceTests
{
	private RecordingMediator _mediator = null!;
	private VariableService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_mediator = new RecordingMediator();
		_service = TestVariableServices.Create(new VariableRegistry(), new NullUserVariableStore(), _mediator);
	}

	[Test]
	public async Task SetIntegrationVariableValue_publishes_on_first_value()
	{
		var id = await CreateIntegrationVariable();
		_mediator.Published.Clear();

		await _service.ReportIntegrationVariableValue("int1", id, 50);

		Assert.Multiple(() =>
		{
			Assert.That(Count<VariableUpdatedNotification>(), Is.EqualTo(1));
			Assert.That(Count<VariableValueChangedNotification>(), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task SetIntegrationVariableValue_suppresses_notifications_when_value_unchanged()
	{
		var id = await CreateIntegrationVariable();
		await _service.ReportIntegrationVariableValue("int1", id, 50);
		_mediator.Published.Clear();

		// A steady-state poll re-reporting the same value must not flood clients.
		await _service.ReportIntegrationVariableValue("int1", id, 50);

		Assert.That(_mediator.Published, Is.Empty);
	}

	[Test]
	public async Task SetIntegrationVariableValue_republishes_when_recovering_from_unavailable()
	{
		var id = await CreateIntegrationVariable();
		await _service.ReportIntegrationVariableValue("int1", id, 50);
		await _service.SetIntegrationVariableAvailability("int1", id, false);
		_mediator.Published.Clear();

		await _service.ReportIntegrationVariableValue("int1", id, 50);

		Assert.Multiple(() =>
		{
			Assert.That(Count<VariableUpdatedNotification>(), Is.EqualTo(1));
			Assert.That(Count<VariableValueChangedNotification>(), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task UpdateUserVariable_suppresses_notifications_when_nothing_changed()
	{
		var create = await _service.CreateUserVariable("v",
			VariableScope.Global,
			null,
			VariableType.Text,
			"hello",
			null);
		var id = create.Data!.Id;
		_mediator.Published.Clear();

		await _service.SetValue(id, "hello");

		Assert.That(_mediator.Published, Is.Empty);
	}

	[Test]
	public async Task UpdateUserVariable_publishes_update_only_on_rename()
	{
		var create = await _service.CreateUserVariable("v",
			VariableScope.Global,
			null,
			VariableType.Text,
			"hello",
			null);
		var id = create.Data!.Id;
		_mediator.Published.Clear();

		await _service.UpdateUserVariable(id, "renamed", null);

		Assert.Multiple(() =>
		{
			Assert.That(Count<VariableUpdatedNotification>(), Is.EqualTo(1));
			Assert.That(Count<VariableValueChangedNotification>(), Is.EqualTo(0));
		});
	}

	[Test]
	public async Task UpsertWidgetVariable_creates_read_only_widget_variable()
	{
		await _service.UpsertWidgetVariable(VariableScope.Widget, "w1", "toggled", VariableType.Boolean, true);

		var variables = await _service.GetByScope(VariableScope.Widget, "w1");
		var toggled = variables.Single(v => v.Name == "toggled");

		Assert.Multiple(() =>
		{
			Assert.That(toggled.Classification, Is.EqualTo(VariableClassification.Widget));
			Assert.That(toggled.Value, Is.EqualTo("true"));
			Assert.That(Count<VariableCreatedNotification>(), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task UpsertWidgetVariable_publishes_change_only_when_value_differs()
	{
		await _service.UpsertWidgetVariable(VariableScope.Widget, "w1", "toggled", VariableType.Boolean, false);
		_mediator.Published.Clear();

		await _service.UpsertWidgetVariable(VariableScope.Widget, "w1", "toggled", VariableType.Boolean, false);
		Assert.That(_mediator.Published, Is.Empty);

		await _service.UpsertWidgetVariable(VariableScope.Widget, "w1", "toggled", VariableType.Boolean, true);
		Assert.Multiple(() =>
		{
			Assert.That(Count<VariableUpdatedNotification>(), Is.EqualTo(1));
			Assert.That(Count<VariableValueChangedNotification>(), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task UpsertWidgetVariable_is_not_persisted_to_the_user_store()
	{
		var store = new RecordingUserVariableStore();
		var service = TestVariableServices.Create(new VariableRegistry(), store, _mediator);

		await service.UpsertWidgetVariable(VariableScope.Widget, "w1", "toggled", VariableType.Boolean, true);

		Assert.That(store.SaveCallCount, Is.EqualTo(0));
	}

	[Test]
	public async Task RemoveWidgetVariable_removes_and_publishes_deleted()
	{
		await _service.UpsertWidgetVariable(VariableScope.Widget, "w1", "toggled", VariableType.Boolean, true);
		_mediator.Published.Clear();

		await _service.RemoveWidgetVariable(VariableScope.Widget, "w1", "toggled");

		Assert.Multiple(() =>
		{
			Assert.That((_service.GetByScope(VariableScope.Widget, "w1").Result), Is.Empty);
			Assert.That(Count<VariableDeletedNotification>(), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task RemoveWidgetVariable_leaves_user_variables_untouched()
	{
		await _service.CreateUserVariable("toggled",
			VariableScope.Widget,
			"w1",
			VariableType.Boolean,
			true,
			null);
		_mediator.Published.Clear();

		await _service.RemoveWidgetVariable(VariableScope.Widget, "w1", "toggled");

		Assert.Multiple(() =>
		{
			Assert.That(_service.GetByScope(VariableScope.Widget, "w1").Result, Has.Count.EqualTo(1));
			Assert.That(_mediator.Published, Is.Empty);
		});
	}

	/// <summary>
	/// A provider may revise a unit, a display name or a write capability without the variable's name
	/// changing - a plugin update, a reconnect, a renamed configuration. Re-registration therefore
	/// re-applies the declaration onto the existing entity instead of keeping whatever the host happened to
	/// see first, and publishes only when something actually moved.
	/// </summary>
	[Test]
	public async Task Re_registering_a_definition_re_applies_a_revised_declaration_and_publishes_once()
	{
		await Register(new VariableDeclaration { Unit = "%", SemanticKind = "percentage" });
		_mediator.Published.Clear();

		var unchanged = await Register(new VariableDeclaration { Unit = "%", SemanticKind = "percentage" });
		var publishedAfterNoChange = _mediator.Published.Count;

		var revised = await Register(new VariableDeclaration
		{
			Unit = "dB", SemanticKind = "percentage", Write = new VariableWriteCapability()
		});

		Assert.Multiple(() =>
		{
			Assert.That(publishedAfterNoChange, Is.Zero, "an identical re-declaration is not a change");
			Assert.That(unchanged.Data!.Id, Is.EqualTo(revised.Data!.Id), "the entity is reused, never replaced");
			Assert.That(revised.Data.Unit, Is.EqualTo("dB"));
			Assert.That(revised.Data.CanWrite, Is.True);
			Assert.That(Count<VariableUpdatedNotification>(), Is.EqualTo(1));
		});
	}

	private Task<Result<VariableEntity, VariableError>> Register(VariableDeclaration declaration)
		=> _service.CreateIntegrationVariable("int1",
			"gain",
			VariableScope.Global,
			null,
			VariableType.Numeric,
			null,
			0,
			"gain",
			declaration);

	private async Task<Guid> CreateIntegrationVariable()
	{
		var result = await _service.CreateIntegrationVariable("int1",
			"v",
			VariableScope.Global,
			null,
			VariableType.Numeric,
			null,
			0);
		return result.Data!.Id;
	}

	private int Count<T>() => _mediator.Published.OfType<T>().Count();

	private sealed class NullUserVariableStore : IUserVariableStore
	{
		public IReadOnlyList<VariableEntity> Load() => [];

		public void Save(IEnumerable<VariableEntity> userVariables)
		{
		}
	}

	private sealed class RecordingUserVariableStore : IUserVariableStore
	{
		public int SaveCallCount { get; private set; }

		public IReadOnlyList<VariableEntity> Load() => [];

		public void Save(IEnumerable<VariableEntity> userVariables) => SaveCallCount++;
	}
}
