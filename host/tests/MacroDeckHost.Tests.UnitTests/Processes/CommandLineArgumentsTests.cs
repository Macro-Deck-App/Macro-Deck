using System.Diagnostics;
using MacroDeckHost.Platform.Processes;

namespace MacroDeckHost.Tests.UnitTests.Processes;

[TestFixture]
[Platform(Exclude = "Win")]
public class CommandLineArgumentsTests
{
	[TestCase("a b c")]
	[TestCase("\"two words\" plain")]
	[TestCase("\"embedded \\\"quote\\\"\" x")]
	[TestCase("back\\slash path\\")]
	[TestCase("trailing\\\\\"quoted arg\"")]
	[TestCase("\\\\\\\"odd")]
	[TestCase("\"a\"\"b\" c")]
	[TestCase("  leading   and\ttabs  ")]
	[TestCase("\"\"")]
	[TestCase("unterminated \"quote here")]
	[TestCase("a\"b c\"d")]
	[TestCase("--profile=\"My Profile\" -- -x")]
	public async Task Arguments_split_like_Process_Start(string arguments)
	{
		using var printf = Process.Start(new ProcessStartInfo("printf")
		{
			Arguments = "%s\\n " + arguments,
			RedirectStandardOutput = true,
			UseShellExecute = false
		})!;
		var output = await printf.StandardOutput.ReadToEndAsync();
		await printf.WaitForExitAsync();
		var expected = output.Split('\n')[..^1];

		Assert.That(CommandLineArguments.Split(arguments), Is.EqualTo(expected));
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("   \t ")]
	public void Blank_arguments_give_no_argument(string? arguments)
	{
		Assert.That(CommandLineArguments.Split(arguments), Is.Empty);
	}
}
