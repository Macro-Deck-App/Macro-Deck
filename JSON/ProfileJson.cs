using SQLite;

namespace SuchByte.MacroDeck.JSON
{
    public class ProfileJson
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; }

        public string JsonString { get; set; }

    }
}
