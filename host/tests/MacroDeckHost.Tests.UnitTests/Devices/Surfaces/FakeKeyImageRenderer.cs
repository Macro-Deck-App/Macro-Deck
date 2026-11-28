using System.Text;
using System.Text.RegularExpressions;
using MacroDeckHost.Application.Devices.Surfaces.KeyImages;

namespace MacroDeckHost.Tests.UnitTests.Devices.Surfaces;

internal sealed partial class FakeKeyImageRenderer : IDeviceKeyImageRenderer
{
	public const string ContentType = "text/plain";

	private readonly Lock _gate = new();
	private readonly List<DeviceKeyImageRenderRequest> _requests = [];
	private readonly List<(string ResourceId, bool Found)> _lookups = [];

	public event EventHandler? FontsChanged;

	public HashSet<string> TickingTypes { get; } = new(StringComparer.Ordinal);

	public IReadOnlyList<DeviceKeyImageRenderRequest> Requests
	{
		get
		{
			lock (_gate)
			{
				return [.. _requests];
			}
		}
	}

	public IReadOnlyList<(string ResourceId, bool Found)> Lookups
	{
		get
		{
			lock (_gate)
			{
				return [.. _lookups];
			}
		}
	}

	public static string Describe(DeviceKeyImageRenderRequest request, int rowOffset = 0, int columnOffset = 0)
	{
		var builder = new StringBuilder()
			.Append(request.Kind)
			.Append('|')
			.Append(request.WidgetType)
			.Append('|')
			.Append(request.Tree?.GetProperty("root").GetRawText())
			.Append('|')
			.Append(request.WidgetData?.GetRawText())
			.Append('|')
			.Append(request.BackgroundColor)
			.Append('|')
			.Append(request.Appearance.ThemeMode)
			.Append('|')
			.Append(request.Culture)
			.Append('|')
			.Append(rowOffset)
			.Append(',')
			.Append(columnOffset);
		return builder.ToString();
	}

	public DeviceKeyImageRenderResult Render(DeviceKeyImageRenderRequest request)
	{
		lock (_gate)
		{
			_requests.Add(request);
		}

		var ticking = request.WidgetType is { } type && TickingTypes.Contains(type);
		var second = request.Now.ToUnixTimeSeconds();
		var shown = new StringBuilder();
		var missing = new List<DeviceKeyImageResourceRequest>();
		foreach (var resourceId in ResourceIdsOf(request))
		{
			var resource = request.Resources?.TryGet(resourceId, null);
			var found = resource is not null;
			lock (_gate)
			{
				_lookups.Add((resourceId, found));
			}

			shown.Append("|res:").Append(resourceId).Append(found ? $"=shown@{resource!.ContentHash}" : "=missing");
			if (!found)
			{
				missing.Add(new DeviceKeyImageResourceRequest(resourceId, null));
			}
		}

		var keys = new List<DeviceKeyImagePicture>();
		for (var row = 0; row < request.Rows; row++)
		{
			for (var column = 0; column < request.Columns; column++)
			{
				var text = Describe(request, row, column) + shown + (ticking ? $"|{second}" : string.Empty);
				keys.Add(new DeviceKeyImagePicture(row, column, Encoding.UTF8.GetBytes(text), ContentType));
			}
		}

		return new DeviceKeyImageRenderResult(keys,
			ticking ? DateTimeOffset.FromUnixTimeSeconds(second + 1) : null,
			missing);
	}

	private static IEnumerable<string> ResourceIdsOf(DeviceKeyImageRenderRequest request)
	{
		return request.Tree is { } tree
			? ResourceIdPattern()
				.Matches(tree.GetRawText())
				.Select(match => match.Groups[1].Value)
				.Distinct(StringComparer.Ordinal)
			: [];
	}

	[GeneratedRegex("\"resourceId\"\\s*:\\s*\"([^\"]+)\"")]
	private static partial Regex ResourceIdPattern();

	public void RaiseFontsChanged()
	{
		FontsChanged?.Invoke(this, EventArgs.Empty);
	}
}
