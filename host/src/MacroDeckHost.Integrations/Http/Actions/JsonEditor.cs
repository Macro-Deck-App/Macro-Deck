using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MacroDeckHost.Integrations.Http.Actions;

internal enum JsonEditStatus
{
	Succeeded,
	NotFound,
	Blocked,
	NotContainer,
	NotArray,
	RootNotRemovable
}

internal static class JsonEditor
{
	private static readonly JsonSerializerOptions _output =
		new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

	public static bool TryParse(string? json, out JsonNode? node)
	{
		node = null;
		if (string.IsNullOrWhiteSpace(json))
		{
			return false;
		}

		try
		{
			node = JsonNode.Parse(json);
			Materialize(node);
			return true;
		}
		catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
		{
			node = null;
			return false;
		}
	}

	public static string Serialize(JsonNode? node)
	{
		return node is null ? "null" : node.ToJsonString(_output);
	}

	public static JsonEditStatus Set(
		JsonNode? root,
		IReadOnlyList<JsonPathSegment> path,
		JsonNode? value,
		out JsonNode? result)
	{
		result = root;
		if (path.Count == 0)
		{
			result = value;
			return JsonEditStatus.Succeeded;
		}

		var status = TryWalk(root, path, path.Count - 1, true, out var parent);
		if (status != JsonEditStatus.Succeeded)
		{
			return status;
		}

		var last = path[^1];
		switch (parent)
		{
			case JsonObject obj when !last.IsIndex:
				obj[last.Property!] = value;
				return JsonEditStatus.Succeeded;
			case JsonArray array when last.IsIndex:
				if (last.Index >= array.Count)
				{
					return JsonEditStatus.NotFound;
				}

				array[last.Index] = value;
				return JsonEditStatus.Succeeded;
			default:
				return JsonEditStatus.Blocked;
		}
	}

	public static JsonEditStatus Remove(JsonNode? root, IReadOnlyList<JsonPathSegment> path)
	{
		if (path.Count == 0)
		{
			return JsonEditStatus.RootNotRemovable;
		}

		var status = TryWalk(root, path, path.Count - 1, false, out var parent);
		if (status != JsonEditStatus.Succeeded)
		{
			return status;
		}

		var last = path[^1];
		switch (parent)
		{
			case JsonObject obj when !last.IsIndex:
				return obj.Remove(last.Property!) ? JsonEditStatus.Succeeded : JsonEditStatus.NotFound;
			case JsonArray array when last.IsIndex:
				if (last.Index >= array.Count)
				{
					return JsonEditStatus.NotFound;
				}

				array.RemoveAt(last.Index);
				return JsonEditStatus.Succeeded;
			default:
				return JsonEditStatus.Blocked;
		}
	}

	public static JsonEditStatus Count(JsonNode? root, IReadOnlyList<JsonPathSegment> path, out int count)
	{
		count = 0;
		var status = TryWalk(root, path, path.Count, false, out var target);
		if (status != JsonEditStatus.Succeeded)
		{
			return status;
		}

		switch (target)
		{
			case JsonArray array:
				count = array.Count;
				return JsonEditStatus.Succeeded;
			case JsonObject obj:
				count = obj.Count;
				return JsonEditStatus.Succeeded;
			default:
				return JsonEditStatus.NotContainer;
		}
	}

	public static JsonEditStatus Append(
		JsonNode? root,
		IReadOnlyList<JsonPathSegment> path,
		JsonNode? item,
		out JsonNode? result)
	{
		result = root;
		if (path.Count == 0)
		{
			if (root is not JsonArray rootArray)
			{
				return JsonEditStatus.NotArray;
			}

			rootArray.Add(item);
			return JsonEditStatus.Succeeded;
		}

		var status = TryWalk(root, path, path.Count - 1, true, out var parent);
		if (status != JsonEditStatus.Succeeded)
		{
			return status;
		}

		var last = path[^1];
		JsonNode? target;
		switch (parent)
		{
			case JsonObject obj when !last.IsIndex:
				if (!obj.TryGetPropertyValue(last.Property!, out target))
				{
					target = new JsonArray();
					obj[last.Property!] = target;
				}

				break;
			case JsonArray array when last.IsIndex:
				if (last.Index >= array.Count)
				{
					return JsonEditStatus.NotFound;
				}

				target = array[last.Index];
				break;
			default:
				return JsonEditStatus.Blocked;
		}

		if (target is not JsonArray targetArray)
		{
			return JsonEditStatus.NotArray;
		}

		targetArray.Add(item);
		return JsonEditStatus.Succeeded;
	}

	private static JsonEditStatus TryWalk(
		JsonNode? root,
		IReadOnlyList<JsonPathSegment> path,
		int length,
		bool createObjects,
		out JsonNode? node)
	{
		node = root;
		for (var i = 0; i < length; i++)
		{
			var segment = path[i];
			switch (node)
			{
				case JsonObject obj when !segment.IsIndex:
					if (obj.TryGetPropertyValue(segment.Property!, out var child))
					{
						node = child;
						continue;
					}

					if (!createObjects)
					{
						return JsonEditStatus.NotFound;
					}

					var created = new JsonObject();
					obj[segment.Property!] = created;
					node = created;
					continue;
				case JsonArray array when segment.IsIndex:
					if (segment.Index >= array.Count)
					{
						return JsonEditStatus.NotFound;
					}

					node = array[segment.Index];
					continue;
				default:
					return JsonEditStatus.Blocked;
			}
		}

		return JsonEditStatus.Succeeded;
	}

	private static void Materialize(JsonNode? node)
	{
		switch (node)
		{
			case JsonObject obj:
				foreach (var property in obj)
				{
					Materialize(property.Value);
				}

				break;
			case JsonArray array:
				foreach (var item in array)
				{
					Materialize(item);
				}

				break;
		}
	}
}
