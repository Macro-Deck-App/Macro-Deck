using System.Text;
using MacroDeckHost.Integrations.AutoHotkey.State;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.AutoHotkey;

[TestFixture]
internal sealed class AutoHotkeyScriptStoreTests
{
	private InMemoryStorage _storage = null!;
	private AutoHotkeyScriptStore _store = null!;

	[SetUp]
	public async Task SetUp()
	{
		_storage = new InMemoryStorage();
		_store = await LoadAsync(_storage);
	}

	[TearDown]
	public void TearDown()
	{
		_store.Dispose();
	}

	[Test]
	public async Task A_script_keeps_its_id_and_variable_slug_across_renames_and_restarts()
	{
		var created = await _store.CreateAsync("Mute Discord in game", "Send \"^m\"");
		await _store.RenameAsync(created.Id, "Something else");

		using var reloaded = await LoadAsync(_storage);
		var script = reloaded.Find(created.Id);
		var source = await reloaded.ReadSourceAsync(created.Id);

		Assert.Multiple(() =>
		{
			Assert.That(script?.Name, Is.EqualTo("Something else"));
			Assert.That(script?.Slug, Is.EqualTo("mute_discord_in_game"));
			Assert.That(source.Text, Is.EqualTo("Send \"^m\""));
		});
	}

	[Test]
	public async Task An_empty_script_is_stored_as_a_non_empty_document()
	{
		var created = await _store.CreateAsync("Empty");

		Assert.That(_storage.Documents[AutoHotkeyScriptStore.SourceKey(created.Id)], Is.Not.Empty);
		Assert.That((await _store.ReadSourceAsync(created.Id)).Text, Is.Empty);
	}

	[Test]
	public async Task Slugs_fall_back_for_names_without_latin_letters_and_stay_unique()
	{
		var first = await _store.CreateAsync("音量");
		var second = await _store.CreateAsync("Same");
		var third = await _store.CreateAsync("same");

		Assert.Multiple(() =>
		{
			Assert.That(first.Slug, Is.EqualTo("s" + first.Id.ToString("N")[..8]));
			Assert.That(second.Slug, Is.EqualTo("same"));
			Assert.That(third.Slug, Is.EqualTo("same_2"));
		});
	}

	[Test]
	public async Task An_unreadable_index_makes_the_library_read_only_and_is_never_overwritten()
	{
		_storage.Put(AutoHotkeyScriptStore.IndexKey, "{ broken");
		using var store = await LoadAsync(_storage);

		Assert.Multiple(() =>
		{
			Assert.That(store.IsReadOnly, Is.True);
			Assert.ThrowsAsync<AutoHotkeyLibraryException>(() => store.CreateAsync("New"));
			Assert.That(_storage.Documents[AutoHotkeyScriptStore.IndexKey], Is.EqualTo("{ broken"));
		});
	}

	[Test]
	public async Task An_unreadable_source_makes_only_that_script_read_only()
	{
		var broken = await _store.CreateAsync("Broken", "a");
		var healthy = await _store.CreateAsync("Healthy", "b");
		_storage.MakeUnreadable(AutoHotkeyScriptStore.SourceKey(broken.Id));

		var source = await _store.ReadSourceAsync(broken.Id);
		var error = Assert.ThrowsAsync<AutoHotkeyLibraryException>(() => _store.SaveSourceAsync(broken.Id, "x"));
		await _store.SaveSourceAsync(healthy.Id, "c");
		var renamed = await _store.RenameAsync(broken.Id, "Still listed");

		Assert.Multiple(() =>
		{
			Assert.That(source.Status, Is.EqualTo(AutoHotkeySourceStatus.Unreadable));
			Assert.That(error!.Error, Is.EqualTo(AutoHotkeyLibraryError.SourceUnreadable));
			Assert.That(renamed.Name, Is.EqualTo("Still listed"));
			Assert.That(_store.IsReadOnly, Is.False);
		});
	}

	[Test]
	public async Task A_missing_source_reads_as_an_empty_writable_script()
	{
		var created = await _store.CreateAsync("Lost", "content");
		await _storage.DeleteAsync(AutoHotkeyScriptStore.SourceKey(created.Id));

		var source = await _store.ReadSourceAsync(created.Id);
		await _store.SaveSourceAsync(created.Id, "rewritten");

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(source.Status, Is.EqualTo(AutoHotkeySourceStatus.Missing));
			Assert.That(source.Text, Is.Empty);
			Assert.That((await _store.ReadSourceAsync(created.Id)).Text, Is.EqualTo("rewritten"));
		});
	}

	[Test]
	public async Task Deleting_removes_the_index_entry_and_the_source()
	{
		var created = await _store.CreateAsync("Gone", "x");

		await _store.DeleteAsync(created.Id);

		Assert.Multiple(() =>
		{
			Assert.That(_store.Find(created.Id), Is.Null);
			Assert.That(_storage.Documents.ContainsKey(AutoHotkeyScriptStore.SourceKey(created.Id)), Is.False);
		});
	}

	[Test]
	public async Task Duplicating_copies_source_and_settings_under_a_new_id()
	{
		var original = await _store.CreateAsync("Original", "body");
		await _store.UpdateSettingsAsync(original.Id, new AutoHotkeyScriptSettings("desc", true, false, null));

		var copy = await _store.DuplicateAsync(original.Id, "Original copy");

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(copy.Id, Is.Not.EqualTo(original.Id));
			Assert.That(copy.Description, Is.EqualTo("desc"));
			Assert.That(copy.IncludeHelper, Is.False);
			Assert.That(copy.StartWithMacroDeck, Is.False);
			Assert.That((await _store.ReadSourceAsync(copy.Id)).Text, Is.EqualTo("body"));
		});
	}

	[Test]
	public async Task Import_accepts_utf8_with_and_without_bom()
	{
		var withBom = await _store.ImportAsync(@"C:\scripts\With BOM.ahk",
			Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("MsgBox \"ä\"")).ToArray(),
			"fallback");
		var withoutBom = await _store.ImportAsync("plain.ahk", Encoding.UTF8.GetBytes("x := 1"), "fallback");

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(withBom.Name, Is.EqualTo("With BOM"));
			Assert.That((await _store.ReadSourceAsync(withBom.Id)).Text, Is.EqualTo("MsgBox \"ä\""));
			Assert.That(withoutBom.Name, Is.EqualTo("plain"));
		});
	}

	[Test]
	public void Import_rejects_files_over_one_mebibyte_and_invalid_utf8()
	{
		var tooLarge = Assert.ThrowsAsync<AutoHotkeyLibraryException>(() =>
			_store.ImportAsync("big.ahk", new byte[AutoHotkeyScriptStore.MaxSourceBytes + 1], "fallback"));
		var notUtf8 = Assert.ThrowsAsync<AutoHotkeyLibraryException>(() =>
			_store.ImportAsync("latin1.ahk", new byte[] { 0x61, 0xE4, 0x62 }, "fallback"));

		Assert.Multiple(() =>
		{
			Assert.That(tooLarge!.Error, Is.EqualTo(AutoHotkeyLibraryError.TooLarge));
			Assert.That(notUtf8!.Error, Is.EqualTo(AutoHotkeyLibraryError.NotUtf8));
		});
	}

	[Test]
	public async Task Export_writes_utf8_with_bom_and_never_overwrites_unasked()
	{
		var folder = Directory.CreateTempSubdirectory("ahk-export-").FullName;
		try
		{
			var created = await _store.CreateAsync("Mute: Discord?", "Send \"ö\"");

			var first = await _store.ExportAsync(created.Id, folder, false);
			var second = await _store.ExportAsync(created.Id, folder, false);
			var bytes = await File.ReadAllBytesAsync(first.FilePath);

			Assert.Multiple(() =>
			{
				Assert.That(Path.GetFileName(first.FilePath), Is.EqualTo("Mute_ Discord_.ahk"));
				Assert.That(bytes.Take(3), Is.EqualTo(Encoding.UTF8.GetPreamble()));
				Assert.That(Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), Is.EqualTo("Send \"ö\""));
				Assert.That(second.Status, Is.EqualTo(AutoHotkeyExportStatus.AlreadyExists));
			});
		}
		finally
		{
			Directory.Delete(folder, true);
		}
	}

	private static async Task<AutoHotkeyScriptStore> LoadAsync(InMemoryStorage storage)
	{
		var store = new AutoHotkeyScriptStore(storage, TimeProvider.System, Logger.None);
		await store.LoadAsync();
		return store;
	}
}
