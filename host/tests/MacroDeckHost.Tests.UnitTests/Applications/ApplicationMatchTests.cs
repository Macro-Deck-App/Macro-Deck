using MacroDeckHost.Platform.Applications;

namespace MacroDeckHost.Tests.UnitTests.Applications;

public class ApplicationMatchTests
{
	private static readonly string _gamePath = Path.Combine(Path.GetTempPath(), "games", "game.exe");
	private static readonly string _otherGamePath = Path.Combine(Path.GetTempPath(), "other", "game.exe");

	private static readonly RunningProcess[] _snapshot =
	[
		new(10, "game", _gamePath),
		new(11, "game", _gamePath),
		new(12, "game", null),
		new(13, "game", _otherGamePath),
		new(14, "editor", Path.Combine(Path.GetTempPath(), "editor.exe"))
	];

	[Test]
	public void A_process_name_counts_every_process_with_that_name()
	{
		Assert.That(ApplicationMatch.FindProcessIds(_snapshot, "Game"), Is.EquivalentTo(new[] { 10, 11, 12, 13 }));
	}

	[Test]
	public void A_program_path_counts_its_own_processes_and_those_whose_path_cannot_be_read()
	{
		Assert.That(ApplicationMatch.FindProcessIds(_snapshot, _gamePath), Is.EquivalentTo(new[] { 10, 11, 12 }));
	}

	[Test]
	public void A_process_name_with_dots_matches_only_that_process()
	{
		RunningProcess[] snapshot =
		[
			new(20, "python3", "/usr/bin/python3"),
			new(21, "python3.12", "/usr/bin/python3.12"),
			new(22, "com.apple.WebKit.WebContent", null)
		];

		Assert.Multiple(() =>
		{
			Assert.That(ApplicationMatch.FindProcessIds(snapshot, "python3.12"), Is.EquivalentTo(new[] { 21 }));
			Assert.That(ApplicationMatch.FindProcessIds(snapshot, "com.apple.WebKit.WebContent"),
				Is.EquivalentTo(new[] { 22 }));
		});
	}

	[Test]
	public void A_program_file_name_matches_the_process_named_without_its_extension()
	{
		Assert.That(ApplicationMatch.FindProcessIds(_snapshot, "editor.exe"), Is.EquivalentTo(new[] { 14 }));
	}

	[Test]
	public void An_empty_application_matches_nothing()
	{
		Assert.That(ApplicationMatch.FindProcessIds(_snapshot, "  "), Is.Empty);
	}

	[Test]
	public void The_focused_application_counts_the_processes_of_the_same_program()
	{
		Assert.Multiple(() =>
		{
			Assert.That(ApplicationMatch.FindSameApplication(_snapshot, 10, _gamePath, "game"),
				Is.EquivalentTo(new[] { 10, 11, 12 }));
			Assert.That(ApplicationMatch.FindSameApplication(_snapshot, 99, null, "editor"),
				Is.EquivalentTo(new[] { 14 }));
		});
	}
}
