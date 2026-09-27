using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Callbacks.IconPacks;
using MacroDeck.Plugin.Protocol.Callbacks.Ui;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Ui;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class RemoteUiResourceRegistryTests
{
	private static readonly byte[] _png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

	[Test]
	public async Task New_bytes_are_uploaded_once_and_the_hosts_handle_is_returned()
	{
		var host = new FakeResourceHost();
		var registry = new RemoteUiResourceRegistry(host, host);

		var handle = await registry.RegisterAsync("photo", _png, "image/png");

		Assert.Multiple(() =>
		{
			Assert.That(host.Uploads, Has.Count.EqualTo(1));
			Assert.That(host.Uploads[0].Kind, Is.EqualTo(AssetKinds.UiResource));
			Assert.That(handle.ResourceId, Is.EqualTo("plugin-x.photo"));
			Assert.That(handle.ContentHash, Is.EqualTo(AssetContentHash.Compute(_png)));
			Assert.That(handle.MediaType, Is.EqualTo("image/png"));
			Assert.That(handle.ByteLength, Is.EqualTo(_png.Length));
		});
	}

	[Test]
	public async Task Bytes_the_host_already_holds_under_that_name_are_not_uploaded_again()
	{
		var host = new FakeResourceHost();
		var registry = new RemoteUiResourceRegistry(host, host);
		await registry.RegisterAsync("photo", _png, "image/png");

		await registry.RegisterAsync("photo", _png, "image/png");

		Assert.That(host.Uploads, Has.Count.EqualTo(1));
	}

	[Test]
	public void A_host_that_still_wants_the_bytes_after_the_upload_fails_the_registration_without_looping()
	{
		var host = new FakeResourceHost { ForgetUploads = true };
		var registry = new RemoteUiResourceRegistry(host, host);

		var exception = Assert.ThrowsAsync<UiResourceException>(() => registry.RegisterAsync("photo", _png, "image/png"));

		Assert.Multiple(() =>
		{
			Assert.That(exception!.ErrorCode, Is.EqualTo(UiResourceErrorCode.Failed));
			Assert.That(host.Uploads, Has.Count.EqualTo(1));
		});
	}

	[TestCase(ProtocolErrorCodes.CapabilityUnsupported, UiResourceErrorCode.Unsupported)]
	[TestCase(ProtocolErrorCodes.UiResourceQuotaExceeded, UiResourceErrorCode.QuotaExceeded)]
	[TestCase(ProtocolErrorCodes.RateLimited, UiResourceErrorCode.RateLimited)]
	[TestCase(ProtocolErrorCodes.SessionNotFound, UiResourceErrorCode.Failed)]
	[TestCase(ProtocolErrorCodes.InternalError, UiResourceErrorCode.Failed)]
	public void A_refusal_from_the_host_surfaces_as_its_error_code(string wireCode, UiResourceErrorCode expected)
	{
		var host = new FakeResourceHost { RefuseWith = wireCode };
		var registry = new RemoteUiResourceRegistry(host, host);

		var exception = Assert.ThrowsAsync<UiResourceException>(() => registry.RegisterAsync("photo", _png, "image/png"));

		Assert.That(exception!.ErrorCode, Is.EqualTo(expected));
	}

	[Test]
	public void A_host_without_resource_registration_is_reported_before_any_bytes_are_sent()
	{
		var host = new FakeResourceHost { RefuseWith = ProtocolErrorCodes.CapabilityUnsupported };
		var registry = new RemoteUiResourceRegistry(host, host);

		Assert.ThrowsAsync<UiResourceException>(() => registry.RegisterAsync("photo", _png, "image/png"));

		Assert.That(host.Uploads, Is.Empty);
	}

	[TestCase("", "image/png", 3)]
	[TestCase("has.dot", "image/png", 3)]
	[TestCase("photo", "image/svg+xml", 3)]
	[TestCase("photo", "text/html", 3)]
	[TestCase("photo", "image/png", 0)]
	[TestCase("photo", "image/png", ProtocolLimits.MaxUiResourceBytes + 1)]
	public void An_invalid_registration_throws_before_anything_is_sent(string name, string mediaType, int length)
	{
		var host = new FakeResourceHost();
		var registry = new RemoteUiResourceRegistry(host, host);

		Assert.ThrowsAsync<ArgumentException>(() => registry.RegisterAsync(name, new byte[length], mediaType));

		Assert.That(host.Invocations, Is.Zero);
	}

	[Test]
	public async Task Concurrent_registrations_all_succeed_with_one_upload_in_flight_at_a_time()
	{
		var host = new FakeResourceHost { UploadDelay = TimeSpan.FromMilliseconds(2) };
		var registry = new RemoteUiResourceRegistry(host, host);

		var handles = await Task.WhenAll(Enumerable.Range(0, 50)
			.Select(index => registry.RegisterAsync($"photo-{index}", (byte[])[.. _png, (byte)index], "image/jpeg")));

		Assert.Multiple(() =>
		{
			Assert.That(handles.Select(handle => handle.ResourceId).Distinct().Count(), Is.EqualTo(50));
			Assert.That(host.Uploads, Has.Count.EqualTo(50));
			Assert.That(host.MaxConcurrentUploads, Is.EqualTo(1));
		});
	}

	[Test]
	public void A_cancelled_registration_throws_OperationCanceledException()
	{
		var host = new FakeResourceHost();
		var registry = new RemoteUiResourceRegistry(host, host);

		Assert.That(async () => await registry.RegisterAsync("photo", _png, "image/png", new CancellationToken(true)),
			Throws.InstanceOf<OperationCanceledException>());
	}

	[Test]
	public async Task Removing_sends_the_name_to_the_host()
	{
		var host = new FakeResourceHost();
		var registry = new RemoteUiResourceRegistry(host, host);

		await registry.RemoveAsync("photo");

		Assert.That(host.Removed, Is.EqualTo(new[] { "photo" }));
	}

	[Test]
	public async Task A_bundled_icon_is_looked_up_by_key_and_name_and_answers_the_hosts_handle_without_an_upload()
	{
		var host = new FakeIconHost
		{
			Handle = new UiResourceHandleDto
			{
				ResourceId = "app.macro-deck.plugin-icon.1", ContentHash = "hash-1", MediaType = "image/png",
				ByteLength = 42,
			}
		};
		var uploader = new FakeResourceHost();
		var registry = new RemoteUiResourceRegistry(host, uploader);

		var handle = await registry.GetPluginIconAsync("logos", "spotify");

		Assert.Multiple(() =>
		{
			Assert.That(host.Calls,
				Is.EqualTo((List<(string, string)>)[(HostApis.IconPacks, HostOperations.IconPacks.GetIconResource)]));
			Assert.That(host.LastArguments, Is.EqualTo(new GetIconResourceArguments { Key = "logos", Name = "spotify" }));
			Assert.That(handle.ResourceId, Is.EqualTo("app.macro-deck.plugin-icon.1"));
			Assert.That(handle.ContentHash, Is.EqualTo("hash-1"));
			Assert.That(handle.MediaType, Is.EqualTo("image/png"));
			Assert.That(handle.ByteLength, Is.EqualTo(42));
			Assert.That(uploader.Uploads, Is.Empty);
		});
	}

	[TestCase(ProtocolErrorCodes.PluginIconNotFound, UiResourceErrorCode.PluginIconNotFound)]
	[TestCase(ProtocolErrorCodes.CapabilityUnsupported, UiResourceErrorCode.Unsupported)]
	[TestCase(ProtocolErrorCodes.AssetTooLarge, UiResourceErrorCode.Failed)]
	[TestCase(ProtocolErrorCodes.RateLimited, UiResourceErrorCode.RateLimited)]
	[TestCase(ProtocolErrorCodes.InternalError, UiResourceErrorCode.Failed)]
	public void A_refused_bundled_icon_lookup_surfaces_as_its_error_code(string wireCode, UiResourceErrorCode expected)
	{
		var host = new FakeIconHost { RefuseWith = wireCode };
		var registry = new RemoteUiResourceRegistry(host, new FakeResourceHost());

		var exception = Assert.ThrowsAsync<UiResourceException>(() => registry.GetPluginIconAsync("logos", "spotify"));

		Assert.That(exception!.ErrorCode, Is.EqualTo(expected));
	}

	[Test]
	public void A_bundled_icon_lookup_answered_without_a_handle_fails()
	{
		var registry = new RemoteUiResourceRegistry(new FakeIconHost(), new FakeResourceHost());

		var exception = Assert.ThrowsAsync<UiResourceException>(() => registry.GetPluginIconAsync("logos", "spotify"));

		Assert.That(exception!.ErrorCode, Is.EqualTo(UiResourceErrorCode.Failed));
	}

	private sealed class FakeIconHost : IHostInvoker
	{
		public List<(string Api, string Operation)> Calls { get; } = [];

		public object? LastArguments { get; private set; }

		public UiResourceHandleDto? Handle { get; init; }

		public string? RefuseWith { get; init; }

		public Task<JsonElement?> InvokeAsync(string api,
			string operation,
			object? arguments,
			CancellationToken cancellationToken)
		{
			Calls.Add((api, operation));
			LastArguments = arguments;

			if (RefuseWith is { } code)
			{
				throw HostInvocationException.CreateNonRetryable(code, "refused");
			}

			return Task.FromResult<JsonElement?>(Handle is null
				? null
				: JsonSerializer.SerializeToElement(Handle, PluginProtocolJson.Options));
		}

		public bool TryComplete(ProtocolEnvelope result) => false;
	}

	private sealed class FakeResourceHost : IHostInvoker, IPluginAssetUploader
	{
		private readonly ConcurrentDictionary<string, byte> _uploaded = new(StringComparer.Ordinal);
		private readonly ConcurrentDictionary<string, string> _registered = new(StringComparer.Ordinal);
		private int _inFlight;
		private int _invocations;

		public ConcurrentQueue<string> Removed { get; } = new();

		public List<(string Kind, string MimeType, byte[] Data)> Uploads { get; } = [];

		public int Invocations => _invocations;

		public int MaxConcurrentUploads { get; private set; }

		public string? RefuseWith { get; init; }

		public bool ForgetUploads { get; init; }

		public TimeSpan UploadDelay { get; init; }

		public Task<JsonElement?> InvokeAsync(string api,
			string operation,
			object? arguments,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Interlocked.Increment(ref _invocations);

			if (RefuseWith is { } code)
			{
				throw HostInvocationException.CreateNonRetryable(code, "refused");
			}

			if (operation == HostOperations.Ui.RemoveResource)
			{
				Removed.Enqueue(((UiRemoveResourceArguments)arguments!).Name);
				return Task.FromResult<JsonElement?>(null);
			}

			var register = (UiRegisterResourceArguments)arguments!;
			var known = _registered.TryGetValue(register.Name, out var hash) && hash == register.ContentHash;

			if (!known && !_uploaded.ContainsKey(register.ContentHash))
			{
				return Result(new UiRegisterResourceResult { UploadRequired = true });
			}

			_registered[register.Name] = register.ContentHash;

			return Result(new UiRegisterResourceResult
			{
				Resource = new UiResourceHandleDto
				{
					ResourceId = "plugin-x." + register.Name,
					ContentHash = register.ContentHash,
					MediaType = register.MediaType,
					ByteLength = _png.Length,
				}
			});
		}

		public Task<JsonElement?> InvokeAsync(string api,
			string operation,
			object? arguments,
			TimeSpan timeout,
			CancellationToken cancellationToken)
			=> InvokeAsync(api, operation, arguments, cancellationToken);

		public Task<JsonElement?> InvokeAsync(string api,
			string operation,
			object? arguments,
			TimeSpan deadline,
			TimeSpan timeout,
			CancellationToken cancellationToken)
			=> InvokeAsync(api, operation, arguments, cancellationToken);

		public async Task<string> UploadAsync(string kind, string mimeType, byte[] data, CancellationToken cancellationToken)
		{
			var inFlight = Interlocked.Increment(ref _inFlight);
			lock (Uploads)
			{
				MaxConcurrentUploads = Math.Max(MaxConcurrentUploads, inFlight);
				Uploads.Add((kind, mimeType, data));
			}

			await Task.Delay(UploadDelay, cancellationToken);
			Interlocked.Decrement(ref _inFlight);

			var hash = AssetContentHash.Compute(data);
			if (!ForgetUploads)
			{
				_uploaded[hash] = 0;
			}

			return hash;
		}

		public bool TryComplete(ProtocolEnvelope ack) => false;

		private static Task<JsonElement?> Result(UiRegisterResourceResult result)
			=> Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(result, PluginProtocolJson.Options));
	}
}
