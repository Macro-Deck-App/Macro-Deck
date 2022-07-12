using Cottle;
using SQLite;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Plugins;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
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


        /// <summary>
        /// Use GetVariables(MacroDeckPlugin macroDeckPlugin)
        /// </summary>
        public static TableQuery<Variable> ListVariables
        {
            get => _database.Table<Variable>();
        }

        /// <summary>
        /// Do not add/remove items to this list
        /// Use SetValue
        /// or
        /// DeleteVariable
        /// </summary>
        [Obsolete("Use GetVariables to list all variables")]
        public static List<Variable> Variables
        {
            get
            {
                var query = _database.Table<Variable>();
                List<Variable> variables = new List<Variable>();
                variables.AddRange(query);
                return variables;
            }
        }

        public static List<Variable> GetVariables(MacroDeckPlugin macroDeckPlugin)
        {
            var variables = new ConcurrentBag<Variable>();
            Parallel.ForEach(ListVariables.Where(x => x.Creator == macroDeckPlugin.Name), variable =>
            {
                variables.Add(variable);
            });
            return variables.ToList();
        }

        public static Variable GetVariable(MacroDeckPlugin macroDeckPlugin, string variableName)
        {
            return ListVariables.Where(x => x.Creator == macroDeckPlugin.Name && x.Name.ToLower() == variableName.ToLower()).FirstOrDefault();
        }

        private static SQLiteConnection _database;

        private static DocumentConfiguration templateConfiguration = new DocumentConfiguration
        {
            Trimmer = DocumentConfiguration.TrimFirstAndLastBlankLines,
        };

        internal static void InsertVariable(Variable variable)
        {
            if (ListVariables.Where(x => x.Name.ToLower() == variable.Name.ToLower()).Count() > 0) return;
            _database.Insert(variable);
        }

        internal static Variable SetValue(string name, object value, VariableType type, string creator = "User")
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            name = ConvertNameString(name);

            Variable variable = ListVariables.Where(v => v.Name.ToLower() == name.ToLower()).FirstOrDefault();
            if (variable == null)
            {
                variable = new Variable
                {
                    Name = name
                };
                _database.Insert(variable);
            }
            variable.Type = type.ToString();
            variable.Creator = creator;

            if (!variable.Value.Equals(value))
            {
                switch (type)
                {
                    case VariableType.Bool:
                        if (bool.TryParse(value.ToString(), out bool boolResult))
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

                if (OnVariableChanged != null)
                {
                    OnVariableChanged(variable, EventArgs.Empty);
                }
            }

            try
            {
                _database.Update(variable);
            } catch { }
            
            return variable;
        }

        internal static Variable SetValue(string name, object value, VariableType type, string[] suggestions, string creator = "User")
        {
            Variable variable = SetValue(name, value, type, creator);
            variable.Suggestions = suggestions;

            return variable;
        }

        [Obsolete("Remove save parameter")]
        public static void SetValue(string name, object value, VariableType type, MacroDeckPlugin plugin, bool save = true)
        {
            SetValue(name, value, type, plugin.Name);
        }


        [Obsolete("Remove save parameter")]
        public static void SetValue(string name, object value, VariableType type, MacroDeckPlugin plugin, string[] suggestions, bool save = true)
        {
            SetValue(name, value, type, suggestions, plugin.Name);
        }

        /// <summary>
        /// Set the value of an variable. If the variable does not exists, Macro Deck automatically creates it.
        /// </summary>
        /// <param name="name">Name of the variable</param>
        /// <param name="value">Value of the variable</param>
        /// <param name="type">Type of the variable</param>
        /// <param name="plugin">The instance of your plugin</param>
        /// <param name="suggestions">Suggestions for the value.</param>
        public static void SetValue(string name, object value, VariableType type, MacroDeckPlugin plugin, string[] suggestions = null)
        {
            SetValue(name, value, type, suggestions, plugin.Name);
        }


        /// <summary>
        /// Deletes a variable
        /// </summary>
        /// <param name="name">Name of the variable</param>
        public static void DeleteVariable(string name)
        {
            _database.Delete(new Variable() { Name = name });
            if (OnVariableRemoved != null)
            {
                OnVariableRemoved(name, EventArgs.Empty);
            }
            MacroDeckLogger.Info("Deleted variable " + name);
        }


        public static string RenderTemplate(string template)
        {
            string result;
            try 
            {
                templateConfiguration.Trimmer = template.StartsWith("_trimblank_", StringComparison.OrdinalIgnoreCase) ? DocumentConfiguration.TrimFirstAndLastBlankLines : DocumentConfiguration.TrimNothing;
                template = template.Replace("_trimblank_", "", StringComparison.OrdinalIgnoreCase);
                var document = Document.CreateDefault(template, templateConfiguration).DocumentOrThrow;

                Dictionary<Value, Value> vars = new Dictionary<Value, Value>();

                foreach (Variable v in ListVariables)
                {
                    if (vars.ContainsKey(v.Name)) continue;
                    Value value = "";

                    switch (v.Type)
                    {
                        case nameof(VariableType.Bool):
                            bool resultBool = false;
                            bool.TryParse(v.Value.Replace("On", "True", StringComparison.CurrentCultureIgnoreCase).Replace("Off", "False", StringComparison.OrdinalIgnoreCase), out resultBool);
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

        internal static void Initialize()
        {
            MacroDeckLogger.Info(typeof(VariableManager), "Initialize variables database...");
            _database = new SQLiteConnection(MacroDeck.VariablesFilePath);
            
            _database.CreateTable<Variable>();
            _database.Execute("delete from Variable where 'Name'='';");
            MacroDeckLogger.Info(typeof(VariableManager), ListVariables.Count() + " variables found");
        }

        internal static void Close()
        {
            try
            {
                _database.Close();
            } catch { }
        }

        public static string ConvertNameString(string str)
        {
            return str.ToString().ToLower()
                .Replace(" ", "_", StringComparison.OrdinalIgnoreCase)
                .Replace("|", "_", StringComparison.OrdinalIgnoreCase)
                .Replace(".", "_", StringComparison.OrdinalIgnoreCase)
                .Replace("-", "_", StringComparison.OrdinalIgnoreCase)
                .Replace("/", "_", StringComparison.OrdinalIgnoreCase)
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
