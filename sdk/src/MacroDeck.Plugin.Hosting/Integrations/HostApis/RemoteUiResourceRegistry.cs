using System.Text.Json;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Callbacks.Ui;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Resources;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

internal sealed class RemoteUiResourceRegistry(IHostInvoker invoker, IPluginAssetUploader uploader)
	: IUiResourceRegistry, IDisposable
{
	// One at a time: the host allows only a few uploads in flight per plugin, shared with icon and
	// artwork uploads, and refuses the rest outright.
	private readonly SemaphoreSlim _gate = new(1, 1);

	public async Task<UiResource> RegisterAsync(string name,
		ReadOnlyMemory<byte> content,
		string mediaType,
		CancellationToken cancellationToken = default)
	{
		ThrowIfInvalidName(name);

		if (!UiResourceRules.IsSupportedMediaType(mediaType))
		{
			var supported = string.Join(", ", UiResourceRules.SupportedMediaTypes);
			throw new ArgumentException($"'{mediaType}' is not a supported media type. Use one of: {supported}.",
				nameof(mediaType));
		}

		if (content.IsEmpty || content.Length > ProtocolLimits.MaxUiResourceBytes)
		{
			throw new ArgumentException(
				$"The content is {content.Length} bytes; a UI resource is 1 to {ProtocolLimits.MaxUiResourceBytes} bytes.",
				nameof(content));
		}

		var bytes = content.ToArray();
		var arguments = new UiRegisterResourceArguments
		{
			Name = name, ContentHash = AssetContentHash.Compute(bytes), MediaType = mediaType
		};

		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var result = await RegisterOnceAsync(arguments, cancellationToken).ConfigureAwait(false);

			if (result.UploadRequired)
			{
				await UploadAsync(mediaType, bytes, cancellationToken).ConfigureAwait(false);
				result = await RegisterOnceAsync(arguments, cancellationToken).ConfigureAwait(false);
			}

			return result.Resource is { } handle
				? new UiResource
				{
					ResourceId = handle.ResourceId,
					ContentHash = handle.ContentHash,
					MediaType = handle.MediaType,
					ByteLength = handle.ByteLength,
				}
				: throw new UiResourceException(UiResourceErrorCode.Failed,
					$"Macro Deck did not accept the bytes uploaded for UI resource '{name}'.");
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task RemoveAsync(string name, CancellationToken cancellationToken = default)
	{
		ThrowIfInvalidName(name);

		try
		{
			await invoker.InvokeAsync(Protocol.Callbacks.HostApis.Ui,
					HostOperations.Ui.RemoveResource,
					new UiRemoveResourceArguments { Name = name },
					cancellationToken)
				.ConfigureAwait(false);
		}
		catch (HostInvocationException exception)
		{
			throw Translate(exception);
		}
	}

	public void Dispose() => _gate.Dispose();

	private async Task<UiRegisterResourceResult> RegisterOnceAsync(UiRegisterResourceArguments arguments,
		CancellationToken cancellationToken)
	{
		JsonElement? result;
		try
		{
			result = await invoker.InvokeAsync(Protocol.Callbacks.HostApis.Ui,
					HostOperations.Ui.RegisterResource,
					arguments,
					cancellationToken)
				.ConfigureAwait(false);
		}
		catch (HostInvocationException exception)
		{
			throw Translate(exception);
		}

		return result?.Deserialize<UiRegisterResourceResult>(PluginProtocolJson.Options) ??
			throw new UiResourceException(UiResourceErrorCode.Failed,
				$"Macro Deck answered the registration of UI resource '{arguments.Name}' without a result.");
	}

	private async Task UploadAsync(string mediaType, byte[] bytes, CancellationToken cancellationToken)
	{
		try
		{
			await uploader.UploadAsync(AssetKinds.UiResource, mediaType, bytes, cancellationToken).ConfigureAwait(false);
		}
		catch (AssetUploadException exception)
		{
			throw new UiResourceException(UiResourceErrorCode.Failed,
				$"Uploading a UI resource failed: {exception.Message}",
				exception);
		}
	}

	private static void ThrowIfInvalidName(string name)
	{
		if (!UiResourceRules.IsValidName(name))
		{
			throw new ArgumentException(
				$"'{name}' is not a valid UI resource name: use a letter or digit followed by up to 63 letters, " +
				"digits, hyphens or underscores.",
				nameof(name));
		}
	}

	private static UiResourceException Translate(HostInvocationException exception)
	{
		var code = exception.Code switch
		{
			ProtocolErrorCodes.CapabilityUnsupported => UiResourceErrorCode.Unsupported,
			ProtocolErrorCodes.UiResourceQuotaExceeded => UiResourceErrorCode.QuotaExceeded,
			ProtocolErrorCodes.RateLimited => UiResourceErrorCode.RateLimited,
			_ => UiResourceErrorCode.Failed,
		};

		return new UiResourceException(code, exception.Message, exception);
	}
}
