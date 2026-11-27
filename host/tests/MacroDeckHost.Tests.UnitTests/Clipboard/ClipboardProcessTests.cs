using System.Diagnostics;
using System.Text;
using MacroDeckHost.Integrations.Clipboard.Native;

namespace MacroDeckHost.Tests.UnitTests.Clipboard;

[TestFixture]
[Platform(Exclude = "Win")]
internal sealed class ClipboardProcessTests
{
	[Test]
	public async Task A_copy_tool_that_leaves_a_selection_server_running_does_not_hold_up_the_write()
	{
		var marker = Path.Combine(Path.GetTempPath(), "macrodeck-clipboard-" + Guid.NewGuid().ToString("N"));
		var tool = new ClipboardCommand("/bin/sh", ["-c", "cat > \"$1\"; (sleep 10; echo late) &", "copy", marker]);
		var elapsed = Stopwatch.StartNew();
		try
		{
			var exitCode = await ClipboardProcess.WriteAsync(tool, "payload"u8.ToArray(), CancellationToken.None);

			Assert.Multiple(() =>
			{
				Assert.That(exitCode, Is.Zero);
				Assert.That(elapsed.Elapsed, Is.LessThan(TimeSpan.FromSeconds(4)));
				Assert.That(File.ReadAllText(marker), Is.EqualTo("payload"));
			});
		}
		finally
		{
			File.Delete(marker);
		}
	}

	[Test]
	public async Task Output_beyond_the_cap_is_reported_as_truncated_instead_of_read_to_the_end()
	{
		var endless = new ClipboardCommand("/bin/sh", ["-c", "while :; do echo clipboard; done"]);

		var result = await ClipboardProcess.ReadAsync(endless, 1000, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.IsTruncated, Is.True);
			Assert.That(result.Succeeded, Is.False);
		});
	}

	[Test]
	public async Task A_reading_tool_reports_its_output_exit_code_and_error_text()
	{
		var tool = new ClipboardCommand("/bin/sh", ["-c", "printf 'image/png'; echo 'Nothing is copied' >&2; exit 1"]);

		var result = await ClipboardProcess.ReadAsync(tool, 1000, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(Encoding.UTF8.GetString(result.Output), Is.EqualTo("image/png"));
			Assert.That(result.ExitCode, Is.EqualTo(1));
			Assert.That(result.Error, Does.Contain("Nothing is copied"));
		});
	}
}
