using System.Diagnostics;
using System.Runtime.Versioning;
using MacroDeckHost.Integrations.Steam.Native;

namespace MacroDeckHost.Tests.UnitTests.Windows.Steam;

[TestFixture]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public class SteamGameProcessesTests
{
	private string _root = null!;
	private string _game = null!;
	private readonly List<Process> _started = [];

	[SetUp]
	public void SetUp()
	{
		_root = Path.Combine(Path.GetTempPath(), "md-steam-close-" + Guid.NewGuid().ToString("N"));
		_game = Path.Combine(_root, "steamapps", "common", "Some Game");
		Directory.CreateDirectory(_game);
		File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), Path.Combine(_game, "game.exe"));
	}

	[TearDown]
	public void TearDown()
	{
		foreach (var process in _started)
		{
			try
			{
				if (!process.HasExited)
				{
					process.Kill();
					process.WaitForExit(5000);
				}
			}
			catch (InvalidOperationException)
			{
			}

			process.Dispose();
		}

		try
		{
			Directory.Delete(_root, true);
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	[Test]
	public void A_game_process_is_found_by_its_folder_and_ended_only_by_force()
	{
		var game = Start(Path.Combine(_game, "game.exe"));
		var elsewhere = Start(Path.Combine(Environment.SystemDirectory, "ping.exe"));

		var found = SteamGameProcesses.FindOnWindows(_game);

		Assert.That(found.Select(process => process.Id), Is.EqualTo(new[] { game.Id }));
		var target = found.Single();
		Assert.Multiple(() =>
		{
			Assert.That(SteamGameProcesses.RequestClose(target),
				Is.False,
				"a process without a window has nothing to close");
			Assert.That(SteamGameProcesses.IsRunning(target), Is.True);
		});

		Assert.That(SteamGameProcesses.Kill(target), Is.True);
		game.WaitForExit(5000);

		Assert.Multiple(() =>
		{
			Assert.That(SteamGameProcesses.IsRunning(target), Is.False);
			Assert.That(elsewhere.HasExited, Is.False, "a process outside the game folder is left alone");
		});
	}

	[Test]
	public void A_process_id_that_was_reused_is_not_the_game_anymore()
	{
		var game = Start(Path.Combine(_game, "game.exe"));
		var stale = new SteamGameProcess(game.Id, game.StartTime.AddSeconds(-30));

		Assert.Multiple(() =>
		{
			Assert.That(SteamGameProcesses.IsRunning(stale), Is.False);
			Assert.That(SteamGameProcesses.Kill(stale), Is.False);
			Assert.That(game.HasExited, Is.False);
		});
	}

	private Process Start(string path)
	{
		var process = Process.Start(new ProcessStartInfo(path, "-n 60 127.0.0.1")
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true
		})!;
		_started.Add(process);
		return process;
	}
}
