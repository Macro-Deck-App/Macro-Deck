using MacroDeck.Plugin.Cli.Runtime;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

[TestFixture]
public class RunWatchTests
{
	private static string MissingExecutable => Path.Combine(Path.GetTempPath(), "macrodeck-plugin-run-no-such-file");

	[Test]
	public async Task Watch_is_refused_without_a_project_to_rebuild()
	{
		var (_, error, exitCode) = await CliRunner.Run("run", "--executable", MissingExecutable, "--watch");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(2));
			Assert.That(error, Does.Contain("watch-needs-project"));
		});
	}

	[Test]
	public async Task Watch_is_refused_against_the_stub_host()
	{
		var (_, error, exitCode) = await CliRunner.Run("run", "--project", MissingExecutable, "--stub-host", "--watch");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(2));
			Assert.That(error, Does.Contain("watch-needs-real-host"));
		});
	}

	[Test]
	public void Watching_a_project_file_runs_it_under_dotnet_watch_from_its_own_directory_without_a_launch_profile()
	{
		var directory = Directory.CreateTempSubdirectory("macrodeck-plugin-watch-").FullName;
		var project = Path.Combine(directory, "MyPlugin.csproj");

		try
		{
			var launch = DotnetWatchLaunch.For(project);

			Assert.Multiple(() =>
			{
				Assert.That(Path.GetFileNameWithoutExtension(launch.FileName), Is.EqualTo("dotnet"));
				Assert.That(launch.WorkingDirectory, Is.EqualTo(directory));
				Assert.That(launch.Arguments,
					Is.EqualTo(new[] { "watch", "--non-interactive", "--project", project, "run", "--no-launch-profile" }));
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public void Watching_a_project_directory_runs_from_that_directory()
	{
		var directory = Directory.CreateTempSubdirectory("macrodeck-plugin-watch-").FullName;

		try
		{
			Assert.That(DotnetWatchLaunch.For(directory).WorkingDirectory, Is.EqualTo(directory));
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}
}
