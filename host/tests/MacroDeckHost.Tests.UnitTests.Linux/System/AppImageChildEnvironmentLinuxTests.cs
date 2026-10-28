using System.Runtime.Versioning;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.System.Actions;
using MacroDeckHost.Platform.Applications;
using MacroDeckHost.Platform.Processes;

namespace MacroDeckHost.Tests.UnitTests.Linux.System;

// The fake libc.so.6 stands in for a bundled library a system program cannot load: through
// LD_LIBRARY_PATH it makes the loader exit 127, as the real bundle does for curl or gio.
[NonParallelizable]
[Platform("Linux")]
[SupportedOSPlatform("linux")]
public class AppImageChildEnvironmentLinuxTests
{
	private readonly Dictionary<string, string?> _original = new(StringComparer.Ordinal);
	private string _root = null!;
	private string _appDir = null!;

	[SetUp]
	public void SetUp()
	{
		_root = Directory.CreateTempSubdirectory("md-appimage-child-").FullName;
		_appDir = Path.Combine(_root, "mount");
		var bundledLibraries = Directory.CreateDirectory(Path.Combine(_appDir, "usr", "lib")).FullName;
		Directory.CreateDirectory(Path.Combine(_appDir, "usr", "bin"));
		File.WriteAllText(Path.Combine(bundledLibraries, "libc.so.6"), "not a library\n");

		var path = Environment.GetEnvironmentVariable("PATH");
		var libraryPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
		Set("APPDIR", _appDir);
		Set("APPIMAGE", Path.Combine(_root, "Macro_Deck.AppImage"));
		Set("PATH", $"{_appDir}/usr/bin/:{_appDir}/usr/sbin/:{path}");
		Set("LD_LIBRARY_PATH", $"{_appDir}/usr/lib/:{_appDir}/usr/lib/x86_64-linux-gnu/:{_appDir}/lib/:{libraryPath}");
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

	[TestCase("default")]
	[TestCase("bash")]
	public async Task Run_command_runs_a_plain_command_inside_an_AppImage(string shell)
	{
		var action = new RunCommandActionDefinition(new VariableApiAccessor());

		var result = await action.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object>
				{
					["shell"] = shell,
					["command"] = "echo hi",
					["timeout"] = 30
				}
			});

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded), result.ErrorCode);
	}

	[Test]
	public async Task Launch_application_starts_a_program_inside_an_AppImage()
	{
		var report = Path.Combine(_root, "report");
		var program = Path.Combine(_root, "program");
		File.WriteAllText(program, $"#!/bin/sh\necho started > '{report}'\n");
		File.SetUnixFileMode(program, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

		await ApplicationServiceFactory.Create(Serilog.Core.Logger.None)
			.LaunchAsync(program, null, null, LaunchMode.Start, false);

		for (var attempt = 0; attempt < 100 && !File.Exists(report); attempt++)
		{
			await Task.Delay(100);
		}

		Assert.That(File.Exists(report), Is.True, "the program never ran");
	}

	[Test]
	public async Task A_system_tool_the_host_runs_starts_inside_an_AppImage()
	{
		var result = await ProcessRunner.RunWithResultAsync("sh", ["-c", "echo hi"]);

		Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
	}

	private void Set(string name, string value)
	{
		_original.TryAdd(name, Environment.GetEnvironmentVariable(name));
		Environment.SetEnvironmentVariable(name, value);
	}
}
