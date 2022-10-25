using SQLite;

namespace SuchByte.MacroDeck.JSON
{
    public class FolderJson
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; }

        public string JsonString { get; set; }

    }
}
