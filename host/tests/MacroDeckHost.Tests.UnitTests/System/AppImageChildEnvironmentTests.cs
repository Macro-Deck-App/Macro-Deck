using System.Diagnostics;
using MacroDeckHost.Integrations.System;
using MacroDeckHost.Integrations.System.Actions;
using MacroDeckHost.Platform.Applications;
using MacroDeckHost.Platform.Processes;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.System;

[NonParallelizable]
[Platform(Exclude = "Win")]
public class AppImageChildEnvironmentTests
{
	private const string Probe
		= "printf '%s\\n' \"${APPDIR-unset}\" \"${LD_LIBRARY_PATH-unset}\" \"$PATH\" \"${XDG_DATA_DIRS-unset}\""
		+ " \"${GTK_THEME-unset}\" \"${MD_PROBE-unset}\"";

	private readonly Dictionary<string, string?> _original = new(StringComparer.Ordinal);
	private string _root = null!;
	private string _appDir = null!;
	private string _systemPath = null!;
	private string _report = null!;

	[SetUp]
	public void SetUp()
	{
		_root = Directory.CreateTempSubdirectory("md-appimage-env-").FullName;
		_appDir = Path.Combine(_root, "mount");
		_report = Path.Combine(_root, "report");
		Directory.CreateDirectory(Path.Combine(_appDir, "usr", "bin"));
		_systemPath = Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin";

		Set("APPDIR", _appDir);
		Set("PATH", $"{_appDir}/usr/bin/:{_appDir}/usr/sbin/:{_systemPath}");
		Set("LD_LIBRARY_PATH", $"{_appDir}/usr/lib/:{_appDir}/usr/lib/x86_64-linux-gnu/:");
		Set("XDG_DATA_DIRS", $"{_appDir}/usr/share:/usr/share:");
		Set("GTK_THEME", "Adwaita:light");
		Set("MD_PROBE", "kept");
	}

	[TearDown]
	public void TearDown()
	{
		foreach (var (name, value) in _original)
		{
			Environment.SetEnvironmentVariable(name, value);
		}

		_original.Clear();
		Directory.Delete(_root, true);
	}

	[Test]
	public async Task A_command_run_without_output_capture_gets_the_environment_outside_the_bundle()
	{
		var action = new RunCommandActionDefinition(new VariableApiAccessor { Current = new RecordingVariableApi() });

		var result = await action.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object>
				{
					["shell"] = "default",
					["command"] = $"{Probe} > '{_report}'",
					["timeout"] = 30
				}
			});

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded), result.ErrorCode);
		AssertOutsideTheBundle(await File.ReadAllLinesAsync(_report));
	}

	[Test]
	public async Task A_command_run_with_output_capture_gets_the_environment_outside_the_bundle()
	{
		using var targets = new ActionVariableTargets(SystemIntegration.IntegrationId);
		var action = new RunCommandActionDefinition(new VariableApiAccessor
		{
			Current = targets.IntegrationVariables,
			UserVariables = targets.UserVariables
		});

		var result = await action.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object>
				{
					["shell"] = "default",
					["command"] = Probe,
					["outputVariable"] = "result",
					["timeout"] = 30
				}
			});

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded), result.ErrorCode);
		var output = (await targets.ValueOf("result"))?.ToString() ?? string.Empty;
		AssertOutsideTheBundle(output.TrimEnd('\n').Split('\n'));
	}

	[Test]
	public async Task A_system_tool_the_host_runs_gets_the_environment_outside_the_bundle()
	{
		var result = await ProcessRunner.RunWithResultAsync("sh", ["-c", Probe]);

		Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
		AssertOutsideTheBundle(result.StandardOutput.TrimEnd('\n').Split('\n'));
	}

	[Test]
	public void Outside_an_AppImage_a_start_info_keeps_its_environment()
	{
		Environment.SetEnvironmentVariable("APPDIR", null);
		var startInfo = new ProcessStartInfo("sh");
		var before = startInfo.Environment.ToDictionary(variable => variable.Key, variable => variable.Value);

		AppImageEnvironment.RestoreOutside(startInfo);

		Assert.That(startInfo.Environment, Is.EquivalentTo(before));
	}

	private void AssertOutsideTheBundle(string[] lines)
	{
		Assert.That(lines, Has.Length.EqualTo(6));
		Assert.Multiple(() =>
		{
			Assert.That(lines[0], Is.EqualTo("unset"), "APPDIR");
			Assert.That(lines[1], Is.EqualTo("unset"), "LD_LIBRARY_PATH");
			Assert.That(lines[2], Is.EqualTo(_systemPath), "PATH");
			Assert.That(lines[3], Is.EqualTo("/usr/share"), "XDG_DATA_DIRS");
			Assert.That(lines[4], Is.EqualTo("unset"), "GTK_THEME");
			Assert.That(lines[5], Is.EqualTo("kept"), "an unrelated variable");
		});
	}

	private void Set(string name, string value)
	{
		_original.TryAdd(name, Environment.GetEnvironmentVariable(name));
		Environment.SetEnvironmentVariable(name, value);
	}
}
