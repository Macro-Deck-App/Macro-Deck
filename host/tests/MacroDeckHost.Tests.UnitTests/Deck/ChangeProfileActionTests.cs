using MacroDeckHost.Integrations.Deck;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Decks;

namespace MacroDeckHost.Tests.UnitTests.Deck;

[TestFixture]
internal sealed class ChangeProfileActionTests
{
	private static readonly string[] _allProfileIds = ["p1", "p2", "p3"];
	private static readonly string[] _streamingProfileIds = ["p1", "p3"];

	private static ActionExecutionContext Context(Dictionary<string, object> parameters, string? originClientId = null)
		=> new() { Parameters = parameters, OriginClientId = originClientId };

	[Test]
	public async Task GetDynamicOptionsAsync_ReturnsProfilesFilteredByLabel()
	{
		var navigator = new FakeDeckNavigator
		{
			Profiles =
			[
				new DeckProfile { Id = "p1", Label = "Streaming" },
				new DeckProfile { Id = "p2", Label = "Gaming" },
				new DeckProfile { Id = "p3", Label = "Streaming Extras" }
			]
		};
		var action = new ChangeProfileActionDefinition(() => navigator);

		var all = await action.GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = "profileId",
				CurrentParameters = new Dictionary<string, object?>()
			},
			CancellationToken.None);
		var filtered = await action.GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = "profileId",
				Filter = "stream",
				CurrentParameters = new Dictionary<string, object?>()
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(all.Options.Select(o => o.Value), Is.EquivalentTo(_allProfileIds));
			Assert.That(filtered.Options.Select(o => o.Value), Is.EquivalentTo(_streamingProfileIds));
		});
	}

	[Test]
	public async Task ExecuteAsync_NullNavigator_FailsWithUnavailable()
	{
		var action = new ChangeProfileActionDefinition(() => null);

		var result = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>()));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
		});
	}

	[Test]
	public async Task ExecuteAsync_NoProfileIdSelected_FailsWithInvalidParameter()
	{
		var action = new ChangeProfileActionDefinition(() => new FakeDeckNavigator());

		var result = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>()));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
		});
	}

	[Test]
	public async Task ExecuteAsync_UnknownProfileId_FailsWithNotFound()
	{
		var navigator = new FakeDeckNavigator { Profiles = [new DeckProfile { Id = "p1", Label = "Streaming" }] };
		var action = new ChangeProfileActionDefinition(() => navigator);

		var result = await action.CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object> { ["profileId"] = "does-not-exist" }));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
		});
	}

	[Test]
	public async Task ExecuteAsync_KnownProfileId_SucceedsAndForwardsTheOriginClientId()
	{
		var navigator = new FakeDeckNavigator { Profiles = [new DeckProfile { Id = "p1", Label = "Streaming" }] };
		var action = new ChangeProfileActionDefinition(() => navigator);

		var result = await action.CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object> { ["profileId"] = "p1" }, originClientId: "client-1"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(navigator.ChangeProfileCalls, Has.Count.EqualTo(1));
			Assert.That(navigator.ChangeProfileCalls.Single(), Is.EqualTo(("p1", "client-1")));
		});
	}

	private sealed class FakeDeckNavigator : IDeckNavigator
	{
		public List<DeckProfile> Profiles { get; set; } = [];
		public List<(string ProfileId, string? OriginClientId)> ChangeProfileCalls { get; } = [];

		public Task ChangeFolderAsync(string folderId,
			string? originClientId = null,
			CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public Task ChangeProfileAsync(string profileId,
			string? originClientId = null,
			CancellationToken cancellationToken = default)
		{
			ChangeProfileCalls.Add((profileId, originClientId));
			return Task.CompletedTask;
		}

		public Task GoToParentAsync(string? originClientId = null, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public Task GoBackAsync(string? originClientId = null, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public IReadOnlyList<DeckFolder> GetFolders() => throw new NotSupportedException();

		public IReadOnlyList<DeckProfile> GetProfiles() => Profiles;
	}
}
