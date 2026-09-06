using System.Globalization;
using MacroDeckHost.Integrations.Variables;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class SetVariableActionTests
{
	private RecordingUserVariableApi _api = null!;
	private IActionExecutor _executor = null!;

	[SetUp]
	public void SetUp()
	{
		_api = new RecordingUserVariableApi();
		_executor = new SetVariableActionDefinition(() => _api).CreateExecutor();
	}

	[Test]
	public async Task It_passes_the_name_operation_value_and_owner_widget_through()
	{
		var result = await Execute(new Dictionary<string, object>
			{
				["variable"] = "count",
				["operation"] = "add",
				["value"] = "2"
			},
			ownerWidgetId: "widget-1");

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(_api.Calls, Has.Count.EqualTo(1));
		Assert.That(_api.Calls[0],
			Is.EqualTo(("count", "widget-1", UserVariableOperation.Add, "2")));
	}

	[Test]
	public async Task A_missing_operation_sets()
	{
		await Execute(new Dictionary<string, object> { ["variable"] = "count", ["value"] = "2" });

		Assert.That(_api.Calls[0].Operation, Is.EqualTo(UserVariableOperation.Set));
	}

	[Test]
	public async Task An_unknown_operation_is_an_invalid_parameter()
	{
		var result = await Execute(new Dictionary<string, object>
		{
			["variable"] = "count",
			["operation"] = "multiply"
		});

		AssertFailed(result, ActionErrorCodes.InvalidParameter);
		Assert.That(_api.Calls, Is.Empty);
	}

	[Test]
	public async Task A_blank_variable_is_an_invalid_parameter()
	{
		var result = await Execute(new Dictionary<string, object> { ["variable"] = "  " });

		AssertFailed(result, ActionErrorCodes.InvalidParameter);
		Assert.That(_api.Calls, Is.Empty);
	}

	[Test]
	public async Task A_resolved_reference_is_read_as_invariant_text()
	{
		// A { "$var": ... } operand arrives boxed as the variable's own CLR type, and the host parses
		// a number invariantly - so the machine's culture must not decide the separator.
		var culture = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = new CultureInfo("de-DE");
		try
		{
			await Execute(new Dictionary<string, object>
			{
				["variable"] = "count",
				["operation"] = "add",
				["value"] = 3.5m
			});
		}
		finally
		{
			CultureInfo.CurrentCulture = culture;
		}

		Assert.That(_api.Calls[0].Value, Is.EqualTo("3.5"));
	}

	[TestCase(UserVariableWriteStatus.NotFound, ActionErrorCodes.NotFound)]
	[TestCase(UserVariableWriteStatus.NotEditable, ActionErrorCodes.PermissionDenied)]
	[TestCase(UserVariableWriteStatus.InvalidValue, ActionErrorCodes.InvalidParameter)]
	public async Task A_write_failure_is_reported_truthfully(UserVariableWriteStatus status, string expectedCode)
	{
		_api.Result = new UserVariableWriteResult(status, "nope");

		var result = await Execute(new Dictionary<string, object> { ["variable"] = "count" });

		AssertFailed(result, expectedCode);
		Assert.That(TestLocalization.Resolve(result.ErrorMessage), Is.EqualTo("nope"));
	}

	[Test]
	public async Task Without_the_host_api_the_action_reports_unavailable()
	{
		var executor = new SetVariableActionDefinition(() => null).CreateExecutor();

		var result = await executor.ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["variable"] = "count" }
		});

		AssertFailed(result, ActionErrorCodes.Unavailable);
	}

	private Task<ActionResult> Execute(IReadOnlyDictionary<string, object> parameters, string? ownerWidgetId = null)
		=> _executor.ExecuteAsync(new ActionExecutionContext
		{
			Parameters = parameters,
			OwnerWidgetId = ownerWidgetId
		});

	private static void AssertFailed(ActionResult result, string expectedCode)
	{
		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
		Assert.That(result.ErrorCode, Is.EqualTo(expectedCode));
	}

	private sealed class RecordingUserVariableApi : IUserVariableApi
	{
		public List<(string Name, string? OwnerWidgetId, UserVariableOperation Operation, string? Value)> Calls { get; }
			= [];

		public UserVariableWriteResult Result { get; set; } = UserVariableWriteResult.Applied();

		public Task<UserVariableWriteResult> ApplyAsync(
			string name,
			string? ownerWidgetId,
			UserVariableOperation operation,
			string? value,
			CancellationToken cancellationToken = default)
		{
			Calls.Add((name, ownerWidgetId, operation, value));
			return Task.FromResult(Result);
		}
	}
}
