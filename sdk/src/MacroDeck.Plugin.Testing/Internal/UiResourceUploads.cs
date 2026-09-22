using MacroDeck.Plugin.Protocol.Limits;

namespace MacroDeck.Plugin.Testing.Internal;

internal sealed class UiResourceUploads
{
	private readonly Lock _gate = new();
	private readonly OrderedDictionary<string, (byte[] Bytes, string MediaType)> _uploads = new(StringComparer.Ordinal);

	public void Add(string contentHash, byte[] bytes, string mediaType)
	{
		lock (_gate)
		{
			_uploads.Remove(contentHash);
			_uploads.Add(contentHash, (bytes, mediaType));

			while (_uploads.Count > ProtocolLimits.MaxUiResourcesPerPlugin ||
				_uploads.Values.Sum(upload => (long)upload.Bytes.Length) > ProtocolLimits.MaxUiResourceBytesPerPlugin)
			{
				_uploads.RemoveAt(0);
			}
		}
	}

	public bool TryTake(string contentHash, out byte[] bytes, out string mediaType)
	{
		lock (_gate)
		{
			if (_uploads.Remove(contentHash, out var upload))
			{
				(bytes, mediaType) = upload;
				return true;
			}
		}

		bytes = [];
		mediaType = string.Empty;
		return false;
	}
}
