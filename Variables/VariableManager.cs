using Cottle;
using SQLite;
using SuchByte.MacroDeck.Plugins;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.Variables
{
    public static class VariableManager
    {

        internal static event EventHandler OnVariableChanged;
        internal static event EventHandler OnVariableRemoved;

        public static List<Variable> Variables;

        private static DocumentConfiguration templateConfiguration = new DocumentConfiguration
        {
            Trimmer = DocumentConfiguration.TrimNothing,
        };


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
            name = ConvertNameString(name);

            Variable variable = Variables.Find(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (variable == null)
            {
                variable = new Variable();
                Variables.Add(variable);
            }
            variable.Name = name;
            variable.Type = type.ToString();
            variable.Creator = creator;

            if (!variable.Value.Equals(value))
            {
                switch (type)
                {
                    case VariableType.Bool:
                        if (Boolean.TryParse(value.ToString(), out bool boolResult))
                        {
                            value = boolResult;
                        }
                        else
                        {
                            value = false;
                        }
                        variable.Value = value.ToString();
                        break;
                    case VariableType.Float:
                        if (float.TryParse(value.ToString().Replace(".", Thread.CurrentThread.CurrentCulture.NumberFormat.NumberDecimalSeparator), out float floatResult))
                        {
                            value = floatResult;
                        }
                        else
                        {
                            value = 0.0f;
                        }
                        variable.Value = ((float)value).ToString();
                        break;
                    case VariableType.Integer:
                        if (Int32.TryParse(value.ToString(), out int intResult))
                        {
                            value = intResult;
                        }
                        else
                        {
                            value = 0;
                        }
                        variable.Value = value.ToString();
                        break;
                    case VariableType.String:
                    default:
                        variable.Value = value.ToString();
                        break;
                }



                /*if ((type == VariableType.Integer || type == VariableType.Float) && !float.TryParse(value.ToString(), out float result))
                {
                    value = 0;
                }
                else if (type == VariableType.Bool && !bool.TryParse(value.ToString(), out bool resultBool))
                {
                    value = false;
                }*/


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


        public static string RenderTemplate(string template)
        {
            string result = "";
            try 
            {
                var document = Document.CreateDefault(template, templateConfiguration).DocumentOrThrow;

                Dictionary<Value, Value> vars = new Dictionary<Value, Value>();

                foreach (Variable v in VariableManager.Variables)
                {
                    if (vars.ContainsKey(v.Name)) continue;
                    Value value = "";

                    switch (v.Type)
                    {
                        case nameof(VariableType.Bool):
                            bool resultBool = false;
                            Boolean.TryParse(v.Value.Replace("On", "True", StringComparison.CurrentCultureIgnoreCase).Replace("Off", "False", StringComparison.OrdinalIgnoreCase), out resultBool);
                            value = Value.FromBoolean(resultBool);
                            break;
                        case nameof(VariableType.Float):
                            float resultFloat = 0.0f;
                            float.TryParse(v.Value, out resultFloat);
                            value = Value.FromNumber(resultFloat);
                            break;
                        case nameof(VariableType.Integer):
                            int resultInteger = 0;
                            Int32.TryParse(v.Value, out resultInteger);
                            value = Value.FromNumber(resultInteger);
                            break;
                        case nameof(VariableType.String):
                            value = Value.FromString(v.Value);
                            break;
                    }

                    vars.Add(v.Name, value);
                }
                result = document.Render(Context.CreateBuiltin(vars));
            } catch (Exception e)
            {
                result = "Error: " + e.Message;
            }

            return result;
        }

        internal static void Load()
        {
            var db = new SQLiteConnection(MacroDeck.VariablesFilePath);
            db.CreateTable<Variable>();

            var query = db.Table<Variable>();

            List<Variable> variablesLoaded = new List<Variable>();
            variablesLoaded.AddRange(query);

            // Convert older variables to the new format
            foreach (Variable variable in variablesLoaded)
            {
                variable.Name = ConvertNameString(variable.Name);
            }

            Variables = variablesLoaded;

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
            finally
            {
                _saving = false;
            }
        }

        internal static void SaveAsync()
        {
            Task.Run(() =>
            {
                Save();
            });
        }


        private static string ConvertNameString(string str)
        {
            return str.ToString().ToLower()
                .Replace(" ", "_", StringComparison.OrdinalIgnoreCase)
                .Replace(".", "_", StringComparison.OrdinalIgnoreCase)
                .Replace("-", "_", StringComparison.OrdinalIgnoreCase)
                .Replace("ä", "ae", StringComparison.OrdinalIgnoreCase)
                .Replace("ö", "oe", StringComparison.OrdinalIgnoreCase)
                .Replace("ü", "ue", StringComparison.OrdinalIgnoreCase)
                .Replace("ß", "ss", StringComparison.OrdinalIgnoreCase);
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
