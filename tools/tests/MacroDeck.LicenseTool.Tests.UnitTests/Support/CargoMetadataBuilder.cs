using System.Text.Json.Nodes;

namespace MacroDeck.LicenseTool.Tests.UnitTests.Support;

internal sealed class CargoMetadataBuilder(FixtureRepository repository)
{
	private const string RootId = "app 0.1.0";

	private readonly JsonArray _packages = [];
	private readonly Dictionary<string, JsonArray> _dependencies = new(StringComparer.Ordinal) { [RootId] = [] };

	public string Crate(string name, string version, string license, IReadOnlyDictionary<string, string> files, bool procMacro = false)
	{
		var id = $"{name} {version}";
		var directory = repository.CrateDirectory(name, version);
		Directory.CreateDirectory(directory);
		foreach (var (file, content) in files)
		{
			File.WriteAllText(Path.Combine(directory, file), content);
		}

		_packages.Add(new JsonObject
		{
			["id"] = id,
			["name"] = name,
			["version"] = version,
			["license"] = license,
			["repository"] = $"https://example.com/{name}",
			["authors"] = new JsonArray($"{name} developers"),
			["manifest_path"] = Path.Combine(directory, "Cargo.toml"),
			["targets"] = new JsonArray(new JsonObject { ["kind"] = new JsonArray(procMacro ? "proc-macro" : "lib") }),
		});
		_dependencies[id] = [];
		return id;
	}

	public void Depend(string from, string to, string? kind = null) =>
		_dependencies[from].Add(new JsonObject
		{
			["pkg"] = to,
			["dep_kinds"] = new JsonArray(new JsonObject { ["kind"] = kind }),
		});

	public static string Root => RootId;

	public string Build()
	{
		var packages = (JsonArray)_packages.DeepClone();
		packages.Add(new JsonObject
		{
			["id"] = RootId,
			["name"] = "app",
			["version"] = "0.1.0",
			["manifest_path"] = "/nonexistent/app/Cargo.toml",
			["targets"] = new JsonArray(new JsonObject { ["kind"] = new JsonArray("bin") }),
		});
		var nodes = new JsonArray();
		foreach (var (id, deps) in _dependencies)
		{
			nodes.Add(new JsonObject { ["id"] = id, ["deps"] = deps.DeepClone() });
		}

		return new JsonObject
		{
			["packages"] = packages,
			["workspace_members"] = new JsonArray(RootId),
			["resolve"] = new JsonObject { ["root"] = RootId, ["nodes"] = nodes },
		}.ToJsonString();
	}
}
