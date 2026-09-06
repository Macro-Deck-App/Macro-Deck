using MacroDeck.Plugin.Packaging.Artifacts;

namespace MacroDeck.Plugin.Packaging.Tests.UnitTests.Artifacts;

[TestFixture]
internal sealed class PluginArtifactEntryPolicyTests
{
	private static readonly string _target =
		Path.Combine(Path.GetTempPath(), "md-plugin-entry-policy", "staging");

	private static PluginArtifactEntryDecision Resolve(string? entryName)
	{
		return PluginArtifactEntryPolicy.Resolve(entryName, _target);
	}

	[TestCase("../evil.txt")]
	[TestCase("../../etc/passwd")]
	[TestCase("a/../../evil.txt")]
	public void An_entry_escaping_the_target_directory_is_rejected(string entryName)
	{
		var decision = Resolve(entryName);

		Assert.Multiple(() =>
		{
			Assert.That(decision.Allowed, Is.False);
			Assert.That(decision.Rejection, Is.EqualTo(PluginArtifactEntryRejection.PathTraversal));
			Assert.That(decision.ResolvedPath, Is.Null);
		});
	}

	/// <summary>
	/// A parent segment is refused even when it would resolve back inside, matching the entrypoint path
	/// rule the supervisor's manifest reader already applies. There is no legitimate artifact that needs
	/// one, and a rule with no exceptions is one nobody has to audit.
	/// </summary>
	[Test]
	public void A_parent_segment_is_rejected_even_when_it_resolves_back_inside()
	{
		var decision = Resolve("lib/../lib/plugin.dll");

		Assert.Multiple(() =>
		{
			Assert.That(decision.Allowed, Is.False);
			Assert.That(decision.Rejection, Is.EqualTo(PluginArtifactEntryRejection.PathTraversal));
		});
	}

	/// <summary>
	/// A containment check written as a plain string prefix, without requiring a separator, resolves this
	/// to a sibling directory whose name starts with the target's and lets it through.
	/// </summary>
	[Test]
	public void A_sibling_directory_whose_name_extends_the_target_is_rejected()
	{
		var decision = PluginArtifactEntryPolicy.Resolve("../staging-evil/x.txt", _target);

		Assert.That(decision.Allowed, Is.False);
	}

	[TestCase("/etc/cron.d/x")]
	[TestCase("C:\\Windows\\System32\\x.dll")]
	[TestCase("c:/windows/x.dll")]
	public void An_absolute_or_drive_qualified_entry_is_rejected(string entryName)
	{
		var decision = Resolve(entryName);

		Assert.Multiple(() =>
		{
			Assert.That(decision.Allowed, Is.False);
			Assert.That(decision.Rejection, Is.EqualTo(PluginArtifactEntryRejection.AbsolutePath));
		});
	}

	/// <summary>
	/// On Linux a backslash is an ordinary filename character, so an implementation that leans on
	/// <c>Path</c> alone treats this as one harmless file name and writes it - while the same artifact
	/// escapes on Windows. The separator has to be normalised before judging, not after.
	/// </summary>
	[Test]
	public void A_backslash_separated_traversal_is_rejected_on_every_platform()
	{
		var decision = Resolve("..\\..\\evil.txt");

		Assert.That(decision.Allowed, Is.False);
	}

	[Test]
	public void A_backslash_separated_relative_entry_resolves_inside_the_target()
	{
		var decision = Resolve("lib\\plugin.dll");

		Assert.Multiple(() =>
		{
			Assert.That(decision.Allowed, Is.True);
			Assert.That(decision.ResolvedPath,
				Is.EqualTo(Path.Combine(Path.GetFullPath(_target), "lib", "plugin.dll")));
		});
	}

	[Test]
	public void An_ordinary_nested_entry_resolves_under_the_target()
	{
		var decision = Resolve("runtimes/osx-arm64/native/lib.dylib");

		Assert.Multiple(() =>
		{
			Assert.That(decision.Allowed, Is.True);
			Assert.That(decision.ResolvedPath,
				Is.EqualTo(Path.Combine(Path.GetFullPath(_target),
					"runtimes",
					"osx-arm64",
					"native",
					"lib.dylib")));
		});
	}

	[TestCase("CON")]
	[TestCase("nul.txt")]
	[TestCase("lib/COM1.dll")]
	[TestCase("LPT9")]
	public void A_reserved_device_name_is_rejected(string entryName)
	{
		var decision = Resolve(entryName);

		Assert.Multiple(() =>
		{
			Assert.That(decision.Allowed, Is.False);
			Assert.That(decision.Rejection, Is.EqualTo(PluginArtifactEntryRejection.IllegalSegment));
		});
	}

	[TestCase("")]
	[TestCase("   ")]
	[TestCase(null)]
	public void An_empty_entry_name_is_rejected(string? entryName)
	{
		Assert.That(Resolve(entryName).Allowed, Is.False);
	}

	[Test]
	public void An_entry_name_containing_a_nul_is_rejected()
	{
		var decision = Resolve("plugin\0.dll");

		Assert.Multiple(() =>
		{
			Assert.That(decision.Allowed, Is.False);
			Assert.That(decision.Rejection, Is.EqualTo(PluginArtifactEntryRejection.MalformedName));
		});
	}

	[Test]
	public void A_path_at_the_length_limit_is_allowed_and_one_over_is_rejected()
	{
		var atLimit = new string('a', 200);
		var overLimit = new string('a', 201);

		Assert.Multiple(() =>
		{
			Assert.That(Resolve(atLimit).Allowed, Is.True);
			Assert.That(Resolve(overLimit).Allowed, Is.False);
			Assert.That(Resolve(overLimit).Rejection,
				Is.EqualTo(PluginArtifactEntryRejection.PathTooLong));
		});
	}

	[Test]
	public void A_path_at_the_depth_limit_is_allowed_and_one_over_is_rejected()
	{
		var atLimit = string.Join('/', Enumerable.Repeat("a", 32));
		var overLimit = string.Join('/', Enumerable.Repeat("a", 33));

		Assert.Multiple(() =>
		{
			Assert.That(Resolve(atLimit).Allowed, Is.True);
			Assert.That(Resolve(overLimit).Allowed, Is.False);
			Assert.That(Resolve(overLimit).Rejection,
				Is.EqualTo(PluginArtifactEntryRejection.PathTooLong));
		});
	}

	/// <summary>
	/// The high 16 bits of a ZIP entry's external attributes carry the Unix mode. A symlink extracted as
	/// a regular file is a write primitive pointing anywhere the link says, which no path check can catch
	/// because the path itself is innocent.
	/// </summary>
	[Test]
	public void A_symlink_entry_is_recognised_as_an_unsafe_file_type()
	{
		const int symlinkMode = 0xA1FF;

		Assert.That(PluginArtifactEntryPolicy.IsUnsafeFileType(symlinkMode << 16), Is.True);
	}

	[TestCase(0x81A4)]
	[TestCase(0x41ED)]
	public void A_regular_file_or_directory_entry_is_a_safe_file_type(int unixMode)
	{
		Assert.That(PluginArtifactEntryPolicy.IsUnsafeFileType(unixMode << 16), Is.False);
	}

	/// <summary>A Windows packer writes DOS attributes and no Unix mode at all, which must not be
	/// mistaken for an exotic file type.</summary>
	[Test]
	public void An_entry_with_no_unix_mode_is_treated_as_a_regular_file()
	{
		Assert.That(PluginArtifactEntryPolicy.IsUnsafeFileType(0x20), Is.False);
	}

	[TestCase(0xC000)]
	[TestCase(0x1000)]
	[TestCase(0x6000)]
	public void A_socket_fifo_or_device_entry_is_an_unsafe_file_type(int fileType)
	{
		Assert.That(PluginArtifactEntryPolicy.IsUnsafeFileType(fileType << 16), Is.True);
	}
}
