using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Platform.Applications;

namespace MacroDeckHost.Tests.UnitTests.Linux.System;

[NonParallelizable]
[Platform("Linux")]
[SupportedOSPlatform("linux")]
public class LaunchDetachedLinuxTests
{
	private string _root = null!;
	private string _report = null!;
	private string? _path;
	private readonly List<int> _launched = [];

	[SetUp]
	public void SetUp()
	{
		_root = Directory.CreateTempSubdirectory("md-detach-linux").FullName;
		_report = Path.Combine(_root, "report");
		var bin = Directory.CreateDirectory(Path.Combine(_root, "bin")).FullName;
		WriteExecutable(Path.Combine(bin, "xdg-open"), Recorder("opener"));
		_path = Environment.GetEnvironmentVariable("PATH");
		Environment.SetEnvironmentVariable("PATH", $"{bin}:{_path}");
	}

	[TearDown]
	public void TearDown()
	{
		Environment.SetEnvironmentVariable("PATH", _path);
		foreach (var pid in _launched)
		{
			_ = Kill(pid, 9);
		}

		_launched.Clear();
		Directory.Delete(_root, true);
	}

	[Test]
	public async Task The_program_of_a_launched_desktop_entry_runs_detached_from_the_host()
	{
		var program = Path.Combine(_root, "program");
		WriteExecutable(program, Recorder("program"));
		var entry = Path.Combine(_root, "program.desktop");
		File.WriteAllText(entry, $"[Desktop Entry]\nType=Application\nName=Probe\nExec={program} --flag\n");

		await Service().LaunchAsync(entry, null, null, LaunchMode.Start, false);

		var report = await ReadReport();
		Assert.Multiple(() =>
		{
			Assert.That(report.Who, Is.EqualTo("program"));
			Assert.That(report.Arguments, Is.EqualTo(new List<string> { "--flag" }));
			AssertDetached(report);
		});
	}

	[Test]
	public async Task A_file_that_cannot_be_executed_is_handed_to_the_default_opener()
	{
		var document = Path.Combine(_root, "notes.txt");
		File.WriteAllText(document, "notes");

		Service().OpenFile(document);

		var report = await ReadReport();
		Assert.Multiple(() =>
		{
			Assert.That(report.Who, Is.EqualTo("opener"));
			Assert.That(report.Arguments, Is.EqualTo(new[] { document }));
			AssertDetached(report);
		});
	}

	[Test]
	public async Task A_document_with_the_execute_bit_is_still_handed_to_the_default_opener()
	{
		var document = Path.Combine(_root, "scan.pdf");
		WriteExecutable(document, "%PDF-1.4\n");

		Service().OpenFile(document);

		var report = await ReadReport();
		Assert.Multiple(() =>
		{
			Assert.That(report.Who, Is.EqualTo("opener"));
			Assert.That(report.Arguments, Is.EqualTo(new[] { document }));
		});
	}

	[Test]
	public async Task A_shell_script_without_an_interpreter_line_is_handed_to_the_default_opener()
	{
		var script = Path.Combine(_root, "plain.sh");
		File.WriteAllText(script, "echo hello\n");

		await Service().LaunchAsync(script, null, null, LaunchMode.Start, false);

		var report = await ReadReport();
		Assert.Multiple(() =>
		{
			Assert.That(report.Who, Is.EqualTo("opener"));
			Assert.That(report.Arguments, Is.EqualTo(new[] { script }));
		});
	}

	private static IApplicationService Service()
	{
		return ApplicationServiceFactory.Create(Serilog.Core.Logger.None);
	}

	private string Recorder(string who)
	{
		return $$"""
				 #!/bin/sh
				 streams=dev-null
				 [ /dev/stdin -ef /dev/null ] && [ /dev/stdout -ef /dev/null ] && [ /dev/stderr -ef /dev/null ] || streams=inherited
				 { echo "$$ $streams {{who}}"; printf '%s\n' "$@"; } > '{{_report}}.tmp' && mv '{{_report}}.tmp' '{{_report}}'
				 exec sleep 60
				 """;
	}

	private static void WriteExecutable(string path, string content)
	{
		File.WriteAllText(path, content);
		File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
	}

	private static void AssertDetached(Report report)
	{
		Assert.That(GetSid(report.Pid), Is.EqualTo(report.Pid), "it leads its own session");
		Assert.That(report.Streams, Is.EqualTo("dev-null"), "it holds none of the host's stdio");
	}

	private async Task<Report> ReadReport()
	{
		for (var attempt = 0; attempt < 100 && !File.Exists(_report); attempt++)
		{
			await Task.Delay(100);
		}

		Assert.That(File.Exists(_report), Is.True, "nothing was started");
		var lines = await File.ReadAllLinesAsync(_report);
		var parts = lines[0].Split(' ');
		var pid = int.Parse(parts[0], global::System.Globalization.CultureInfo.InvariantCulture);
		_launched.Add(pid);
		return new Report(pid, parts[1], parts[2], lines[1..]);
	}

	private sealed record Report(int Pid, string Streams, string Who, string[] Arguments);

	[DllImport("libc", EntryPoint = "getsid", SetLastError = true)]
	private static extern int GetSid(int pid);

	[DllImport("libc", EntryPoint = "kill", SetLastError = true)]
	private static extern int Kill(int pid, int signal);
}
