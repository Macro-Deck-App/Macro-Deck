using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Integrations.Variables;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class WriteVariableToFileActionTests
{
	private string _folder = null!;
	private Dictionary<(string Name, string? Widget), HostVariableValue> _variables = null!;

	[SetUp]
	public void SetUp()
	{
		_folder = Path.Combine(Path.GetTempPath(), "macro-deck-write-action-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_folder);
		_variables = [];
	}

	[TearDown]
	public void TearDown() => Directory.Delete(_folder, true);

	[Test]
	public async Task It_writes_the_current_value_of_any_variable_and_replaces_the_file()
	{
		var path = Path.Combine(_folder, "cpu.txt");
		await File.WriteAllTextAsync(path, "older and longer content");
		_variables[("system_cpu_load", null)] = new HostVariableValue("12.5", true);

		var result = await Execute("system_cpu_load", path);

		Assert.Multiple(async () =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo("12.5"));
		});
	}

	[Test]
	public async Task The_widget_running_the_action_decides_which_variable_is_read()
	{
		var path = Path.Combine(_folder, "count.txt");
		_variables[("count", null)] = new HostVariableValue("1", true);
		_variables[("count", "widget-1")] = new HostVariableValue("10", true);

		await Execute("count", path, ownerWidgetId: "widget-1");

		Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo("10"));
	}

	[Test]
	public async Task An_unknown_variable_is_not_found_and_writes_nothing()
	{
		var path = Path.Combine(_folder, "missing.txt");

		var result = await Execute("missing", path);

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
			Assert.That(File.Exists(path), Is.False);
		});
	}

	[Test]
	public async Task A_variable_without_a_value_right_now_writes_nothing()
	{
		var path = Path.Combine(_folder, "stale.txt");
		_variables[("weather", null)] = new HostVariableValue("old", false);

		var result = await Execute("weather", path);

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
			Assert.That(File.Exists(path), Is.False);
		});
	}

	[Test]
	public async Task A_relative_path_is_refused()
	{
		_variables[("count", null)] = new HostVariableValue("1", true);

		var result = await Execute("count", "count.txt");

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
	}

	[Test]
	public async Task A_missing_folder_fails_instead_of_being_created()
	{
		var path = Path.Combine(_folder, "not-there", "count.txt");
		_variables[("count", null)] = new HostVariableValue("1", true);

		var result = await Execute("count", path);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(Directory.Exists(Path.GetDirectoryName(path)), Is.False);
		});
	}

	[Test]
	public async Task Without_the_host_lookup_the_action_reports_variables_unavailable()
	{
		var executor = new WriteVariableToFileActionDefinition(() => null).CreateExecutor();

		var result = await executor.ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["variable"] = "count", ["file"] = Path.Combine(_folder, "x") }
		});

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
	}

	private Task<ActionResult> Execute(string variable, string path, string? ownerWidgetId = null)
	{
		VariableReader reader = (name, widget, _) =>
			Task.FromResult(_variables.TryGetValue((name, widget), out var value) ||
				_variables.TryGetValue((name, null), out value)
					? value
					: null);
		var executor = new WriteVariableToFileActionDefinition(() => reader).CreateExecutor();
		return executor.ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["variable"] = variable, ["file"] = path },
			OwnerWidgetId = ownerWidgetId
		});
	}
}
