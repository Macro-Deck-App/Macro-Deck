using SQLite;
using System;
using System.Collections.Generic;
using System.Text;

namespace SuchByte.MacroDeck.JSON
{
    public class ProfileJson
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; }

        public string JsonString { get; set; }

    }
}
