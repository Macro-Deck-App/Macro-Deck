using SQLite;
using SuchByte.MacroDeck.Plugins;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.Variables
{
    public static class VariableManager
    {

        internal static event EventHandler OnVariableChanged;
        internal static event EventHandler OnVariableRemoved;

        public static List<Variable> Variables;


        
        private static bool _saving = false; // To prevent multiple save processes


        internal static void RefreshVariable(Variable variable)
        {
            if (OnVariableChanged != null)
            {
                OnVariableChanged(variable, EventArgs.Empty);
            }
            VariableManager.SaveAsync();
        }

        internal static Variable SetValue(string name, object value, VariableType type, string creator = "User", bool save = true)
        {
            if (Variables == null) return null;
            Variable variable = Variables.Find(v => v.Name.Equals(name));
            if (variable == null)
            {
                variable = new Variable
                {
                    Name = name,
                };
                Variables.Add(variable);
            }
            variable.Type = type.ToString();
            variable.Creator = creator;
            if (!variable.Value.Equals(value))
            {
                if ((type == VariableType.Integer || type == VariableType.Float) && !float.TryParse(value.ToString(), out float result))
                {
                    value = 0;
                }
                else if (type == VariableType.Bool && !bool.TryParse(value.ToString(), out bool resultBool))
                {
                    value = false;
                }
                variable.Value = value.ToString();
                if (OnVariableChanged != null)
                {
                    OnVariableChanged(variable, EventArgs.Empty);
                }
            }
            if (save)
            {
                SaveAsync();
            }
            return variable;
        }

        internal static Variable SetValue(string name, object value, VariableType type, string[] suggestions, string creator = "User", bool save = true)
        {
            if (Variables == null) return null;
            Variable variable = SetValue(name, value, type, creator, false);
            variable.Suggestions = suggestions;
            if (save)
            {
                SaveAsync();
            }

            return variable;
        }

        /// <summary>
        /// Set the value of an variable. If the variable does not exists, Macro Deck automatically creates it.
        /// </summary>
        /// <param name="name">Name of the variable</param>
        /// <param name="value">Value of the variable</param>
        /// <param name="type">Type of the variable</param>
        /// <param name="plugin">The instance of your plugin</param>
        /// <param name="save">Save the variable to the database? set to false if your plugin updates the variable often.</param>
        public static void SetValue(string name, object value, VariableType type, MacroDeckPlugin plugin, bool save = true)
        {
            SetValue(name, value, type, plugin.Name, save);
        }


        /// <summary>
        /// Set the value of an variable. If the variable does not exists, Macro Deck automatically creates it.
        /// </summary>
        /// <param name="name">Name of the variable</param>
        /// <param name="value">Value of the variable</param>
        /// <param name="type">Type of the variable</param>
        /// <param name="plugin">The instance of your plugin</param>
        /// <param name="suggestions">Suggestions for the value.</param>
        /// <param name="save">Save the variable to the database? set to false if your plugin updates the variable often.</param>
        public static void SetValue(string name, object value, VariableType type, MacroDeckPlugin plugin, string[] suggestions, bool save = true)
        {
            SetValue(name, value, type, suggestions, plugin.Name, save);
        }



        internal static void DeleteVariable(string name)
        {
            if (Variables == null) return;
            Variable variable = Variables.Find(v => v.Name.Equals(name));
            if (variable != null)
            {
                Variables.Remove(variable);
                if (OnVariableRemoved != null)
                {
                    OnVariableRemoved(name, EventArgs.Empty);
                }
            }
            Save();
        }

        

        internal static object ParseValue(string name)
        {
            if (Variables == null) return null;
            object value = null;
            Variable variable = Variables.Find(v => v.Name.Equals(name));
            if (variable.Value.Equals(VariableType.Bool.ToString()))
            {
                value = Boolean.Parse(variable.Value);
            } else if (variable.Value.Equals(VariableType.Float.ToString()))
            {
                value = float.Parse(variable.Value);
            } else if (variable.Value.Equals(VariableType.Integer.ToString()))
            {
                value = Int32.Parse(variable.Value);
            } else if (variable.Value.Equals(VariableType.String.ToString()))
            {
                value = variable.Value as string;
            }
            return value;
        }

        internal static void Load()
        {
            var db = new SQLiteConnection(MacroDeck.VariablesFilePath);
            db.CreateTable<Variable>();

            var query = db.Table<Variable>();

            Variables = new List<Variable>();
            Variables.AddRange(query);

            db.Close();
        }

        internal static void LoadAsync()
        {
            Task.Run(() =>
            {
                Load();
            });
        }

        internal static void Save()
        {
            if (MacroDeck.SafeMode || _saving || Variables == null)
            {
                return;
            }
            _saving = true;
            try
            {
                var databasePath = MacroDeck.VariablesFilePath;

                var db = new SQLiteConnection(databasePath);
                db.CreateTable<Variable>();
                db.DeleteAll<Variable>();

                foreach (Variable variable in Variables.ToArray())
                {

                    db.InsertOrReplace(variable);
                }

                db.Close();
            }
            catch { }
            _saving = false;
        }

        internal static void SaveAsync()
        {
            Task.Run(() =>
            {
                Save();
            });
        }


    }

    public enum VariableType
    {
        Integer,
        Float,
        String,
        Bool,
    }
}
