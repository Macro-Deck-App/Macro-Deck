using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Profiles;

// The last profile can never be deleted, however the deletes of the last two arrive.
[NonParallelizable]
public class LastProfileDeletionRaceTests
{
	private const int Rounds = 200;

	private InProcessHost _host = null!;

	[SetUp]
	public async Task SetUp()
	{
		_host = await InProcessHost.StartAsync();
		await _host.Services.GetRequiredService<IProfileCache>().InitializeCache();
		await _host.Services.GetRequiredService<IFolderCache>().InitializeCache();
	}

	[TearDown]
	public async Task TearDown()
	{
		await _host.DisposeAsync();
	}

	[Test]
	public async Task Deleting_the_last_two_profiles_at_once_always_keeps_one()
	{
		var cache = _host.Services.GetRequiredService<IProfileCache>();
		await CreateProfile("first");

		var emptied = 0;
		for (var round = 0; round < Rounds; round++)
		{
			await CreateProfile($"round {round}");
			var ids = cache.GetAll().Select(profile => profile.Id).ToList();
			Assert.That(ids, Has.Count.EqualTo(2));

			using var start = new ManualResetEventSlim();
			var deletes = ids.Select(id => Task.Run(async () =>
				{
					start.Wait();
					await using var scope = _host.Services.CreateAsyncScope();
					return await scope.ServiceProvider.GetRequiredService<IProfileService>().Delete(id);
				}))
				.ToList();
			start.Set();
			var results = await Task.WhenAll(deletes);

			if (cache.GetAll().Count == 0)
			{
				emptied++;
				await CreateProfile($"refill {round}");
			}

			Assert.That(results.Count(result => result.Success), Is.LessThanOrEqualTo(1), $"round {round}");
		}

		Assert.That(emptied, Is.Zero);
	}

	private async Task CreateProfile(string name)
	{
		await using var scope = _host.Services.CreateAsyncScope();
		var created = await scope.ServiceProvider.GetRequiredService<IProfileService>().Create(name);
		Assert.That(created.Success, Is.True, created.ErrorMessage);
	}
}
