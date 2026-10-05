using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Profiles;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Services;

[TestFixture]
public class MoveProfileRequestMessageHandlerTests
{
	private ProfileCache _cache = null!;
	private MoveProfileRequestMessageHandler _handler = null!;
	private Guid _first;
	private Guid _second;

	[SetUp]
	public async Task SetUp()
	{
		_cache = new ProfileCache(new InMemoryProfileStore(), new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		_handler = new MoveProfileRequestMessageHandler(new ProfileService(_cache,
			new FolderCache(_cache),
			new InMemoryDeviceRepository(),
			new RecordingMediator(),
			new WidgetSecretCloner(new FakeSecretService()),
			new NullWidgetVariableCloner()));

		_first = Guid.NewGuid();
		_second = Guid.NewGuid();
		await _cache.AddOrUpdate(new ProfileEntity { Id = _first, Name = "First", Order = 0 });
		await _cache.AddOrUpdate(new ProfileEntity { Id = _second, Name = "Second", Order = 1 });
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	private ValueTask<MoveProfileResponse> Move(string id, string targetId, string position)
		=> _handler.Handle(new MoveProfileRequest { Id = id, TargetId = targetId, Position = position },
			CancellationToken.None);

	[Test]
	public async Task A_valid_move_answers_the_new_placements()
	{
		var response = await Move(_second.ToString(), _first.ToString(), "before");

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(response.Profiles!.Single(p => p.Id == _second.ToString()).Order, Is.EqualTo(0));
			Assert.That(response.Profiles!.Single(p => p.Id == _first.ToString()).Order, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_plugin_profile_cannot_be_moved()
	{
		var response = await Move("spotify::car-thing", _first.ToString(), "before");

		Assert.That(response.Error!.Code, Is.EqualTo(nameof(ProfileError.IsVirtual)));
	}

	[Test]
	public async Task A_profile_cannot_be_moved_next_to_a_plugin_profile()
	{
		var response = await Move(_first.ToString(), "spotify::car-thing", "after");

		Assert.That(response.Error!.Code, Is.EqualTo(nameof(ProfileError.IsVirtual)));
	}

	[TestCase("inside")]
	[TestCase("")]
	[TestCase("7")]
	public async Task A_position_other_than_before_or_after_is_rejected(string position)
	{
		var response = await Move(_second.ToString(), _first.ToString(), position);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo(nameof(ProfileError.ValidationError)));
		});
	}

	[Test]
	public async Task Missing_ids_are_rejected()
	{
		var response = await Move(string.Empty, _first.ToString(), "before");

		Assert.That(response.Error!.Code, Is.EqualTo(nameof(ProfileError.ValidationError)));
	}
}
