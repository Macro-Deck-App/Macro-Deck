using FastColoredTextBoxNS;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Variables;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    public partial class TemplateEditor : DialogForm
    {
        private AutocompleteMenu autocompleteMenu;

        private static readonly string[] operators = { "and", "cmp", "default", "defined", "eq", "ge", "gt", "has", "le", "lt", "ne", 
                                                       "not", "or", "xor", "when", "declare", "as", "dump", "echo", "empty", "set", "to", 
                                                       "return", "true", "false", "void"};
        private static readonly string[] functions = { "abs", "add", "call", "cast", "cat", "ceil", "char", "cmp", "cos", "cross", "default", "defined", 
                                                       "div", "eq", "except", "filter", "find", "flip", "floor", "format", "ge", "gt", "has", "join", "lcase", 
                                                        "le", "len", "lt", "map", "match", "max", "min", "mod", "mul", "ne", "ord", "pow", "rand", "range", "round", "sin", 
                                                        "slice", "sort", "split", "sub", "token", "type", "ucase", "union", "when", "xor", "zip"};
        private static readonly string[] commands = { "if", "elif", "else", "for", "while" };
        private static readonly string[] special = { "_trimblank_" };

        private readonly TextStyle functionStyle = new TextStyle(Brushes.DarkKhaki, null, FontStyle.Regular);
        private readonly TextStyle commentStyle = new TextStyle(Brushes.Green, null, FontStyle.Regular);
        private readonly TextStyle operatorStyle = new TextStyle(Brushes.SteelBlue, null, FontStyle.Regular);
        private readonly TextStyle commandStyle = new TextStyle(Brushes.Plum, null, FontStyle.Regular);
        private readonly TextStyle variableStyle = new TextStyle(Brushes.IndianRed, null, FontStyle.Regular);
        private readonly TextStyle specialStyle = new TextStyle(Brushes.Lime, null, FontStyle.Regular);

        private readonly Regex commentRegex = new Regex(@"{\s*_\}", RegexOptions.Multiline | RegexOptions.Compiled);
        private readonly Regex operatorRegex = new Regex(@$"\b(?x: {string.Join(" | ", operators)})\b", RegexOptions.Singleline | RegexOptions.Compiled);
        private readonly Regex functionRegex = new Regex(@$"\b(?x: {string.Join(" | ", functions)})\b", RegexOptions.Singleline | RegexOptions.Compiled);
        private readonly Regex commandRegex = new Regex(@$"\b(?x: {string.Join(" | ", commands)})\b", RegexOptions.Singleline | RegexOptions.Compiled);
        private readonly Regex specialRegex = new Regex(@$"\b(?x: {string.Join(" | ", special)})\b", RegexOptions.Singleline | RegexOptions.Compiled);

        private Regex variableRegex;

        private string[] variablesArray;

        public string Template 
        { 
            get
            {
                return this.template.Text;
            } 
            set
            {
                this.template.Text = value;
            }
        }

        public TemplateEditor(string template = "")
        {
            InitializeComponent();
            this.autocompleteMenu = new AutocompleteMenu(this.template);
            this.lblTemplateEngineInfo.Text = LanguageManager.Strings.MacroDeckUsesCottleTemplateEngine;
            this.lblResultLabel.Text = LanguageManager.Strings.Result;
            this.btnVariables.Text = LanguageManager.Strings.Variable;
            this.checkTrimBlankLines.Text = LanguageManager.Strings.TrimBlankLines;

            List<string> variablesList = new List<string>();
<<<<<<< HEAD
            foreach (var v in VariableManager.ListVariables)
=======
            foreach (var v in VariableManager.Variables)
>>>>>>> origin/main
            {
                variablesList.Add(v.Name);
            }
            variablesArray = variablesList.ToArray();

            variableRegex = new Regex(@$"\b(?x: {string.Join(" | ", variablesArray)})\b", RegexOptions.Singleline | RegexOptions.Compiled);

            this.autocompleteMenu.Items.SetAutocompleteItems(variablesArray.Concat(operators).Concat(functions).Concat(commands).Concat(special).ToArray());
            

            this.template.TextChanged += Template_TextChanged;
            this.Template = template;
        }

        private void Template_TextChanged(object sender, TextChangedEventArgs e)
        {
            this.lblResult.Text = VariableManager.RenderTemplate(this.template.Text);

            this.checkTrimBlankLines.Checked = this.template.Text.StartsWith("_trimblank_", StringComparison.OrdinalIgnoreCase);

            FastColoredTextBoxNS.Range range = (sender as FastColoredTextBox).Range;

            //clear style of changed range
            range.ClearStyle(functionStyle);
            range.ClearStyle(commentStyle);
            range.ClearStyle(operatorStyle);
            range.ClearStyle(commandStyle);
            range.ClearStyle(variableStyle);
            range.ClearStyle(specialStyle);

            range.SetStyle(commentStyle, commentRegex);
            range.SetStyle(operatorStyle, operatorRegex);
            range.SetStyle(functionStyle, functionRegex);
            range.SetStyle(commandStyle, commandRegex);
            range.SetStyle(variableStyle, variableRegex);
            range.SetStyle(specialStyle, specialRegex);
        }
        
        private void Insert(string str)
        {
            var selectionIndex = this.template.SelectionStart ;
            this.template.Text = this.template.Text.Insert(selectionIndex, str);
            this.template.SelectionStart = selectionIndex + str.Length;
        }

        private void BtnIf_Click(object sender, EventArgs e)
        {
<<<<<<< HEAD
            var dummyVariable = VariableManager.ListVariables.ToList().Find(x => x.Type == VariableType.Bool.ToString());
=======
            var dummyVariable = VariableManager.Variables.Find(x => x.Type == VariableType.Bool.ToString());
>>>>>>> origin/main
            string dummyVariableName = dummyVariable != null ? dummyVariable.Name : "VARIABLE";            
            this.Insert("{if VARIABLE: " + Environment.NewLine + "true" + Environment.NewLine + " |else: " + Environment.NewLine + "false" + Environment.NewLine + "}");
        }

       
        private void BtnAnd_Click(object sender, EventArgs e)
        {
            this.Insert("{and(2 < 3, 5 > 1)}");
        }

        private void BtnOr_Click(object sender, EventArgs e)
        {
            this.Insert("{or(2 = 3, 5 > 1)}");
        }

        private void BtnNot_Click(object sender, EventArgs e)
        {
            this.Insert("{not(1 = 2)}");
        }

        private void BtnVariables_Click(object sender, EventArgs e)
        {
            this.variablesContextMenu.Items.Clear();
            foreach (Variable variable in VariableManager.ListVariables)
            {
                ToolStripMenuItem item = new ToolStripMenuItem
                {
                    ForeColor = Color.White,
                    Text = variable.Name,
                };
                item.Click += Item_Click;
                this.variablesContextMenu.Items.Add(item);
            }
            this.variablesContextMenu.Show(PointToScreen(new Point(((ButtonPrimary)sender).Bounds.Left, ((ButtonPrimary)sender).Bounds.Bottom)));
        }

        private void Item_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem item = (ToolStripMenuItem)sender;
            this.Insert("{" + item.Text + "}");
        }

        private void LblTemplateEngineInfo_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://cottle.readthedocs.io/en/stable/page/03-builtin.html",
                UseShellExecute = true
            });
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void CheckTrimBlankLines_CheckedChanged(object sender, EventArgs e)
        {
            if (checkTrimBlankLines.Checked && !template.Text.StartsWith("_trimblank_", StringComparison.OrdinalIgnoreCase))
            {
                template.Text = template.Text.Insert(0, "_trimblank_\r\n");
            } else if (!checkTrimBlankLines.Checked && template.Text.StartsWith("_trimblank_", StringComparison.OrdinalIgnoreCase))
            {
                template.Text = template.Text.Replace("_trimblank_\r\n", string.Empty).Replace("_trimblank_", string.Empty);
            }
        }
    }
}
