using SQLite;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using static System.Windows.Forms.ComboBox;

namespace SuchByte.MacroDeck.Variables
{
    public class Variable
    {

        public string Name { get; set; } = "";
        public string Value { get; set; } = "false";
        public string Creator { get; set; } = "User";
        public string Type { get; set; } = VariableType.Bool.ToString();

        [Ignore]
        public string[] Suggestions { get; set; }
    }
}
