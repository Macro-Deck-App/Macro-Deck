using System.Net;
using System.Text.Json;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Persistence.Profiles;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Persistence;

// A change the disk refused is not saved, so the API must not report it as saved, and what the host shows
// afterwards must be what a restart would load (MD3#647).
[NonParallelizable]
public class ProfileWriteFailureTests
{
	private InProcessHost _host = null!;
	private SwitchableProfileStore _store = null!;
	private string _profileId = null!;
	private string _folderId = null!;
	private string _widgetId = null!;

	[SetUp]
	public async Task SetUp()
	{
		_host = await InProcessHost.StartAsync(services =>
		{
			services.RemoveAll<IProfileStore>();
			services.AddSingleton<IProfileStore>(provider => _store = new SwitchableProfileStore(
				ActivatorUtilities.CreateInstance<JsonProfileStore>(provider)));
		});
		await _host.Services.GetRequiredService<IProfileCache>().InitializeCache();
		await _host.Services.GetRequiredService<IFolderCache>().InitializeCache();

		var profile = await PostJson("/api/profiles", new { name = "Studio" });
		_profileId = profile.GetProperty("profile").GetProperty("id").GetString()!;
		var folders = await InProcessHost.ReadJsonAsync(
			await _host.SendAsync(HttpMethod.Get, $"/api/folders?profileId={_profileId}", loopback: true));
		_folderId = folders.GetProperty("folders")[0].GetProperty("id").GetString()!;
		var widget = await PostJson("/api/widgets",
			new { folderId = _folderId, type = "ActionButton", positionX = 0, positionY = 0, width = 1, height = 1 });
		_widgetId = widget.GetProperty("widget").GetProperty("id").GetString()!;
	}

	[TearDown]
	public async Task TearDown()
	{
		await _host.DisposeAsync();
	}

	[TestCase("rename profile")]
	[TestCase("rename folder")]
	[TestCase("create folder")]
	[TestCase("create widget")]
	[TestCase("move widget")]
	[TestCase("delete widget")]
	public async Task A_refused_write_is_reported_and_leaves_what_a_restart_would_load(string mutation)
	{
		var before = DiskState();
		_store.Fail = true;

		var response = await Mutate(mutation);
		_store.Fail = false;

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(await ReportedSuccess(response), Is.False, "the API reported a refused write as saved");
			Assert.That(DiskState(), Is.EqualTo(before), "the refused change reached the disk");
			Assert.That(LiveState(), Is.EqualTo(before), "the host shows a change a restart would lose");
		});
	}

	[TestCase("rename profile")]
	[TestCase("rename folder")]
	[TestCase("create folder")]
	[TestCase("create widget")]
	[TestCase("move widget")]
	[TestCase("delete widget")]
	public async Task A_write_the_disk_accepts_is_reported_and_loaded_after_a_restart(string mutation)
	{
		var before = DiskState();

		var response = await Mutate(mutation);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(await ReportedSuccess(response), Is.True);
			Assert.That(DiskState(), Is.Not.EqualTo(before));
			Assert.That(LiveState(), Is.EqualTo(DiskState()));
		});
	}

	[Test]
	public async Task A_refused_change_is_not_written_out_by_the_next_successful_save()
	{
		_store.Fail = true;
		await Mutate("rename folder");
		_store.Fail = false;

		await _host.SendAsync(HttpMethod.Put, "/api/profiles", new { id = _profileId, name = "Booth" }, loopback: true);

		Assert.That(DiskState(), Does.Not.Contain("Renamed folder"));
	}

	private Task<HttpResponseMessage> Mutate(string mutation)
	{
		return mutation switch
		{
			"rename profile" => _host.SendAsync(HttpMethod.Put,
				"/api/profiles",
				new { id = _profileId, name = "Renamed profile" },
				loopback: true),
			"rename folder" => _host.SendAsync(HttpMethod.Put,
				"/api/folders",
				new { id = _folderId, name = "Renamed folder" },
				loopback: true),
			"create folder" => _host.SendAsync(HttpMethod.Post,
				"/api/folders",
				new { profileId = _profileId, name = "New folder", parentId = _folderId },
				loopback: true),
			"create widget" => _host.SendAsync(HttpMethod.Post,
				"/api/widgets",
				new { folderId = _folderId, type = "ActionButton", positionX = 2, positionY = 2, width = 1, height = 1 },
				loopback: true),
			"move widget" => _host.SendAsync(HttpMethod.Put,
				"/api/widgets/positions",
				new
				{
					folderId = _folderId,
					positions = new[] { new { id = _widgetId, positionX = 3, positionY = 1, width = 1, height = 1 } }
				},
				loopback: true),
			"delete widget" => _host.SendAsync(HttpMethod.Delete,
				$"/api/widgets/{_widgetId}?folderId={_folderId}",
				loopback: true),
			_ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null)
		};
	}

	private async Task<JsonElement> PostJson(string path, object body)
	{
		var response = await _host.SendAsync(HttpMethod.Post, path, body, loopback: true);
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), path);
		return await InProcessHost.ReadJsonAsync(response);
	}

	private static async Task<bool> ReportedSuccess(HttpResponseMessage response)
	{
		if (!response.IsSuccessStatusCode)
		{
			return false;
		}

		var body = await response.Content.ReadAsStringAsync();
		using var document = JsonDocument.Parse(body);
		return !document.RootElement.TryGetProperty("success", out var success) || success.GetBoolean();
	}

	private static string DiskState()
	{
		using var fresh = new ProfileCache(new JsonProfileStore(new MacroDeckPaths(), Log.Logger), Log.Logger);
		fresh.InitializeCache().GetAwaiter().GetResult();
		return Describe(fresh);
	}

	private string LiveState()
	{
		return Describe((ProfileCache)_host.Services.GetRequiredService<IProfileCache>());
	}

	private static string Describe(ProfileCache cache)
	{
		var files = cache.GetAll()
			.OrderBy(profile => profile.Id)
			.Select(profile => ProfileFileMapper.ToFile(profile, cache.GetFoldersByProfileId(profile.Id)));
		return JsonSerializer.Serialize(files);
	}

	private sealed class SwitchableProfileStore(IProfileStore inner) : IProfileStore
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
