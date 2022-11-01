using SQLite;

namespace SuchByte.MacroDeck.JSON;

public class IconPackJson
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; }

    public string JsonString { get; set; }
}