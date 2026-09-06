using System.Text.Json;
using MacroDeck.Plugin.Protocol.Limits;

namespace MacroDeckHost.Application.Ui.Sessions;

public enum UiPayloadShape
{
	Tree,
	Patch
}

public readonly record struct UiPayloadScan
{
	public required bool Ok { get; init; }

	public string? Code { get; init; }

	public int FromRevision { get; init; }

	public int ToRevision { get; init; }

	public int OperationCount { get; init; }

	// Nodes carried by the payload: every node of a tree, or the nodes an insert or replace
	// operation brings with it.
	public int CarriedNodes { get; init; }

	// How many remove-node operations the patch contains. Not how many nodes they
	// remove - a relay that holds no tree cannot know a removed subtree's size.
	public int RemoveOperations { get; init; }

	public static UiPayloadScan Failed(string code) => new() { Ok = false, Code = code };
}

// One forward-only pass over the bytes the provider sent, which are then relayed unchanged: no
// JsonDocument, no binding to UiTree/UiPatch, no second serialization. Structure is
// tracked by container role rather than by property name alone, because a node's properties bag
// is arbitrary provider JSON that may itself contain a key called children - and, symmetrically,
// because a resource is by definition an object inside a node's properties, so a resourceId and
// a byteLength that meet anywhere else are two ordinary provider members, not a declaration this
// host may terminate a session over.
public static class UiPayloadValidator
{
	private const int MaxTrackedDepth = ProtocolLimits.MaxJsonDepth + 2;

	private enum Role
	{
		Opaque,
		TreeDocument,
		PatchDocument,
		Node,
		NodeProperties,
		ChildrenArray,
		OperationsArray,
		Operation
	}

	private enum Scalar
	{
		None,
		Revision,
		FromRevision,
		ToRevision,
		Op,
		ByteLength
	}

	public static UiPayloadScan Scan(ReadOnlySpan<byte> utf8, UiPayloadShape shape)
	{
		var sizeLimit = shape == UiPayloadShape.Tree
			? ProtocolLimits.MaxUiTreeBytes
			: ProtocolLimits.MaxUiPatchBytes;

		// Checked before a single token is read: parsing an oversized body is the risk being bounded.
		if (utf8.Length > sizeLimit)
		{
			return UiPayloadScan.Failed(UiSessionErrorCodes.PayloadTooLarge);
		}

		try
		{
			return ScanTokens(utf8, shape);
		}
		catch (JsonException)
		{
			return UiPayloadScan.Failed(UiSessionErrorCodes.InvalidPayload);
		}
	}

	private static UiPayloadScan ScanTokens(ReadOnlySpan<byte> utf8, UiPayloadShape shape)
	{
		var reader = new Utf8JsonReader(utf8,
			new JsonReaderOptions
			{
				MaxDepth = ProtocolLimits.MaxJsonDepth,
				CommentHandling = JsonCommentHandling.Disallow,
				AllowTrailingCommas = false
			});

		Span<Role> roles = stackalloc Role[MaxTrackedDepth];
		Span<bool> hasResourceId = stackalloc bool[MaxTrackedDepth];
		Span<long> declaredBytes = stackalloc long[MaxTrackedDepth];

		var depth = 0;
		var pending = Role.Opaque;
		var scalar = Scalar.None;

		var carriedNodes = 0;
		var operations = 0;
		var removeOperations = 0;
		var fromRevision = 0;
		var toRevision = 0;
		var revision = 0;

		while (reader.Read())
		{
			switch (reader.TokenType)
			{
				case JsonTokenType.StartObject:
				case JsonTokenType.StartArray:
				{
					if (depth >= MaxTrackedDepth)
					{
						return UiPayloadScan.Failed(UiSessionErrorCodes.InvalidPayload);
					}

					var parent = depth == 0 ? Role.Opaque : roles[depth - 1];
					var role = depth == 0
						? shape == UiPayloadShape.Tree ? Role.TreeDocument : Role.PatchDocument
						: parent switch
						{
							Role.ChildrenArray => Role.Node,
							Role.OperationsArray => Role.Operation,
							// Everything nested inside a properties bag stays inside it: the bag is
							// arbitrary provider JSON, and its members carry no structural meaning
							// beyond being where a resource may appear.
							Role.NodeProperties => Role.NodeProperties,
							_ => pending
						};

					if (reader.TokenType == JsonTokenType.StartObject)
					{
						if (role == Role.Node)
						{
							carriedNodes++;

							if (carriedNodes > ProtocolLimits.MaxUiNodesPerTree)
							{
								return UiPayloadScan.Failed(UiSessionErrorCodes.PayloadTooLarge);
							}
						}
						else if (role == Role.Operation)
						{
							operations++;
						}
					}

					roles[depth] = role;
					hasResourceId[depth] = false;
					declaredBytes[depth] = -1;
					depth++;
					pending = Role.Opaque;
					scalar = Scalar.None;
					break;
				}

				case JsonTokenType.EndObject:
				{
					depth--;

					if (hasResourceId[depth] && declaredBytes[depth] > ProtocolLimits.MaxUiResourceBytes)
					{
						return UiPayloadScan.Failed(UiSessionErrorCodes.PayloadTooLarge);
					}

					pending = Role.Opaque;
					scalar = Scalar.None;
					break;
				}

				case JsonTokenType.EndArray:
					depth--;
					pending = Role.Opaque;
					scalar = Scalar.None;
					break;

				case JsonTokenType.PropertyName:
				{
					var container = depth == 0 ? Role.Opaque : roles[depth - 1];
					(pending, scalar) = Classify(container, ref reader);

					if (container == Role.NodeProperties && reader.ValueTextEquals("resourceId"u8) && depth > 0)
					{
						hasResourceId[depth - 1] = true;
					}

					break;
				}

				case JsonTokenType.Number:
				{
					switch (scalar)
					{
						case Scalar.Revision when reader.TryGetInt32(out var value):
							revision = value;
							break;
						case Scalar.FromRevision when reader.TryGetInt32(out var value):
							fromRevision = value;
							break;
						case Scalar.ToRevision when reader.TryGetInt32(out var value):
							toRevision = value;
							break;
						case Scalar.ByteLength when depth > 0 && reader.TryGetInt64(out var value):
							declaredBytes[depth - 1] = value;
							break;
					}

					scalar = Scalar.None;
					break;
				}

				case JsonTokenType.String:
				{
					if (scalar == Scalar.Op && reader.ValueTextEquals("remove-node"u8))
					{
						removeOperations++;
					}

					scalar = Scalar.None;
					break;
				}

				default:
					scalar = Scalar.None;
					break;
			}
		}

		if (depth != 0)
		{
			return UiPayloadScan.Failed(UiSessionErrorCodes.InvalidPayload);
		}

		return new UiPayloadScan
		{
			Ok = true,
			FromRevision = shape == UiPayloadShape.Tree ? revision : fromRevision,
			ToRevision = shape == UiPayloadShape.Tree ? revision : toRevision,
			OperationCount = operations,
			CarriedNodes = carriedNodes,
			RemoveOperations = removeOperations
		};
	}

	private static (Role Pending, Scalar Scalar) Classify(Role container, ref Utf8JsonReader reader)
		=> container switch
		{
			Role.TreeDocument when reader.ValueTextEquals("root"u8) => (Role.Node, Scalar.None),
			Role.TreeDocument when reader.ValueTextEquals("revision"u8) => (Role.Opaque, Scalar.Revision),
			Role.PatchDocument when reader.ValueTextEquals("operations"u8) => (Role.OperationsArray, Scalar.None),
			Role.PatchDocument when reader.ValueTextEquals("fromRevision"u8) => (Role.Opaque, Scalar.FromRevision),
			Role.PatchDocument when reader.ValueTextEquals("toRevision"u8) => (Role.Opaque, Scalar.ToRevision),
			Role.Node when reader.ValueTextEquals("properties"u8) => (Role.NodeProperties, Scalar.None),
			Role.Node when reader.ValueTextEquals("children"u8) => (Role.ChildrenArray, Scalar.None),
			Role.Node when reader.ValueTextEquals("fallback"u8) => (Role.Node, Scalar.None),
			Role.Operation when reader.ValueTextEquals("node"u8) => (Role.Node, Scalar.None),
			Role.Operation when reader.ValueTextEquals("properties"u8) => (Role.NodeProperties, Scalar.None),
			Role.Operation when reader.ValueTextEquals("op"u8) => (Role.Opaque, Scalar.Op),
			Role.NodeProperties when reader.ValueTextEquals("byteLength"u8) => (Role.NodeProperties, Scalar.ByteLength),
			_ => (Role.Opaque, Scalar.None)
		};
}
