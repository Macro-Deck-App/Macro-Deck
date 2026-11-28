using MacroDeckHost.Application.Devices.Surfaces.KeyImages;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Plugins.IconPacks;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Tests.UnitTests.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Devices.Surfaces;

[TestFixture]
internal sealed class DeviceKeyImageResourcesTests
{
	private const string Broken = "broken.icon";

	private ManualTimeProvider _time = null!;
	private UiResourceStore _store = null!;
	private ServiceProvider _services = null!;
	private DeviceKeyImageResources _resources = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new ManualTimeProvider();
		_store = new UiResourceStore();
		var services = new ServiceCollection();
		services.AddSingleton<IUiResourceStore>(_store);
		services.AddSingleton<IPluginIconUiResources>(new ThrowingPluginIcons());
		_services = services.BuildServiceProvider();
		_resources = new DeviceKeyImageResources(_services.GetRequiredService<IServiceScopeFactory>(),
			new IconAppearanceContext(null, IconAppearanceTraits.Static),
			_time,
			Serilog.Core.Logger.None);
	}

	[TearDown]
	public void TearDown()
	{
		_services.Dispose();
	}

	private string Register(string name, byte[]? content = null)
	{
		return _store.Register(new UiResourceRegistration
			{
				OwnerId = "test", Name = name, MediaType = "image/png", Content = content ?? [1]
			})
			.ResourceId;
	}

	private async Task FetchAsync(params string[] resourceIds)
	{
		await _resources.FetchAsync(resourceIds.Select(id => new DeviceKeyImageResourceRequest(id, null)),
			CancellationToken.None);
	}

	private bool Pending(string resourceId)
	{
		_resources.Lookup(resourceId, null, out var pending);
		return pending;
	}

	private void Retain(params string[] resourceIds)
	{
		_resources.Retain(resourceIds.Select(id => (id, (int?)null)));
	}

	[Test]
	public async Task An_entry_not_used_within_the_window_is_dropped_and_one_used_in_it_is_kept()
	{
		var old = Register("old");
		var recent = Register("recent");
		await FetchAsync(old, recent);
		Retain(old, recent);

		_time.Advance(TimeSpan.FromSeconds(20));
		Retain(recent);
		_time.Advance(TimeSpan.FromSeconds(15));
		Retain();

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(Pending(old), Is.True);
			Assert.That(Pending(recent), Is.False);
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task At_most_the_cap_of_entries_beyond_those_in_use_is_kept_the_oldest_first_out()
	{
		var ids = Enumerable.Range(0, DeviceKeyImageResources.MaxUnusedEntries + 2)
			.Select(index => Register($"icon{index}"))
			.ToArray();
		foreach (var id in ids)
		{
			await FetchAsync(id);
			Retain(id);
			_time.Advance(TimeSpan.FromMilliseconds(10));
		}

		var inUse = Register("in-use");
		await FetchAsync(inUse);
		Retain(inUse);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(ids.Take(2).Select(Pending), Is.All.True, "the two oldest are out");
			Assert.That(ids.Skip(2).Select(Pending), Is.All.False);
			Assert.That(Pending(inUse), Is.False);
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task An_id_registered_again_with_new_bytes_counts_as_still_loading()
	{
		var id = Register("icon");
		await FetchAsync(id);
		Register("icon", [9, 9]);

		Assert.That(Pending(id), Is.True);
	}

	[Test]
	public async Task An_absent_id_counts_as_still_loading_once_it_is_registered()
	{
		await FetchAsync("test.later");
		var before = Pending("test.later");
		Register("later");

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(before, Is.False);
			Assert.That(Pending("test.later"), Is.True);
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task A_forgotten_absent_id_stays_known_and_is_fetched_again()
	{
		await FetchAsync("test.gone");
		_resources.ForgetAbsent();
		var stale = Pending("test.gone");

		var refetched = await _resources.FetchAsync([new DeviceKeyImageResourceRequest("test.gone", null)],
			CancellationToken.None);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(stale, Is.False, "it never holds a key");
			Assert.That(refetched,
				Is.EqualTo(new DeviceKeyImageFetch(false, false)),
				"absent again, nothing to redraw");
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task A_lookup_that_throws_is_recorded_absent_and_the_others_still_resolve()
	{
		var id = Register("fine");

		var fetched = await _resources.FetchAsync([
				new DeviceKeyImageResourceRequest(Broken, null),
				new DeviceKeyImageResourceRequest(id, null)
			],
			CancellationToken.None);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(fetched, Is.EqualTo(new DeviceKeyImageFetch(true, true)));
			Assert.That(Pending(Broken), Is.False);
			Assert.That(_resources.TryGet(id, null), Is.Not.Null);
			await Task.CompletedTask;
		});
	}

	private sealed class ThrowingPluginIcons : IPluginIconUiResources
	{
		public Task<PluginIconResourceResult> GetHandleAsync(
			string pluginId,
			string key,
			string name,
			CancellationToken cancellationToken)
		{
			return Task.FromResult(new PluginIconResourceResult(PluginIconResourceStatus.NotFound));
		}

		public Task<PluginIconResourceResult> GetHandleAsync(Guid iconId, CancellationToken cancellationToken)
		{
			return Task.FromResult(new PluginIconResourceResult(PluginIconResourceStatus.NotFound));
		}

		public Task<UiResourceContent?> TryGetAsync(string resourceId, CancellationToken cancellationToken)
		{
			return resourceId == Broken
				? throw new InvalidOperationException("The icon pack could not be read.")
				: Task.FromResult<UiResourceContent?>(null);
		}
	}
}
