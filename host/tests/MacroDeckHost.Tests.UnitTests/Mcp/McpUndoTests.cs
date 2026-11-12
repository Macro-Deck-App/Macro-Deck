using System.Text.Json;
using MacroDeckHost.Application.Mcp;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;

namespace MacroDeckHost.Tests.UnitTests.Mcp;

public partial class McpEndpointTests
{
	[Test]
	public async Task A_deleted_widget_can_be_undone_after_more_batches_than_the_history_keeps()
	{
		await using var client = await ConnectWithToken();
		var folderId = await CreateFolder(client);
		var widgetId = await CreateWidget(client, folderId, null);
		var delete = await Apply(client, [Change("delete", "widget", widgetId.ToString())]);
		await ApplyFillers(client, McpJournal.Capacity + 5);

		var undo = await client.CallToolAsync("undo",
			new Dictionary<string, object?> { ["batchId"] = delete.GetProperty("batchId").GetString() });

		await Assert.MultipleAsync(() =>
		{
			Assert.That(undo.IsError, Is.Not.True, JsonSerializer.Serialize(undo));
			Assert.That(FindWidget(widgetId)?.FolderId, Is.EqualTo(folderId));
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task A_secret_kept_for_undo_survives_later_batches_and_is_scrubbed_once_its_batch_leaves_the_history()
	{
		await using var client = await ConnectWithToken();
		var folderId = await CreateFolder(client);
		var secretId = await SetSecret(client);
		var widgetId = await CreateWidget(client, folderId, Flow(secretId));
		var ownSecret = Application.Secrets.SecretReferences.Extract(FindWidget(widgetId)!.Data).Single();
		await Apply(client, [Change("delete", "widget", widgetId.ToString())]);

		await ScrubDeferred();
		await ApplyFillers(client, McpJournal.Capacity);
		var afterFillers = await Resolve(ownSecret);

		var variables = await Apply(client,
			Enumerable.Range(0, McpJournal.DeleteCapacity)
				.Select(index => Change("create",
					"variable",
					reference: $"v{index}",
					data: new { name = $"doomed_{Guid.NewGuid():N}", value = "x" }))
				.ToArray());
		foreach (var variable in variables.GetProperty("createdIds").EnumerateObject())
		{
			await Apply(client, [Change("delete", "variable", variable.Value.GetString())]);
		}

		var afterEviction = await Resolve(ownSecret);

		await Assert.MultipleAsync(() =>
		{
			Assert.That(afterFillers, Is.EqualTo(SecretValue));
			Assert.That(afterEviction, Is.Null);
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task The_secret_of_an_expired_delete_batch_is_scrubbed_without_a_later_batch()
	{
		await using var client = await ConnectWithToken();
		var folderId = await CreateFolder(client);
		var secretId = await SetSecret(client);
		var widgetId = await CreateWidget(client, folderId, Flow(secretId));
		var ownSecret = Application.Secrets.SecretReferences.Extract(FindWidget(widgetId)!.Data).Single();
		var delete = await Apply(client, [Change("delete", "widget", widgetId.ToString())]);
		var batchId = Guid.Parse(delete.GetProperty("batchId").GetString()!);

		McpJournalBatch? afterExpiry;
		string? secretAfterExpiry;
		_clock.Offset = McpJournal.DeleteRetention + TimeSpan.FromDays(1);
		try
		{
			await ScrubDeferred();
			secretAfterExpiry = await Resolve(ownSecret);
			using var scope = _host.Services.CreateScope();
			afterExpiry = scope.ServiceProvider.GetRequiredService<IMcpJournal>().Find(batchId);
		}
		finally
		{
			_clock.Offset = TimeSpan.Zero;
		}

		await Assert.MultipleAsync(() =>
		{
			Assert.That(afterExpiry, Is.Null);
			Assert.That(secretAfterExpiry, Is.Null);
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task Undoing_a_created_profile_keeps_the_secrets_of_its_widgets_while_their_batch_can_be_undone()
	{
		await using var client = await ConnectWithToken();
		await Apply(client, [Change("create", "profile", data: new { name = "Kept" })]);
		var profileBatch = await Apply(client,
			[Change("create", "profile", reference: "p", data: new { name = "Undone" })]);
		var profileId = Guid.Parse(profileBatch.GetProperty("createdIds").GetProperty("p").GetString()!);
		var folderId = Folders().GetFoldersByProfileId(profileId).Single().Id;
		var secretId = await SetSecret(client);
		var widgetId = await CreateWidget(client, folderId, Flow(secretId));
		var ownSecret = Application.Secrets.SecretReferences.Extract(FindWidget(widgetId)!.Data).Single();

		var undo = await client.CallToolAsync("undo",
			new Dictionary<string, object?> { ["batchId"] = profileBatch.GetProperty("batchId").GetString() });

		Assert.That(undo.IsError, Is.Not.True, JsonSerializer.Serialize(undo));
		Assert.That(FindWidget(widgetId), Is.Null, JsonSerializer.Serialize(undo));
		Assert.That(await Resolve(ownSecret), Is.EqualTo(SecretValue));
	}

	[Test]
	public async Task Undoing_a_folder_delete_restores_its_whole_subtree_and_the_start_folder()
	{
		await using var client = await ConnectWithToken();
		var batch = await Apply(client,
		[
			Change("create", "profile", reference: "p", data: new { name = "Subtree" }),
			Change("create", "folder", reference: "a", data: new { profileId = "@p", name = "A" }),
			Change("create", "folder", reference: "b", data: new { profileId = "@p", name = "B" }),
			Change("create", "folder", reference: "s", data: new { profileId = "@p", name = "S", parentId = "@a" })
		]);
		var ids = batch.GetProperty("createdIds");
		var a = Guid.Parse(ids.GetProperty("a").GetString()!);
		var b = Guid.Parse(ids.GetProperty("b").GetString()!);
		var s = Guid.Parse(ids.GetProperty("s").GetString()!);
		var widgetId = await CreateWidget(client, s, null);
		Guid variableId;
		using (var scope = _host.Services.CreateScope())
		{
			variableId = (await scope.ServiceProvider.GetRequiredService<IVariableService>()
					.CreateUserVariable("subtree_state",
						VariableScope.Widget,
						widgetId.ToString(),
						VariableType.Text,
						"on",
						null))
				.Data!.Id;
		}

		var profileId = Folders().GetFolderById(a)!.ProfileId;
		await MakeStartFolder(profileId, a);
		var startFoldersBefore = StartFolders(profileId);
		await Apply(client, [Change("delete", "folder", a.ToString())]);
		var anotherTookOver = Folders().GetFoldersByProfileId(profileId).Any(folder => folder.IsDefault);
		var undo = await client.CallToolAsync("undo");

		using var check = _host.Services.CreateScope();
		var variable = await check.ServiceProvider.GetRequiredService<IVariableService>().GetById(variableId);
		await Assert.MultipleAsync(() =>
		{
			Assert.That(undo.StructuredContent!.Value.GetProperty("problems").GetArrayLength(), Is.Zero);
			Assert.That(anotherTookOver, Is.True);
			Assert.That(StartFolders(profileId), Is.EqualTo(startFoldersBefore));
			Assert.That(Folders().GetFolderById(b), Is.Not.Null);
			Assert.That(Folders().GetFolderById(s)?.ParentId, Is.EqualTo(a));
			Assert.That(FindWidget(widgetId)?.FolderId, Is.EqualTo(s));
			Assert.That(variable?.Value, Is.EqualTo("on"));
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task Undoing_a_folder_delete_keeps_a_start_folder_the_user_picked_after_the_batch()
	{
		await using var client = await ConnectWithToken();
		var batch = await Apply(client,
		[
			Change("create", "profile", reference: "p", data: new { name = "Picked" }),
			Change("create", "folder", reference: "a", data: new { profileId = "@p", name = "A" }),
			Change("create", "folder", reference: "b", data: new { profileId = "@p", name = "B" })
		]);
		var a = Guid.Parse(batch.GetProperty("createdIds").GetProperty("a").GetString()!);
		var profileId = Folders().GetFolderById(a)!.ProfileId;
		await MakeStartFolder(profileId, a);
		await Apply(client, [Change("delete", "folder", a.ToString())]);
		var picked = Folders().GetFoldersByProfileId(profileId).First(folder => !folder.IsDefault).Id;
		await MakeStartFolder(profileId, picked);

		var undo = await client.CallToolAsync("undo");

		await Assert.MultipleAsync(() =>
		{
			Assert.That(Codes(undo.StructuredContent!.Value), Does.Contain(McpProblemCodes.EntityChangedSince));
			Assert.That(Folders().GetFolderById(a)?.IsDefault, Is.False);
			Assert.That(Folders().GetFolderById(picked)?.IsDefault, Is.True);
			Assert.That(Folders().GetFoldersByProfileId(profileId).Count(folder => folder.IsDefault), Is.EqualTo(1));
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task Concurrent_batches_undos_and_secret_grants_lose_nothing()
	{
		await EnsureTestIntegration();
		var token = await CreateToken();
		var tokenId = token.Id;
		await using var client = await Connect(token.Plaintext, LegacyProtocol, TypingHandler());

		var applies = Enumerable.Range(0, 6)
			.Select(_ => Apply(client,
				[Change("create", "variable", data: new { name = $"race_{Guid.NewGuid():N}", value = "x" })]))
			.ToList();
		var secrets = Enumerable.Range(0, 4).Select(_ => SetSecret(client)).ToList();
		var results = await Task.WhenAll(applies);
		var undos = results.Take(3)
			.Select(result => client.CallToolAsync("undo",
					new Dictionary<string, object?> { ["batchId"] = result.GetProperty("batchId").GetString() })
				.AsTask())
			.ToList();
		var secretIds = await Task.WhenAll(secrets);
		var undone = await Task.WhenAll(undos);

		using var scope = _host.Services.CreateScope();
		var ledger = scope.ServiceProvider.GetRequiredService<IMcpSecretLedger>();
		var journal = scope.ServiceProvider.GetRequiredService<IMcpJournal>();
		await Assert.MultipleAsync(() =>
		{
			Assert.That(results.Select(result => result.GetProperty("applied").GetBoolean()), Is.All.True);
			Assert.That(undone.Select(result => result.IsError == true), Is.All.False);
			Assert.That(secretIds.Select(id => ledger.IsGranted(tokenId, id)), Is.All.True);
			Assert.That(results.Select(result => journal.Find(Guid.Parse(result.GetProperty("batchId").GetString()!))),
				Is.All.Not.Null);
			return Task.CompletedTask;
		});
	}

	private async Task MakeStartFolder(Guid profileId, Guid folderId)
	{
		var folders = Folders().GetFoldersByProfileId(profileId);
		foreach (var folder in folders)
		{
			folder.IsDefault = folder.Id == folderId;
		}

		await Folders().AddOrUpdateRange(folders);
	}

	private Dictionary<Guid, bool> StartFolders(Guid profileId)
	{
		return Folders().GetFoldersByProfileId(profileId).ToDictionary(folder => folder.Id, folder => folder.IsDefault);
	}

	private static async Task ApplyFillers(McpClient client, int count)
	{
		for (var batch = 0; batch < count; batch++)
		{
			await Apply(client,
				[Change("create", "variable", data: new { name = $"filler_{Guid.NewGuid():N}", value = "x" })]);
		}
	}

	private async Task ScrubDeferred()
	{
		using var scope = _host.Services.CreateScope();
		await scope.ServiceProvider.GetRequiredService<IMcpSecretLedger>().ScrubDeferred();
	}

	private sealed class ShiftedClock : TimeProvider
	{
		public TimeSpan Offset { get; set; }

		public override DateTimeOffset GetUtcNow()
		{
			return System.GetUtcNow() + Offset;
		}
	}
}
