using MacroDeck.Sdk.Variables;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using DomainVariableType = MacroDeckHost.Domain.Enums.VariableType;
using SdkVariableType = MacroDeck.Sdk.Variables.VariableType;

namespace MacroDeckHost.Tests.UnitTests.Variables;

/// <summary>
/// ADR 0081's one write path. Before it, a value could be refused in three different places - the
/// classification check on <c>UpdateUserVariable</c>, the caller-is-owner check on the integration path,
/// and the UI handler's own rule - and each of them could disagree with the others about the same
/// variable. The substance of these tests is where a refusal comes from, not just that one happens.
/// </summary>
[TestFixture]
public class VariableWriteDispatchTests
{
	private const string IntegrationId = "writable-integration";

	private RecordingMediator _mediator = null!;
	private VariableRegistry _registry = null!;
	private FakeWritableVariableProviderIntegration _provider = null!;
	private VariableService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_mediator = new RecordingMediator();
		_registry = new VariableRegistry();
		_provider = new FakeWritableVariableProviderIntegration
		{
			Id = IntegrationId,
			IsInitialized = true,
			Variables =
			[
				VariableDefinition.Eager("volume", SdkVariableType.Numeric) with
				{
					Id = "volume", Write = new VariableWriteCapability()
				}
			]
		};
		_service = TestVariableServices.Create(_registry,
			new NullUserVariableStore(),
			_mediator,
			new ConfigurableIntegrationRegistry([_provider]));
	}

	[Test]
	public async Task A_provider_variable_that_declares_a_write_reaches_its_owner()
	{
		var id = await ProviderVariable(new VariableWriteCapability());

		var result = await _service.SetValue(id, 42d);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_provider.Writes, Is.EqualTo(new[] { ("volume", (object?)42d) }));
		});
	}

	/// <summary>
	/// The declaration is the gate, and the gate is in front of the provider rather than inside it. That is
	/// what lets a client decide whether to offer an editing control at all without asking the integration,
	/// and it is why <see cref="IVariableProvider.SetValueAsync" /> may assume it is only ever called for a
	/// variable that declared a write.
	/// </summary>
	[Test]
	public async Task A_provider_variable_that_declares_no_write_is_refused_without_the_provider_being_asked()
	{
		var id = await ProviderVariable(write: null);

		var result = await _service.SetValue(id, 42d);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(VariableError.NotWritable));
			Assert.That(_provider.Writes, Is.Empty, "the owning provider must never hear about a refused write");
		});
	}

	[Test]
	public async Task A_widget_variable_is_refused_because_its_widget_writes_it()
	{
		await _service.UpsertWidgetVariable(VariableScope.Widget, "w1", "toggled", DomainVariableType.Boolean, false);
		var entity = _registry.FindByName(VariableScope.Widget, "w1", "toggled")!;

		var result = await _service.SetValue(entity.Id, true);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(VariableError.NotWritable));
			Assert.That(entity.Value, Is.EqualTo("false"));
		});
	}

	[Test]
	public async Task A_user_variable_is_stored_by_the_host_and_never_dispatched()
	{
		var created = await _service.CreateUserVariable("greeting",
			VariableScope.Global,
			null,
			DomainVariableType.Text,
			"hello",
			null);

		var result = await _service.SetValue(created.Data!.Id, "bye");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Data!.Value, Is.EqualTo("bye"));
			Assert.That(_provider.Writes, Is.Empty);
		});
	}

	[Test]
	public async Task An_unknown_variable_is_not_found()
	{
		var result = await _service.SetValue(Guid.NewGuid(), 1);

		Assert.That(result.Error, Is.EqualTo(VariableError.NotFound));
	}

	/// <summary>
	/// The provider is authoritative about its own range - an OBS input gain-boosted past 100 is a real
	/// value the host must be able to send - so a write is not validated against the variable's own
	/// min/max/step. Only a value no provider could act on is refused here.
	/// </summary>
	[Test]
	public async Task A_write_outside_the_declared_range_is_dispatched_but_a_non_finite_one_is_not()
	{
		var id = await ProviderVariable(new VariableWriteCapability(), min: 0, max: 100);

		var beyond = await _service.SetValue(id, 250d);
		var nonFinite = await _service.SetValue(id, double.NaN);
		var infinite = await _service.SetValue(id, double.PositiveInfinity);

		Assert.Multiple(() =>
		{
			Assert.That(beyond.Success, Is.True);
			Assert.That(nonFinite.Error, Is.EqualTo(VariableError.InvalidValue));
			Assert.That(infinite.Error, Is.EqualTo(VariableError.InvalidValue));
			Assert.That(_provider.Writes, Has.Count.EqualTo(1), "only the finite value ever left the host");
		});
	}

	[TestCase(VariableWriteStatus.NotWritable, VariableError.NotWritable)]
	[TestCase(VariableWriteStatus.NotFound, VariableError.NotFound)]
	[TestCase(VariableWriteStatus.Unavailable, VariableError.OwnerUnavailable)]
	[TestCase(VariableWriteStatus.InvalidValue, VariableError.InvalidValue)]
	[TestCase(VariableWriteStatus.Failed, VariableError.WriteFailed)]
	public async Task A_providers_refusal_reaches_the_caller_as_itself(
		VariableWriteStatus status,
		VariableError expected)
	{
		var id = await ProviderVariable(new VariableWriteCapability());
		_provider.Answer = new VariableWriteResult { Status = status };

		var result = await _service.SetValue(id, 42d);

		Assert.That(result.Error, Is.EqualTo(expected));
	}

	/// <summary>
	/// "Applied" is the provider's word for "I took it", not the host's for "this is now the value". The
	/// authoritative reading comes back on the read side, so echoing the requested value here would show a
	/// number the device may have clamped or quantized away.
	/// </summary>
	[Test]
	public async Task An_applied_write_does_not_echo_the_value_into_the_registry()
	{
		var id = await ProviderVariable(new VariableWriteCapability());

		await _service.SetValue(id, 42d);

		Assert.That(_registry.GetById(id)!.Value, Is.EqualTo("10"));
	}

	[Test]
	public async Task A_write_to_a_variable_whose_owner_is_gone_reports_the_owner_rather_than_failing_the_write()
	{
		var id = await ProviderVariable(new VariableWriteCapability());
		_provider.IsInitialized = false;

		var result = await _service.SetValue(id, 42d);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(VariableError.OwnerUnavailable));
			Assert.That(_provider.Writes, Is.Empty);
		});
	}

	/// <summary>
	/// The two pre-existing checks were re-scoped rather than folded away, and each still answers the
	/// question it always did: renaming a provider variable is not a user's to do, and a provider report is
	/// only authentic from the owning provider. Neither of them is a write authority any more - that is
	/// <see cref="IVariableService.SetValue" /> alone.
	/// </summary>
	[Test]
	public async Task The_definition_and_report_paths_keep_their_own_checks()
	{
		var id = await ProviderVariable(new VariableWriteCapability());

		var renamed = await _service.UpdateUserVariable(id, "renamed", null);
		var reportedByAnother = await _service.ReportIntegrationVariableValue("someone-else", id, 99d);
		var reportedByOwner = await _service.ReportIntegrationVariableValue(IntegrationId, id, 99d);

		Assert.Multiple(() =>
		{
			Assert.That(renamed.Error,
				Is.EqualTo(VariableError.NotEditable),
				"a writable value does not make the name the user's to change");
			Assert.That(reportedByAnother.Error, Is.EqualTo(VariableError.NotOwnedByIntegration));
			Assert.That(reportedByOwner.Success, Is.True);
			Assert.That(_registry.GetById(id)!.Value, Is.EqualTo("99"));
		});
	}

	private async Task<Guid> ProviderVariable(
		VariableWriteCapability? write,
		double? min = null,
		double? max = null)
	{
		var created = await _service.CreateIntegrationVariable(IntegrationId,
			"volume",
			VariableScope.Global,
			null,
			DomainVariableType.Numeric,
			10,
			0,
			"volume",
			new VariableDeclaration { Write = write });

		var entity = created.Data!;
		entity.Min = min;
		entity.Max = max;
		_registry.Upsert(entity);
		_mediator.Published.Clear();
		return entity.Id;
	}

	private sealed class NullUserVariableStore : IUserVariableStore
	{
		public IReadOnlyList<VariableEntity> Load() => [];

		public void Save(IEnumerable<VariableEntity> userVariables)
		{
		}
	}
}
