using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Platform.Applications;

namespace MacroDeckHost.Tests.UnitTests.Applications;

[TestFixture]
[NonParallelizable]
[Platform(Exclude = "Win")]
[UnsupportedOSPlatform("windows")]
public class LaunchedApplicationDetachmentTests
{
	private string _root = null!;
	private string _report = null!;
	private string _probe = null!;
	private int? _launched;

	[OneTimeSetUp]
	public async Task The_test_runner_does_not_already_run_on_dev_null()
	{
		using var inherits = Process.Start(new ProcessStartInfo("sh")
		{
			ArgumentList = { "-c", "[ /dev/stdout -ef /dev/null ]" }
		})!;
		await inherits.WaitForExitAsync();
		Assume.That(inherits.ExitCode, Is.Not.EqualTo(0), "the test runner's own stdout is already /dev/null");
	}

	[SetUp]
	public void SetUp()
	{
		_root = Directory.CreateTempSubdirectory("md-detach").FullName;
		_report = Path.Combine(_root, "report");
		_probe = Path.Combine(_root, "probe");
		File.WriteAllText(_probe,
			$$"""
			  #!/bin/sh
			  streams=dev-null
			  [ /dev/stdin -ef /dev/null ] && [ /dev/stdout -ef /dev/null ] && [ /dev/stderr -ef /dev/null ] || streams=inherited
			  place=elsewhere
			  [ -f ./working-directory-marker ] && place=working-directory
			  { echo "$$ $streams $place"; printf '%s\n' "$@"; } > '{{_report}}.tmp' && mv '{{_report}}.tmp' '{{_report}}'
			  exec sleep 60
			  """);
		File.SetUnixFileMode(_probe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		_launched = null;
	}

	[TearDown]
	public void TearDown()
	{
		if (_launched is { } pid)
		{
			_ = Kill(pid, 9);
		}

		Directory.Delete(_root, true);
	}

	[Test]
	public async Task A_launched_executable_runs_detached_from_the_host()
	{
		var service = ApplicationServiceFactory.Create(Serilog.Core.Logger.None);

		await service.LaunchAsync(_probe, null, null, LaunchMode.Start, false);

		AssertDetached(await ReadReport());
	}

	[Test]
	public async Task An_executable_opened_as_a_file_runs_detached_from_the_host()
	{
		var service = ApplicationServiceFactory.Create(Serilog.Core.Logger.None);

		service.OpenFile(_probe);

		AssertDetached(await ReadReport());
	}

	[Test]
	public async Task A_launched_program_still_gets_its_arguments_and_working_directory()
	{
		var workingDirectory = Directory.CreateDirectory(Path.Combine(_root, "work")).FullName;
		File.WriteAllText(Path.Combine(workingDirectory, "working-directory-marker"), string.Empty);
		var service = ApplicationServiceFactory.Create(Serilog.Core.Logger.None);

		await service.LaunchAsync(_probe, "one \"two words\" three", workingDirectory, LaunchMode.Start, false);

		var report = await ReadReport();
		Assert.Multiple(() =>
		{
			Assert.That(report.Place, Is.EqualTo("working-directory"));
			Assert.That(report.Arguments, Is.EqualTo(new[] { "one", "two words", "three" }));
		});
	}

	[Test]
	public async Task A_program_named_without_a_path_is_found_on_PATH_and_runs_detached()
	{
		var bin = Directory.CreateDirectory(Path.Combine(_root, "bin")).FullName;
		var name = $"md-probe-{Guid.NewGuid():N}";
		File.Copy(_probe, Path.Combine(bin, name));
		var path = Environment.GetEnvironmentVariable("PATH");
		Environment.SetEnvironmentVariable("PATH", $"{bin}:{path}");
		try
		{
			var service = ApplicationServiceFactory.Create(Serilog.Core.Logger.None);

			await service.LaunchAsync(name, null, null, LaunchMode.Start, false);

			AssertDetached(await ReadReport());
		}
		finally
		{
			Environment.SetEnvironmentVariable("PATH", path);
		}
	}

	private static void AssertDetached(ProbeReport report)
	{
		Assert.Multiple(() =>
		{
			Assert.That(GetSid(report.Pid), Is.EqualTo(report.Pid), "the launched program leads its own session");
			Assert.That(GetPgid(report.Pid),
				Is.Not.EqualTo(GetPgid(0)),
				"the launched program left the host's process group");
			Assert.That(report.Streams, Is.EqualTo("dev-null"), "the launched program holds none of the host's stdio");
		});
	}

	private async Task<ProbeReport> ReadReport()
	{
		for (var attempt = 0; attempt < 100 && !File.Exists(_report); attempt++)
		{
			await Task.Delay(100);
		}

		Assert.That(File.Exists(_report), Is.True, "the launched program never ran");
		var lines = await File.ReadAllLinesAsync(_report);
		var parts = lines[0].Split(' ');
		var pid = int.Parse(parts[0], global::System.Globalization.CultureInfo.InvariantCulture);
		_launched = pid;
		return new ProbeReport(pid, parts[1], parts[2], lines[1..]);
	}

	private sealed record ProbeReport(int Pid, string Streams, string Place, string[] Arguments);

	[DllImport("libc", EntryPoint = "getsid", SetLastError = true)]
	private static extern int GetSid(int pid);

	[DllImport("libc", EntryPoint = "getpgid", SetLastError = true)]
	private static extern int GetPgid(int pid);

	[DllImport("libc", EntryPoint = "kill", SetLastError = true)]
	private static extern int Kill(int pid, int signal);
}
