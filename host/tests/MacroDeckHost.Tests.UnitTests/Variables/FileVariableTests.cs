using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Variables.Files;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class FileVariableTests
{
	private const string WidgetId = "11111111-1111-1111-1111-111111111111";

	private static readonly string Folder = Path.Combine(Path.GetTempPath(), "macro-deck-file-variables");
	private static readonly string FilePath = Path.Combine(Folder, "value.txt");
	private static readonly string OtherFilePath = Path.Combine(Folder, "other.txt");

	private VariableRegistry _registry = null!;
	private RecordingMediator _mediator = null!;
	private FakeVariableFileSystem _files = null!;
	private FileVariableSynchronizer _synchronizer = null!;
	private CountingUserVariableStore _store = null!;
	private VariableService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_registry = new VariableRegistry();
		_mediator = new RecordingMediator();
		_files = new FakeVariableFileSystem();
		_store = new CountingUserVariableStore();
		_synchronizer = new FileVariableSynchronizer(_registry,
			_mediator,
			_files,
			Logger.None,
			TimeSpan.Zero,
			TimeSpan.Zero);
		_service = TestVariableServices.Create(_registry, _store, _mediator, files: _synchronizer);
	}

	[TearDown]
	public void TearDown() => _synchronizer.Dispose();

	[Test]
	public async Task A_new_file_variable_holds_the_files_content_without_its_trailing_line_break()
	{
		_files.SetFile(FilePath, "Hello from a script\n", notify: false);

		var created = await CreateFileVariable("status", VariableType.Text);

		Assert.Multiple(() =>
		{
			Assert.That(created.Success, Is.True);
			Assert.That(created.Data!.Value, Is.EqualTo("Hello from a script"));
			Assert.That(_registry.IsAvailable(created.Data.Id), Is.True);
			Assert.That(created.Data.CanWrite, Is.False);
		});
	}

	[Test]
	public async Task A_widget_scoped_file_variable_follows_its_file_like_a_global_one()
	{
		_files.SetFile(FilePath, "1", notify: false);
		var created = await _service.CreateUserVariable("level",
			VariableScope.Widget,
			WidgetId,
			VariableType.Numeric,
			null,
			null,
			new VariableFileSource(FilePath, false));

		await ChangeFileExternally(FilePath, "2");

		var local = await _service.Resolve("level", VariableScope.Widget, WidgetId);
		Assert.Multiple(() =>
		{
			Assert.That(local!.Id, Is.EqualTo(created.Data!.Id));
			Assert.That(local.Value, Is.EqualTo("2"));
		});
	}

	[Test]
	public async Task An_external_change_updates_the_value_and_rewriting_the_same_content_announces_nothing()
	{
		_files.SetFile(FilePath, "first", notify: false);
		var created = await CreateFileVariable("status", VariableType.Text);
		_mediator.Published.Clear();

		await ChangeFileExternally(FilePath, "second");
		await ChangeFileExternally(FilePath, "second");

		Assert.Multiple(() =>
		{
			Assert.That(_registry.GetById(created.Data!.Id)!.Value, Is.EqualTo("second"));
			Assert.That(ValueChanges(), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_file_change_is_not_saved_to_the_user_variable_file()
	{
		_files.SetFile(FilePath, "first", notify: false);
		await CreateFileVariable("status", VariableType.Text);
		var savesAfterCreate = _store.Saves;

		await ChangeFileExternally(FilePath, "second");

		Assert.That(_store.Saves, Is.EqualTo(savesAfterCreate));
	}

	[Test]
	public async Task A_missing_file_makes_the_variable_unavailable_until_the_file_appears()
	{
		var created = await CreateFileVariable("status", VariableType.Text);
		var unavailableAtFirst = !_registry.IsAvailable(created.Data!.Id);

		await ChangeFileExternally(FilePath, "now here");

		Assert.Multiple(() =>
		{
			Assert.That(unavailableAtFirst, Is.True);
			Assert.That(_registry.IsAvailable(created.Data.Id), Is.True);
			Assert.That(_registry.GetById(created.Data.Id)!.Value, Is.EqualTo("now here"));
		});
	}

	[Test]
	public async Task Content_that_does_not_fit_the_type_makes_the_variable_unavailable_and_keeps_its_value()
	{
		_files.SetFile(FilePath, "12", notify: false);
		var created = await CreateFileVariable("count", VariableType.Numeric);

		await ChangeFileExternally(FilePath, "twelve");

		Assert.Multiple(() =>
		{
			Assert.That(_registry.IsAvailable(created.Data!.Id), Is.False);
			Assert.That(_registry.GetById(created.Data.Id)!.Value, Is.EqualTo("12"));
		});
	}

	[Test]
	public async Task Renaming_a_variable_whose_file_is_missing_keeps_it_unavailable()
	{
		var created = await CreateFileVariable("status", VariableType.Text);
		_mediator.Published.Clear();

		var renamed = await _service.UpdateUserVariable(created.Data!.Id, "renamed_status", null);

		Assert.Multiple(() =>
		{
			Assert.That(renamed.Success, Is.True);
			Assert.That(_registry.IsAvailable(created.Data.Id), Is.False);
			Assert.That(ValueChanges(), Is.Empty);
		});
	}

	[Test]
	public async Task A_read_only_file_variable_refuses_a_write_and_leaves_the_file_alone()
	{
		_files.SetFile(FilePath, "7", notify: false);
		var created = await CreateFileVariable("count", VariableType.Numeric);

		var result = await _service.SetValue(created.Data!.Id, 9m);
		await _synchronizer.WhenIdleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(VariableError.FileReadOnly));
			Assert.That(_registry.GetById(created.Data.Id)!.Value, Is.EqualTo("7"));
			Assert.That(_files.Writes, Is.Empty);
		});
	}

	[Test]
	public async Task Write_back_writes_the_new_value_once_and_its_own_echo_announces_nothing_more()
	{
		_files.SetFile(FilePath, "note", notify: false);
		var created = await CreateFileVariable("note", VariableType.Text, allowWriteBack: true);
		_mediator.Published.Clear();

		await _service.SetValue(created.Data!.Id, "line with a break\n");
		await _synchronizer.WhenIdleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_files.Writes, Is.EqualTo(new[] { (FilePath, "line with a break\n") }));
			Assert.That(_registry.GetById(created.Data.Id)!.Value, Is.EqualTo("line with a break\n"));
			Assert.That(ValueChanges(), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_value_that_came_from_the_file_is_never_written_back()
	{
		_files.SetFile(FilePath, "5", notify: false);
		var created = await _service.CreateUserVariable("level",
			VariableScope.Global,
			null,
			VariableType.Numeric,
			null,
			2,
			new VariableFileSource(FilePath, true));

		await ChangeFileExternally(FilePath, "6");

		Assert.Multiple(() =>
		{
			Assert.That(_registry.GetById(created.Data!.Id)!.Value, Is.EqualTo("6.00"));
			Assert.That(_files.Writes, Is.Empty);
			Assert.That(_files.Content(FilePath), Is.EqualTo("6"));
		});
	}

	[Test]
	public async Task A_read_that_started_before_the_users_write_cannot_bring_back_the_old_content()
	{
		_files.SetFile(FilePath, "1", notify: false);
		var created = await CreateFileVariable("level", VariableType.Numeric, allowWriteBack: true);
		var id = created.Data!.Id;

		var gate = new TaskCompletionSource();
		_files.ReadGate = gate;
		_files.SetFile(FilePath, "2");
		await WaitUntil(() => _files.GatedReads == 1);
		await _service.SetValue(id, 7m);
		_files.ReadGate = null;
		gate.SetResult();
		await _synchronizer.WhenIdleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_registry.GetById(id)!.Value, Is.EqualTo("7"));
			Assert.That(_files.Content(FilePath), Is.EqualTo("7"));
		});
	}

	[Test]
	public async Task The_check_after_a_write_never_reapplies_a_value_the_user_already_replaced()
	{
		_files.SetFile(FilePath, "1", notify: false);
		var created = await CreateFileVariable("level", VariableType.Numeric, allowWriteBack: true);
		var id = created.Data!.Id;
		_mediator.Published.Clear();

		var gate = new TaskCompletionSource();
		_files.ReadGate = gate;
		await _service.SetValue(id, 5m);
		await WaitUntil(() => _files.Content(FilePath) == "5" && _files.GatedReads == 1);
		await _service.SetValue(id, 6m);
		await WaitUntil(() => _files.Content(FilePath) == "6");
		_files.ReadGate = null;
		gate.SetResult();
		await _synchronizer.WhenIdleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_registry.GetById(id)!.Value, Is.EqualTo("6"));
			Assert.That(ValueChanges().Select(change => change.PreviousValue), Is.EqualTo(new[] { "1", "5" }));
		});
	}

	[Test]
	public async Task Rapid_writes_end_with_the_last_value_in_memory_and_in_the_file()
	{
		_files.SetFile(FilePath, "0", notify: false);
		var created = await CreateFileVariable("level", VariableType.Numeric, allowWriteBack: true);
		var id = created.Data!.Id;

		var writes = new TaskCompletionSource();
		_files.WriteGate = writes;
		await _service.SetValue(id, 5m);
		await _service.SetValue(id, 6m);
		await _service.SetValue(id, 7m);
		_files.WriteGate = null;
		writes.SetResult();
		await _synchronizer.WhenIdleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_registry.GetById(id)!.Value, Is.EqualTo("7"));
			Assert.That(_files.Content(FilePath), Is.EqualTo("7"));
		});
	}

	[Test]
	public async Task Another_application_writing_a_value_we_wrote_earlier_is_still_applied()
	{
		_files.SetFile(FilePath, "0", notify: false);
		var created = await CreateFileVariable("level", VariableType.Numeric, allowWriteBack: true);
		var id = created.Data!.Id;
		await _service.SetValue(id, 5m);
		await _synchronizer.WhenIdleAsync();
		await _service.SetValue(id, 6m);
		await _synchronizer.WhenIdleAsync();

		await ChangeFileExternally(FilePath, "5");

		Assert.That(_registry.GetById(id)!.Value, Is.EqualTo("5"));
	}

	[Test]
	public async Task A_failed_write_back_keeps_the_users_value_and_shows_the_variable_as_unavailable()
	{
		_files.SetFile(FilePath, "1", notify: false);
		var created = await CreateFileVariable("level", VariableType.Numeric, allowWriteBack: true);
		var id = created.Data!.Id;
		_files.WriteFailure = _ => new UnauthorizedAccessException("read-only file");

		await _service.SetValue(id, 5m);
		await _synchronizer.WhenIdleAsync();
		var afterFailure = (_registry.GetById(id)!.Value, _registry.IsAvailable(id), _files.Content(FilePath));

		_files.WriteFailure = null;
		await ChangeFileExternally(FilePath, "8");

		Assert.Multiple(() =>
		{
			Assert.That(afterFailure, Is.EqualTo(("5", false, "1")));
			Assert.That(_registry.GetById(id)!.Value, Is.EqualTo("8"));
			Assert.That(_registry.IsAvailable(id), Is.True);
		});
	}

	[Test]
	public async Task A_write_back_into_a_missing_file_creates_it_and_makes_the_variable_available()
	{
		var created = await CreateFileVariable("note", VariableType.Text, allowWriteBack: true);
		var id = created.Data!.Id;

		await _service.SetValue(id, "created by Macro Deck");
		await _synchronizer.WhenIdleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_files.Content(FilePath), Is.EqualTo("created by Macro Deck"));
			Assert.That(_registry.IsAvailable(id), Is.True);
		});
	}

	[Test]
	public async Task A_handler_that_throws_during_a_write_does_not_stop_later_file_changes()
	{
		_files.SetFile(FilePath, "1", notify: false);
		var created = await CreateFileVariable("level", VariableType.Numeric, allowWriteBack: true);
		var id = created.Data!.Id;
		var throwOnce = true;
		_mediator.BeforePublish = notification =>
		{
			if (notification is VariableValueChangedNotification && throwOnce)
			{
				throwOnce = false;
				throw new InvalidOperationException("handler failed");
			}
		};

		Assert.ThrowsAsync<InvalidOperationException>(() => _service.SetValue(id, 5m));
		await _synchronizer.WhenIdleAsync();
		await ChangeFileExternally(FilePath, "9");

		Assert.That(_registry.GetById(id)!.Value, Is.EqualTo("9"));
	}

	[Test]
	public async Task A_new_path_is_read_and_the_old_file_no_longer_counts()
	{
		_files.SetFile(FilePath, "old", notify: false);
		_files.SetFile(OtherFilePath, "other", notify: false);
		var created = await CreateFileVariable("status", VariableType.Text);
		var id = created.Data!.Id;

		var updated = await _service.UpdateUserVariable(id, null, null, new VariableFileSource(OtherFilePath, false));
		await _synchronizer.WhenIdleAsync();
		var afterSwitch = _registry.GetById(id)!.Value;
		await ChangeFileExternally(FilePath, "ignored");
		await ChangeFileExternally(OtherFilePath, "followed");

		Assert.Multiple(() =>
		{
			Assert.That(updated.Success, Is.True);
			Assert.That(afterSwitch, Is.EqualTo("other"));
			Assert.That(_registry.GetById(id)!.Value, Is.EqualTo("followed"));
			Assert.That(_files.WatcherCount(FilePath), Is.Zero);
			Assert.That(_files.Writes, Is.Empty);
		});
	}

	[Test]
	public async Task Turning_write_back_off_drops_a_queued_write_and_follows_the_file_again()
	{
		_files.SetFile(FilePath, "1", notify: false);
		var created = await CreateFileVariable("level", VariableType.Numeric, allowWriteBack: true);
		var id = created.Data!.Id;
		var writes = new TaskCompletionSource();
		_files.WriteGate = writes;
		await _service.SetValue(id, 5m);
		await WaitUntil(() => _files.GatedWrites == 1);
		await _service.SetValue(id, 6m);

		await _service.UpdateUserVariable(id, null, null, new VariableFileSource(FilePath, false));
		_files.WriteGate = null;
		writes.SetResult();
		await _synchronizer.WhenIdleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_files.Writes.Select(write => write.Content), Is.EqualTo(new[] { "5" }));
			Assert.That(_registry.GetById(id)!.Value, Is.EqualTo(_files.Content(FilePath)));
			Assert.That(_registry.GetById(id)!.CanWrite, Is.False);
		});
	}

	[Test]
	public async Task A_refused_update_leaves_the_file_source_as_it_was()
	{
		_files.SetFile(FilePath, "old", notify: false);
		_files.SetFile(OtherFilePath, "other", notify: false);
		await _service.CreateUserVariable("taken", VariableScope.Global, null, VariableType.Text, "x", null);
		var created = await CreateFileVariable("status", VariableType.Text);
		var id = created.Data!.Id;

		var updated = await _service.UpdateUserVariable(id, "taken", null, new VariableFileSource(OtherFilePath, true));
		await _synchronizer.WhenIdleAsync();
		await ChangeFileExternally(FilePath, "still followed");

		Assert.Multiple(() =>
		{
			Assert.That(updated.Error, Is.EqualTo(VariableError.AlreadyExists));
			Assert.That(_registry.GetById(id)!.FileSource, Is.EqualTo(new VariableFileSource(FilePath, false)));
			Assert.That(_registry.GetById(id)!.CanWrite, Is.False);
			Assert.That(_registry.GetById(id)!.Value, Is.EqualTo("still followed"));
		});
	}

	[Test]
	public async Task Setting_the_value_the_file_already_holds_leaves_the_file_untouched()
	{
		_files.SetFile(FilePath, "Hello\n", notify: false);
		var created = await CreateFileVariable("greeting", VariableType.Text, allowWriteBack: true);

		await _service.SetValue(created.Data!.Id, "Hello");
		await _synchronizer.WhenIdleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_files.Writes, Is.Empty);
			Assert.That(_files.Content(FilePath), Is.EqualTo("Hello\n"));
		});
	}

	[Test]
	public async Task A_set_right_after_an_external_change_still_reaches_the_file()
	{
		_files.SetFile(FilePath, "B", notify: false);
		var created = await CreateFileVariable("state", VariableType.Text, allowWriteBack: true);
		var id = created.Data!.Id;

		var gate = new TaskCompletionSource();
		_files.ReadGate = gate;
		_files.SetFile(FilePath, "A");
		await WaitUntil(() => _files.GatedReads == 1);
		await _service.SetValue(id, "B");
		_files.ReadGate = null;
		gate.SetResult();
		await _synchronizer.WhenIdleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_files.Content(FilePath), Is.EqualTo("B"));
			Assert.That(_registry.GetById(id)!.Value, Is.EqualTo("B"));
		});
	}

	[Test]
	public async Task A_normal_variable_cannot_be_given_a_file_source_afterwards()
	{
		var created = await _service.CreateUserVariable("count", VariableScope.Global, null, VariableType.Numeric, 1, null);

		var updated = await _service.UpdateUserVariable(created.Data!.Id,
			null,
			null,
			new VariableFileSource(FilePath, false));

		Assert.That(updated.Error, Is.EqualTo(VariableError.ValidationError));
	}

	[Test]
	public async Task A_relative_path_is_refused()
	{
		var created = await _service.CreateUserVariable("status",
			VariableScope.Global,
			null,
			VariableType.Text,
			null,
			null,
			new VariableFileSource("value.txt", false));

		Assert.That(created.Error, Is.EqualTo(VariableError.InvalidFilePath));
	}

	[Test]
	public async Task Deleting_a_variable_or_its_widget_stops_watching_the_file()
	{
		_files.SetFile(FilePath, "1", notify: false);
		var global = await CreateFileVariable("level", VariableType.Numeric);
		await _service.CreateUserVariable("local",
			VariableScope.Widget,
			WidgetId,
			VariableType.Numeric,
			null,
			null,
			new VariableFileSource(OtherFilePath, false));

		await _service.DeleteUserVariable(global.Data!.Id);
		await _service.DeleteByScopeInstance(VariableScope.Widget, WidgetId);

		Assert.Multiple(() =>
		{
			Assert.That(_files.WatcherCount(FilePath), Is.Zero);
			Assert.That(_files.WatcherCount(OtherFilePath), Is.Zero);
		});
	}

	[Test]
	public async Task A_normal_user_variable_stays_writable()
	{
		var created = await _service.CreateUserVariable("count", VariableScope.Global, null, VariableType.Numeric, 1, null);

		var result = await _service.SetValue(created.Data!.Id, 2m);

		Assert.Multiple(() =>
		{
			Assert.That(created.Data.CanWrite, Is.True);
			Assert.That(result.Success, Is.True);
			Assert.That(_files.Writes, Is.Empty);
		});
	}

	private Task<MacroDeckHost.Domain.Common.Result<VariableEntity, VariableError>> CreateFileVariable(
		string name,
		VariableType type,
		bool allowWriteBack = false)
		=> _service.CreateUserVariable(name,
			VariableScope.Global,
			null,
			type,
			null,
			null,
			new VariableFileSource(FilePath, allowWriteBack));

	private async Task ChangeFileExternally(string path, string content)
	{
		_files.SetFile(path, content);
		await _synchronizer.WhenIdleAsync();
	}

	private List<VariableValueChangedNotification> ValueChanges()
		=> _mediator.Published.OfType<VariableValueChangedNotification>().ToList();

	private static async Task WaitUntil(Func<bool> condition)
	{
		for (var attempt = 0; attempt < 200 && !condition(); attempt++)
		{
			await Task.Delay(10);
		}

		Assert.That(condition(), Is.True, "Condition was not reached in time");
	}

	private sealed class CountingUserVariableStore : IUserVariableStore
	{
		public int Saves { get; private set; }

		public IReadOnlyList<VariableEntity> Load() => [];

		public void Save(IEnumerable<VariableEntity> userVariables) => Saves++;
	}
}
