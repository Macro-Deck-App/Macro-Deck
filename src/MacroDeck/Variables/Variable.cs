using SQLite;

namespace SuchByte.MacroDeck.Variables;

public class Variable
{

    [PrimaryKey]
    public string Name { get; set; } = "";
    public string Value { get; set; } = "false";
    public string Creator { get; set; } = "User";
    public string Type { get; set; } = VariableType.Bool.ToString();

    [Ignore]
    public VariableType VariableType => (VariableType)Enum.Parse(typeof(VariableType), Type);

    [Ignore]
    public string[] Suggestions { get; set; }
}