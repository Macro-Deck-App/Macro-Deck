using MacroDeck.Sdk;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Widgets;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Variables;
using Microsoft.Extensions.DependencyInjection;
using DomainVariableType = MacroDeckHost.Domain.Enums.VariableType;
using SdkVariableType = MacroDeck.Sdk.Variables.VariableType;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class UserVariableWriterTests
{
	private const string WidgetId = "11111111-1111-1111-1111-111111111111";
	private const string OtherWidgetId = "22222222-2222-2222-2222-222222222222";

	private VariableService _service = null!;
	private ServiceProvider _provider = null!;
	private UserVariableWriter _writer = null!;

	[SetUp]
	public void SetUp()
	{
		_service = TestVariableServices.Create(new VariableRegistry(),
			new NullUserVariableStore(),
			new RecordingMediator());
		_provider = new ServiceCollection()
			.AddScoped<IVariableService>(_ => _service)
			.AddScoped<IWidgetAppearanceService>(_ => new StubWidgetAppearanceService(WidgetId, OtherWidgetId))
			.BuildServiceProvider();
		_writer = new UserVariableWriter(_provider.GetRequiredService<IServiceScopeFactory>());
	}

	[TearDown]
	public void TearDown()
	{
		_writer.Dispose();
		_provider.Dispose();
	}

	[Test]
	public async Task Set_replaces_the_value()
	{
		await CreateUserVariable("greeting", DomainVariableType.Text, "hello");

		var result = await Apply("greeting", UserVariableOperation.Set, "bye");

		Assert.That(result.Status, Is.EqualTo(UserVariableWriteStatus.Applied));
		Assert.That(await ValueOf("greeting"), Is.EqualTo("bye"));
	}

	[Test]
	public async Task Set_with_no_value_clears_a_text_variable()
	{
		await CreateUserVariable("greeting", DomainVariableType.Text, "hello");

		var result = await Apply("greeting", UserVariableOperation.Set, null);

		Assert.That(result.Status, Is.EqualTo(UserVariableWriteStatus.Applied));
		Assert.That(await ValueOf("greeting"), Is.Empty);
	}

	[Test]
	public async Task Set_rejects_a_value_that_does_not_fit_a_numeric_variable()
	{
		await CreateUserVariable("count", DomainVariableType.Numeric, 5);

		var result = await Apply("count", UserVariableOperation.Set, "many");

		Assert.That(result.Status, Is.EqualTo(UserVariableWriteStatus.InvalidValue));
		Assert.That(await ValueOf("count"), Is.EqualTo("5"));
	}

	[Test]
	public async Task Set_rejects_a_value_that_does_not_fit_a_boolean_variable()
	{
		await CreateUserVariable("armed", DomainVariableType.Boolean, true);

		var result = await Apply("armed", UserVariableOperation.Set, "yes");

		Assert.That(result.Status, Is.EqualTo(UserVariableWriteStatus.InvalidValue));
		Assert.That(await ValueOf("armed"), Is.EqualTo("true"));
	}

	[TestCase("false", "false")]
	[TestCase("TRUE", "true")]
	[TestCase("0", "false")]
	public async Task Set_accepts_the_spellings_of_a_boolean(string typed, string expected)
	{
		await CreateUserVariable("armed", DomainVariableType.Boolean, true);

		var result = await Apply("armed", UserVariableOperation.Set, typed);

		Assert.That(result.Status, Is.EqualTo(UserVariableWriteStatus.Applied));
		Assert.That(await ValueOf("armed"), Is.EqualTo(expected));
	}

	[Test]
	public async Task Set_writes_a_number_parsed_invariantly()
	{
		await CreateUserVariable("ratio", DomainVariableType.Numeric, 0);

		await Apply("ratio", UserVariableOperation.Set, "3.5");

		Assert.That(await ValueOf("ratio"), Is.EqualTo("3.5"));
	}

	[Test]
	public async Task Concurrent_adds_do_not_lose_an_increment()
	{
		await CreateUserVariable("count", DomainVariableType.Numeric, 0);

		await Task.WhenAll(Enumerable.Range(0, 20)
			.Select(_ => Apply("count", UserVariableOperation.Add, "1")));

		Assert.That(await ValueOf("count"), Is.EqualTo("20"));
	}

	[Test]
	public async Task Add_increments_and_a_negative_value_subtracts()
	{
		await CreateUserVariable("count", DomainVariableType.Numeric, 5);

		await Apply("count", UserVariableOperation.Add, "3");
		await Apply("count", UserVariableOperation.Add, "-4");

		Assert.That(await ValueOf("count"), Is.EqualTo("4"));
	}

	[Test]
	public async Task Add_rejects_a_value_that_is_not_a_number()
	{
		await CreateUserVariable("count", DomainVariableType.Numeric, 5);

		var result = await Apply("count", UserVariableOperation.Add, "many");

		Assert.That(result.Status, Is.EqualTo(UserVariableWriteStatus.InvalidValue));
		Assert.That(await ValueOf("count"), Is.EqualTo("5"));
	}

	[Test]
	public async Task Add_rejects_a_variable_that_is_not_numeric()
	{
		await CreateUserVariable("greeting", DomainVariableType.Text, "hello");

		var result = await Apply("greeting", UserVariableOperation.Add, "1");

		Assert.That(result.Status, Is.EqualTo(UserVariableWriteStatus.InvalidValue));
		Assert.That(await ValueOf("greeting"), Is.EqualTo("hello"));
	}

	[Test]
	public async Task Toggle_flips_a_boolean_and_ignores_the_value()
	{
		await CreateUserVariable("armed", DomainVariableType.Boolean, false);

		await Apply("armed", UserVariableOperation.Toggle, "whatever");
		Assert.That(await ValueOf("armed"), Is.EqualTo("true"));

		await Apply("armed", UserVariableOperation.Toggle, null);
		Assert.That(await ValueOf("armed"), Is.EqualTo("false"));
	}

	[Test]
	public async Task Toggle_rejects_a_variable_that_is_not_boolean()
	{
		await CreateUserVariable("count", DomainVariableType.Numeric, 1);

		var result = await Apply("count", UserVariableOperation.Toggle, null);

		Assert.That(result.Status, Is.EqualTo(UserVariableWriteStatus.InvalidValue));
	}

	[Test]
	public async Task Append_concatenates_to_a_text_variable()
	{
		await CreateUserVariable("log", DomainVariableType.Text, "a");

		await Apply("log", UserVariableOperation.Append, "b");

		Assert.That(await ValueOf("log"), Is.EqualTo("ab"));
	}

	[Test]
	public async Task Append_rejects_a_variable_that_is_not_text()
	{
		await CreateUserVariable("count", DomainVariableType.Numeric, 7);

		var result = await Apply("count", UserVariableOperation.Append, "8");

		Assert.That(result.Status, Is.EqualTo(UserVariableWriteStatus.InvalidValue));
		Assert.That(await ValueOf("count"), Is.EqualTo("7"));
	}

	[Test]
	public async Task An_unknown_name_is_not_found()
	{
		var result = await Apply("nope", UserVariableOperation.Set, "x");

		Assert.That(result.Status, Is.EqualTo(UserVariableWriteStatus.NotFound));
	}

	[Test]
	public async Task A_name_that_can_never_be_a_variable_is_not_found()
	{
		var result = await Apply("   ", UserVariableOperation.Set, "x");

		Assert.That(result.Status, Is.EqualTo(UserVariableWriteStatus.NotFound));
	}

	[TestCase("vars.greeting")]
	[TestCase("Greeting")]
	public async Task A_non_canonical_name_still_resolves(string typed)
	{
		await CreateUserVariable("greeting", DomainVariableType.Text, "hello");

		var result = await Apply(typed, UserVariableOperation.Set, "bye");

		Assert.That(result.Status, Is.EqualTo(UserVariableWriteStatus.Applied));
		Assert.That(await ValueOf("greeting"), Is.EqualTo("bye"));
	}

	[Test]
	public async Task An_integration_variable_is_not_editable()
	{
		await _service.CreateIntegrationVariable("int1",
			"track",
			VariableScope.Global,
			null,
			DomainVariableType.Text,
			"song",
			null);

		var result = await Apply("track", UserVariableOperation.Set, "other");

		Assert.That(result.Status, Is.EqualTo(UserVariableWriteStatus.NotEditable));
		Assert.That(await ValueOf("track"), Is.EqualTo("song"));
	}

	[Test]
	public async Task A_widget_variable_is_not_editable()
	{
		await _service.UpsertWidgetVariable(VariableScope.Widget,
			WidgetId,
			"toggled",
			DomainVariableType.Boolean,
			false);

		var result = await _writer.ApplyAsync("toggled", WidgetId, UserVariableOperation.Toggle, null);

		Assert.That(result.Status, Is.EqualTo(UserVariableWriteStatus.NotEditable));
	}

	[Test]
	public async Task A_button_scoped_variable_wins_over_a_global_of_the_same_name()
	{
		await CreateUserVariable("count", DomainVariableType.Numeric, 1);
		await _service.CreateUserVariable("count",
			VariableScope.Widget,
			WidgetId,
			DomainVariableType.Numeric,
			10,
			null);

		await _writer.ApplyAsync("count", WidgetId, UserVariableOperation.Add, "1");

		Assert.That(await ValueOf("count"), Is.EqualTo("1"), "the global one is untouched");
		Assert.That(await ValueOf("count", WidgetId), Is.EqualTo("11"));
	}

	[Test]
	public async Task Without_an_owner_widget_the_global_variable_is_written()
	{
		await CreateUserVariable("count", DomainVariableType.Numeric, 1);
		await _service.CreateUserVariable("count",
			VariableScope.Widget,
			WidgetId,
			DomainVariableType.Numeric,
			10,
			null);

		await Apply("count", UserVariableOperation.Add, "1");

		Assert.That(await ValueOf("count"), Is.EqualTo("2"));
		Assert.That(await ValueOf("count", WidgetId), Is.EqualTo("10"));
	}

	private Task<UserVariableWriteResult> Apply(string name, UserVariableOperation operation, string? value)
		=> _writer.ApplyAsync(name, null, operation, value);

	private Task<Result<VariableEntity, VariableError>> CreateUserVariable(
		string name,
		DomainVariableType type,
		object? value)
		=> _service.CreateUserVariable(name, VariableScope.Global, null, type, value, null);

	private async Task<string> ValueOf(string name, string? widgetId = null)
	{
		var entity = widgetId is null
			? await _service.Resolve(name, VariableScope.Global, null)
			: await _service.Resolve(name, VariableScope.Widget, widgetId);
		return entity!.Value;
	}

	private sealed class NullUserVariableStore : IUserVariableStore
	{
		public IReadOnlyList<VariableEntity> Load() => [];

		public void Save(IEnumerable<VariableEntity> userVariables)
		{
		}
	}

	[Test]
	public async Task Create_makes_a_global_variable_that_apply_can_then_write()
	{
		var created = await _writer.CreateAsync("greeting", null, SdkVariableType.Text, "hi");

		var applied = await Apply("greeting", UserVariableOperation.Append, " there");

		Assert.Multiple(async () =>
		{
			Assert.That(created.Status, Is.EqualTo(UserVariableCreateStatus.Created));
			Assert.That(applied.Status, Is.EqualTo(UserVariableWriteStatus.Applied));
			Assert.That(await ValueOf("greeting"), Is.EqualTo("hi there"));
		});
	}

	[Test]
	public async Task Create_with_an_owner_widget_scopes_the_variable_to_that_widget_alone()
	{
		var created = await _writer.CreateAsync("count", WidgetId, SdkVariableType.Numeric, "1");

		Assert.Multiple(async () =>
		{
			Assert.That(created.Status, Is.EqualTo(UserVariableCreateStatus.Created));
			Assert.That(await _service.Resolve("count", VariableScope.Widget, WidgetId), Is.Not.Null);

			// Both of these fail for an implementation that ignores the owner and creates a global,
			// which the widget-context lookup above would still find by falling back to it.
			Assert.That(await _service.Resolve("count", VariableScope.Global, null), Is.Null);
			Assert.That(await _service.Resolve("count", VariableScope.Widget, OtherWidgetId), Is.Null);
		});
	}

	[Test]
	public async Task Create_under_a_widget_that_does_not_exist_is_refused_and_stores_nothing()
	{
		var result = await _writer.CreateAsync("count",
			"99999999-9999-9999-9999-999999999999",
			SdkVariableType.Numeric,
			"1");

		Assert.Multiple(async () =>
		{
			Assert.That(result.Status, Is.EqualTo(UserVariableCreateStatus.UnknownWidget));
			Assert.That(await _service.GetAll(), Has.None.Matches<VariableEntity>(v => v.Name == "count"));
		});
	}

	[Test]
	public async Task Create_under_an_owner_widget_id_that_is_not_a_widget_id_is_refused()
	{
		var result = await _writer.CreateAsync("count", "not-a-widget-id", SdkVariableType.Numeric, "1");

		Assert.Multiple(async () =>
		{
			Assert.That(result.Status, Is.EqualTo(UserVariableCreateStatus.UnknownWidget));
			Assert.That(await _service.GetAll(), Has.None.Matches<VariableEntity>(v => v.Name == "count"));
		});
	}

	[Test]
	public async Task Create_rejects_a_duplicate_in_the_same_scope_but_allows_a_widget_local_shadow()
	{
		await CreateUserVariable("greeting", DomainVariableType.Text, "hello");

		var duplicate = await _writer.CreateAsync("greeting", null, SdkVariableType.Text, "x");
		var shadow = await _writer.CreateAsync("greeting", WidgetId, SdkVariableType.Text, "x");

		Assert.Multiple(async () =>
		{
			Assert.That(duplicate.Status, Is.EqualTo(UserVariableCreateStatus.AlreadyExists));
			Assert.That(await ValueOf("greeting"), Is.EqualTo("hello"));
			Assert.That(shadow.Status, Is.EqualTo(UserVariableCreateStatus.Created));
		});
	}

	[Test]
	public async Task Create_rejects_an_initial_value_that_is_not_the_declared_type()
	{
		var result = await _writer.CreateAsync("count", null, SdkVariableType.Numeric, "many");

		Assert.Multiple(async () =>
		{
			Assert.That(result.Status, Is.EqualTo(UserVariableCreateStatus.InvalidValue));
			Assert.That(await _service.GetAll(), Has.None.Matches<VariableEntity>(v => v.Name == "count"));
		});
	}

	[Test]
	public async Task A_created_widget_scoped_variable_shadows_a_created_global_when_written()
	{
		await _writer.CreateAsync("counter", null, SdkVariableType.Numeric, "1");
		await _writer.CreateAsync("counter", WidgetId, SdkVariableType.Numeric, "10");

		var result = await _writer.ApplyAsync("counter", WidgetId, UserVariableOperation.Add, "1");

		var scoped = await _service.Resolve("counter", VariableScope.Widget, WidgetId);
		var global = await _service.Resolve("counter", VariableScope.Global, null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(UserVariableWriteStatus.Applied));
			Assert.That(scoped!.Value, Is.EqualTo("11"));
			Assert.That(global!.Value, Is.EqualTo("1"));
		});
	}

	/// <summary>
	/// The gate serializes resolve-compute-write so two flows cannot lose an increment, and it is
	/// process-wide. A provider is arbitrary code that may write another variable back through this same
	/// API while it is applying one, so a dispatch that still held the gate would deadlock the host - not
	/// fail, hang - and the deadlock would only ever show up against a real integration.
	/// </summary>
	[Test]
	public async Task A_provider_writing_back_through_this_api_while_applying_does_not_deadlock()
	{
		var provider = new ReentrantWriteProvider();
		var service = TestVariableServices.Create(new VariableRegistry(),
			new NullUserVariableStore(),
			new RecordingMediator(),
			new ConfigurableIntegrationRegistry([provider]));
		await using var provider2 = new ServiceCollection()
			.AddScoped<IVariableService>(_ => service)
			.AddScoped<IWidgetAppearanceService>(_ => new StubWidgetAppearanceService(WidgetId))
			.BuildServiceProvider();
		using var writer = new UserVariableWriter(provider2.GetRequiredService<IServiceScopeFactory>());
		provider.Writer = writer;

		await service.CreateUserVariable("log", VariableScope.Global, null, DomainVariableType.Text, "", null);
		await service.CreateIntegrationVariable(ReentrantWriteProvider.IntegrationId,
			"gain",
			VariableScope.Global,
			null,
			DomainVariableType.Numeric,
			1,
			0,
			"gain",
			new VariableDeclaration { Write = new VariableWriteCapability() });

		var applied = await writer.ApplyAsync("gain", null, UserVariableOperation.Set, "5")
			.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(async () =>
		{
			Assert.That(applied.Status, Is.EqualTo(UserVariableWriteStatus.Applied));
			Assert.That(provider.Applied, Is.EqualTo(5m));
			Assert.That((await service.Resolve("log", VariableScope.Global, null))!.Value, Is.EqualTo("written"));
		});
	}

	private sealed class ReentrantWriteProvider : IIntegration, IVariableProvider
	{
		internal const string IntegrationId = "reentrant";

		public UserVariableWriter? Writer { get; set; }

		public object? Applied { get; private set; }

		public string Id => IntegrationId;

		public MacroDeck.Localization.LocalizedText Name => "Reentrant";

		public string Version => "1.0.0";

		public IReadOnlyList<MacroDeck.Sdk.Actions.IActionDefinition> Actions => [];

		public bool IsInitialized { get; private set; } = true;

		public IReadOnlyList<VariableDefinition> Variables =>
		[
			VariableDefinition.Eager("gain", SdkVariableType.Numeric) with
			{
				Id = "gain", Write = new VariableWriteCapability()
			}
		];

		public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult(VariableReading.Unavailable);

		public async ValueTask<VariableWriteResult> SetValueAsync(
			string localId,
			object? value,
			CancellationToken cancellationToken = default)
		{
			Applied = value;
			await Writer!.ApplyAsync("log", null, UserVariableOperation.Set, "written", cancellationToken);
			return VariableWriteResult.Applied();
		}

		public Task InitializeAsync(IIntegrationContext context)
		{
			IsInitialized = true;
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			IsInitialized = false;
			return Task.CompletedTask;
		}
	}

	private sealed class StubWidgetAppearanceService : IWidgetAppearanceService
	{
		private readonly HashSet<string> _widgets;

		public StubWidgetAppearanceService(params string[] widgetIds) => _widgets = [.. widgetIds];

		public IReadOnlyList<WidgetTargetInfo> GetWidgets() => [];

		public bool Exists(string widgetId) => _widgets.Contains(widgetId);

		public Task<bool> ApplyAsync(WidgetAppearanceRequest request, CancellationToken cancellationToken = default)
			=> Task.FromResult(false);
	}
}
