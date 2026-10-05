using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Persistence.Profiles;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Services;

[TestFixture]
public class ProfileServiceMoveTests
{
	private static readonly Guid Gaming = Guid.Parse("00000000-0000-0000-0000-00000000000a");
	private static readonly Guid Streaming = Guid.Parse("00000000-0000-0000-0000-00000000000b");
	private static readonly Guid Work = Guid.Parse("00000000-0000-0000-0000-00000000000c");

	private InMemoryProfileStore _store = null!;
	private ProfileCache _cache = null!;
	private RecordingMediator _mediator = null!;
	private ProfileService _service = null!;

	private async Task Seed(params ProfileFile[] profiles)
	{
		_store = new InMemoryProfileStore(profiles);
		_cache = new ProfileCache(_store, new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		_mediator = new RecordingMediator();
		_service = new ProfileService(_cache,
			new FolderCache(_cache),
			new InMemoryDeviceRepository(),
			_mediator,
			new WidgetSecretCloner(new FakeSecretService()),
			new NullWidgetVariableCloner());
	}

	private Task SeedThree()
		=> Seed(new ProfileFile { Id = Gaming, Name = "Gaming", Order = 0 },
			new ProfileFile { Id = Streaming, Name = "Streaming", Order = 1 },
			new ProfileFile { Id = Work, Name = "Work", Order = 2 });

	private Guid[] PersistedOrder()
		=> _store.LoadAll().Profiles.OrderBy(file => file.Order).Select(file => file.Id).ToArray();

	[TearDown]
	public void TearDown() => _cache?.Dispose();

	[Test]
	public async Task Moving_the_last_profile_before_the_first_puts_it_on_top_and_persists_unique_orders()
	{
		await SeedThree();

		var result = await _service.Move(Work, Gaming, ProfileMovePosition.Before);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(PersistedOrder(), Is.EqualTo(new[] { Work, Gaming, Streaming }));
			Assert.That(_store.LoadAll().Profiles.Select(file => file.Order), Is.EquivalentTo(new[] { 0, 1, 2 }));
		});
	}

	[Test]
	public async Task Moving_a_profile_after_another_places_it_directly_behind_it()
	{
		await SeedThree();

		var result = await _service.Move(Gaming, Streaming, ProfileMovePosition.After);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(PersistedOrder(), Is.EqualTo(new[] { Streaming, Gaming, Work }));
		});
	}

	[Test]
	public async Task A_move_reports_only_the_profiles_whose_position_changed_and_does_not_announce_profile_updates()
	{
		await SeedThree();

		var result = await _service.Move(Gaming, Streaming, ProfileMovePosition.After);

		var reordered = _mediator.Published.OfType<ProfilesReorderedNotification>().Single();
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Select(profile => profile.Id), Is.EquivalentTo(new[] { Gaming, Streaming }));
			Assert.That(reordered.Profiles.Select(profile => profile.Id), Is.EquivalentTo(new[] { Gaming, Streaming }));
			Assert.That(_mediator.Published.OfType<ProfileUpdatedNotification>(), Is.Empty);
		});
	}

	[Test]
	public async Task Duplicate_stored_orders_are_resolved_by_name_and_then_normalized()
	{
		await Seed(new ProfileFile { Id = Work, Name = "Work", Order = 0 },
			new ProfileFile { Id = Streaming, Name = "streaming", Order = 0 },
			new ProfileFile { Id = Gaming, Name = "Gaming", Order = 0 });

		var result = await _service.Move(Gaming, Work, ProfileMovePosition.After);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(PersistedOrder(), Is.EqualTo(new[] { Streaming, Work, Gaming }));
			Assert.That(_store.LoadAll().Profiles.Select(file => file.Order), Is.EquivalentTo(new[] { 0, 1, 2 }));
		});
	}

	[Test]
	public async Task A_profile_created_after_a_move_is_added_at_the_end()
	{
		await SeedThree();
		await _service.Move(Work, Gaming, ProfileMovePosition.Before);

		var created = await _service.Create("Music");

		Assert.That(PersistedOrder().Last(), Is.EqualTo(created.Data!.Id));
	}

	[Test]
	public async Task Moving_relative_to_an_unknown_profile_fails_and_changes_nothing()
	{
		await SeedThree();

		var unknownTarget = await _service.Move(Gaming, Guid.NewGuid(), ProfileMovePosition.Before);
		var unknownSource = await _service.Move(Guid.NewGuid(), Gaming, ProfileMovePosition.Before);

		Assert.Multiple(() =>
		{
			Assert.That(unknownTarget.Error, Is.EqualTo(ProfileError.NotFound));
			Assert.That(unknownSource.Error, Is.EqualTo(ProfileError.NotFound));
			Assert.That(PersistedOrder(), Is.EqualTo(new[] { Gaming, Streaming, Work }));
			Assert.That(_mediator.Published.OfType<ProfilesReorderedNotification>(), Is.Empty);
		});
	}

	[Test]
	public async Task Moving_a_profile_relative_to_itself_is_rejected()
	{
		await SeedThree();

		var result = await _service.Move(Gaming, Gaming, ProfileMovePosition.After);

		Assert.That(result.Error, Is.EqualTo(ProfileError.ValidationError));
	}
}
