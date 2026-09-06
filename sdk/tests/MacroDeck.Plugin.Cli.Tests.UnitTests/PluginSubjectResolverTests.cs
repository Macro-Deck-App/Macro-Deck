using MacroDeck.Plugin.Cli.Runtime;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>
/// <see cref="PluginSubjectResolver" />'s <c>--project</c> resolution: the everyday "wrong folder" case
/// issue #556's D10 finding calls out (an empty error message, because the real MSBuild output was
/// captured and discarded), fixed with a fast pre-check and a pure output-combining helper - neither of
/// which needs a real <c>dotnet build</c> process to exercise.
/// </summary>
[TestFixture]
public class PluginSubjectResolverTests
{
	[Test]
	public void A_directory_with_no_project_file_is_reported_without_running_a_build()
	{
		var directory = Directory.CreateTempSubdirectory("macrodeck-plugin-cli-tests-empty-").FullName;

		try
		{
			var console = new CliConsole(Verbosity.Normal, noColor: true, new StringWriter(), new StringWriter());

			// The no-project-file check runs synchronously, before any process is ever spawned - this must
			// complete immediately, not after a build attempt.
			var ex = Assert.ThrowsAsync<PluginSubjectException>(async () =>
				await PluginSubjectResolver.ResolveProjectAsync(directory, console, CancellationToken.None));

			Assert.Multiple(() =>
			{
				Assert.That(ex!.Message, Is.EqualTo($"No project file in '{directory}'."));
				Assert.That(ex.Message, Does.Not.Contain("dotnet build"));
				Assert.That(ex.Message, Does.Not.Contain("-getProperty:TargetPath"));
				Assert.That(ex.Message, Does.Not.Contain("exited 1"));
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public async Task A_project_is_actually_built_before_its_output_is_resolved()
	{
		// --project promises to build the plugin, not merely to point at where a build would have put it:
		// on a clean checkout there is no output yet, so a resolution that only evaluates the project
		// leaves the caller with a path to a file that does not exist. This spawns a real 'dotnet build'
		// because that promise lives in MSBuild's behaviour, which is exactly what a fake would hide.
		var directory = Directory.CreateTempSubdirectory("macrodeck-plugin-cli-tests-project-").FullName;

		try
		{
			await File.WriteAllTextAsync(Path.Combine(directory, "Plugin.csproj"),
				"""
				<Project Sdk="Microsoft.NET.Sdk">
					<PropertyGroup>
						<OutputType>Exe</OutputType>
						<TargetFramework>net10.0</TargetFramework>
					</PropertyGroup>
				</Project>
				""");

			await File.WriteAllTextAsync(Path.Combine(directory, "Program.cs"), "return 0;");

			var console = new CliConsole(Verbosity.Normal, noColor: true, new StringWriter(), new StringWriter());

			await PluginSubjectResolver.ResolveProjectAsync(directory, console, CancellationToken.None);

			// Wherever the SDK chose to put it, the assembly has to exist once resolution reports success.
			Assert.That(Directory.EnumerateFiles(directory, "Plugin.dll", SearchOption.AllDirectories),
				Is.Not.Empty);
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public void A_failed_builds_own_output_is_forwarded_even_when_it_only_wrote_to_stdout()
	{
		// At -verbosity:quiet, MSBuild sends a failing build's own errors to stdout, not stderr - stderr is
		// usually empty on failure. This is the regression the pure helper exists to fix: the old message
		// forwarded stderr alone and so was empty on exactly this shape of failure.
		const string stdout = "error CS1002: ; expected [/repro/Plugin.csproj]";
		const string stderr = "";

		var combined = PluginSubjectResolver.DescribeBuildOutput(stdout, stderr);

		Assert.That(combined, Is.EqualTo(stdout));
	}

	[Test]
	public void Both_streams_empty_combine_to_no_detail_at_all()
	{
		// CliConsole.WriteError never prints a blank detail line - DescribeBuildOutput must return null,
		// not an empty string, when there is genuinely nothing to add.
		var combined = PluginSubjectResolver.DescribeBuildOutput(string.Empty, "   ");

		Assert.That(combined, Is.Null);
	}
}
