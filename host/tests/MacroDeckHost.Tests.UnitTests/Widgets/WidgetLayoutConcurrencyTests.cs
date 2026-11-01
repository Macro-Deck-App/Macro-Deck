using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Persistence.Profiles;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Widgets;

[TestFixture]
public class WidgetLayoutConcurrencyTests
{
	private static readonly TimeSpan _hangLimit = TimeSpan.FromSeconds(20);

	private PinScopeFixture _fixture = null!;
	private WidgetLayoutGate _gate = null!;

	[SetUp]
	public void SetUp()
	{
		_fixture = new PinScopeFixture();
		_gate = new WidgetLayoutGate();
	}

	[TearDown]
	public void TearDown()
	{
		_gate.Dispose();
		_fixture.Dispose();
	}

	[Test]
	public async Task Concurrent_creates_on_free_cells_all_succeed()
	{
		var folder = _fixture.AddFolder("Large", null, 10, 10);
		var cells = Enumerable.Range(0, 100).Select(i => (X: i % 10, Y: i / 10)).ToList();

		var results = await InParallel(cells.Count, i => NewScope().Create(folder.Id, Tile(cells[i].X, cells[i].Y)));

		Assert.Multiple(() =>
		{
			Assert.That(results.All(result => result.Success), Is.True);
			Assert.That(_fixture.Reload(folder).Widgets, Has.Count.EqualTo(100));
		});
		AssertNoOverlap(folder);
	}

	[Test]
	public async Task Concurrent_creates_on_one_cell_give_one_success_and_position_occupied_for_the_rest()
	{
		var folder = _fixture.AddFolder("Contested", null, 4, 4);

		var results = await InParallel(30, _ => NewScope().Create(folder.Id, Tile(1, 1)));

		Assert.Multiple(() =>
		{
			Assert.That(results.Count(result => result.Success), Is.EqualTo(1));
			Assert.That(results.Where(result => !result.Success).Select(result => result.Error),
				Is.All.EqualTo(WidgetError.PositionOccupied));
			Assert.That(_fixture.Reload(folder).Widgets, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task Concurrent_moves_onto_one_cell_let_exactly_one_win()
	{
		var folder = _fixture.AddFolder("Moves", null, 4, 4);
		var first = _fixture.AddWidget(folder, 0, 0);
		var second = _fixture.AddWidget(folder, 1, 0);

		for (var round = 0; round < 300; round++)
		{
			var results = await InParallel(2,
				i => NewScope()
					.UpdatePositions(folder.Id,
						[new WidgetPlacement(i == 0 ? first.Id : second.Id, 3, 3, 1, 1)]));

			Assert.That(results.Count(result => result.Success), Is.EqualTo(1), $"round {round}");
			AssertNoOverlap(folder);

			var reset = await NewScope()
				.UpdatePositions(folder.Id,
					[new WidgetPlacement(first.Id, 0, 0, 1, 1), new WidgetPlacement(second.Id, 1, 0, 1, 1)]);
			Assert.That(reset.Success, Is.True);
		}
	}

	[Test]
	public async Task A_pin_landing_during_a_create_on_its_cell_cannot_also_succeed()
	{
		var target = _fixture.Games;
		var pinned = _fixture.AddWidget(_fixture.Main, 2, 2);
		var cloner = new ParkingSecretCloner();

		var create = Task.Run(() => NewScope(cloner).Create(target.Id, Tile(2, 2)));
		await cloner.Entered.Task.WaitAsync(_hangLimit);
		var pin = Task.Run(() => NewScope().SetPinned(_fixture.Main.Id, pinned.Id, true, PinScope.Profile));
		await Task.Delay(100);
		cloner.Release.SetResult();

		var created = await create.WaitAsync(_hangLimit);
		var pinResult = await pin.WaitAsync(_hangLimit);

		Assert.That(created.Success && pinResult.Success, Is.False);
	}

	[Test]
	public async Task Readers_can_enumerate_a_folder_while_every_kind_of_write_runs()
	{
		var folder = _fixture.AddFolder("Busy", null, 4, 4);
		var seed = _fixture.AddWidget(folder, 0, 0);
		using var stop = new CancellationTokenSource();

		var reader = Task.Run(() =>
		{
			while (!stop.IsCancellationRequested)
			{
				foreach (var widget in _fixture.Reload(folder).Widgets)
				{
					_ = widget.Id;
				}
			}
		});

		for (var i = 0; i < 3000 && !reader.IsCompleted; i++)
		{
			var added = Tile(1, 1);
			added.Id = Guid.NewGuid();
			added.FolderId = folder.Id;
			_fixture.FolderCache.AddWidget(folder.Id, added);
			_fixture.FolderCache.UpdateWidget(folder.Id, added);
			_fixture.FolderCache.UpdateWidgets(folder.Id, [seed]);
			_fixture.FolderCache.ReplaceWidgets(folder.Id, [added.Id], [added]);
			_fixture.FolderCache.RemoveWidget(folder.Id, added.Id);
		}

		await stop.CancelAsync();
		await reader.WaitAsync(_hangLimit);
	}

	[Test]
	public async Task A_notification_handler_can_call_back_into_the_widget_service()
	{
		var folder = _fixture.AddFolder("Reentry", null, 4, 4);
		var existing = _fixture.AddWidget(folder, 0, 0);
		var reentered = false;
		_fixture.Mediator.BeforePublish = _ =>
		{
			if (reentered)
			{
				return;
			}

			reentered = true;
			NewScope().Delete(existing.Id, folder.Id).GetAwaiter().GetResult();
		};

		var created = await Task.Run(() => NewScope().Create(folder.Id, Tile(1, 1))).WaitAsync(_hangLimit);

		Assert.That(created.Success, Is.True);
		Assert.That(_fixture.Reload(folder).Widgets.Select(widget => widget.Id), Does.Not.Contain(existing.Id));
	}

	[Test]
	public async Task A_failed_persist_inside_the_section_does_not_block_the_next_operation()
	{
		var store = new FailingProfileStore(_fixture.Store);
		using var cache = new ProfileCache(store, new LoggerConfiguration().CreateLogger());
		await cache.InitializeCache();
		var folders = new FolderCache(cache);
		var folder = _fixture.Main;
		var service = NewScope(new ParkingSecretCloner { Passthrough = true }, folders, cache);

		store.Fail = true;
		Assert.ThrowsAsync<ProfilePersistenceException>(() => service.Create(folder.Id, Tile(0, 0)));
		store.Fail = false;

		var next = await service.Create(folder.Id, Tile(0, 0)).WaitAsync(_hangLimit);

		Assert.That(next.Success, Is.True);
	}

	[Test]
	public async Task A_folder_write_that_waited_for_the_gate_does_not_lose_a_widget_added_meanwhile()
	{
		var folder = _fixture.AddFolder("Undo", null, 4, 4);
		var folderService = new FolderService(_fixture.FolderCache,
			_fixture.Cache,
			new InMemoryDeviceRepository(),
			_fixture.Mediator,
			new WidgetSecretCloner(new FakeSecretService()),
			new WidgetSecretScrubber(new FakeSecretService()),
			new NullWidgetVariableCloner(),
			TestFolderViewProviders.Registry(),
			null,
			_gate);

		var lease = await _gate.Enter();
		var write = Task.Run(() => folderService.SetFocusRule(folder.Id,
			new FolderFocusRule
			{
				Enabled = true,
				ApplicationIdentity = "com.example.app",
				IdentityKind = ApplicationIdentityKind.BundleId
			}));
		await Task.Delay(100);

		var restored = new FolderEntity
		{
			Id = folder.Id,
			ProfileId = folder.ProfileId,
			Name = folder.Name,
			Order = folder.Order,
			Rows = folder.Rows,
			Columns = folder.Columns,
			CreatedAt = folder.CreatedAt,
			Widgets = folder.Widgets
		};
		await _fixture.FolderCache.AddOrUpdate(restored);
		var added = _fixture.AddWidget(restored, 0, 0);
		lease.Dispose();

		var result = await write.WaitAsync(_hangLimit);

		Assert.That(result.Success, Is.True);
		Assert.That(_fixture.Reload(folder).Widgets.Select(widget => widget.Id), Does.Contain(added.Id));
	}

	private WidgetService NewScope(
		IWidgetSecretCloner? cloner = null,
		FolderCache? folders = null,
		ProfileCache? cache = null)
	{
		var secrets = new FakeSecretService();
		return new WidgetService(folders ?? _fixture.FolderCache,
			cache ?? _fixture.Cache,
			_fixture.Mediator,
			new WidgetSecretScrubber(secrets),
			cloner ?? new YieldingSecretCloner(),
			new NullWidgetVariableCloner(),
			null,
			_gate);
	}

	private static WidgetEntity Tile(int x, int y)
	{
		return new WidgetEntity
		{
			Type = WidgetTypeIds.ActionButton,
			PositionX = x,
			PositionY = y,
			Width = 1,
			Height = 1
		};
	}

	private static async Task<T[]> InParallel<T>(int count, Func<int, Task<T>> operation)
	{
		using var barrier = new Barrier(count);
		var tasks = Enumerable.Range(0, count)
			.Select(i => Task.Factory
				.StartNew(() =>
					{
						barrier.SignalAndWait(_hangLimit);
						return operation(i);
					},
					CancellationToken.None,
					TaskCreationOptions.LongRunning,
					TaskScheduler.Default)
				.Unwrap());

		return await Task.WhenAll(tasks).WaitAsync(_hangLimit);
	}

	private void AssertNoOverlap(FolderEntity folder)
	{
		var cells = _fixture.Reload(folder).Widgets.Select(widget => (widget.PositionX, widget.PositionY)).ToList();
		Assert.That(cells, Is.Unique);
	}

	private sealed class YieldingSecretCloner : IWidgetSecretCloner
	{
		public async Task<string?> CloneReferencedSecrets(string? widgetData)
		{
			await Task.Delay(1);
			return widgetData;
		}
	}

	private sealed class ParkingSecretCloner : IWidgetSecretCloner
	{
		public bool Passthrough { get; init; }
		public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public async Task<string?> CloneReferencedSecrets(string? widgetData)
		{
			if (!Passthrough)
			{
				Entered.TrySetResult();
				await Release.Task;
			}

			return widgetData;
		}
	}

	private sealed class FailingProfileStore(IProfileStore inner) : IProfileStore
	{
		public bool Fail { get; set; }

		public ProfileLoadResult LoadAll()
		{
			return inner.LoadAll();
		}

		public bool Save(ProfileFile profile)
		{
			return !Fail && inner.Save(profile);
		}

		public void Delete(Guid id)
		{
			inner.Delete(id);
		}
	}
}
