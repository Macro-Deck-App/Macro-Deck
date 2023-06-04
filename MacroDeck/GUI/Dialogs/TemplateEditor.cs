using System.Diagnostics;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using FastColoredTextBoxNS;
using FastColoredTextBoxNS.Types;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Variables;

namespace SuchByte.MacroDeck.GUI.Dialogs;

public partial class TemplateEditor : DialogForm
{
    private const string TrimBlankNewLine = VariableManager.TemplateTrimBlank + "\r\n";

    private static readonly string[] Operators = { "and", "cmp", "default", "defined", "eq", "ge", "gt", "has", "le", "lt", "ne", 
        "not", "or", "xor", "when", "declare", "as", "dump", "echo", "empty", "set", "to", 
        "return", "true", "false", "void"};
    private static readonly string[] Functions = { "abs", "add", "call", "cast", "cat", "ceil", "char", "cmp", "cos", "cross", "default", "defined", 
        "div", "eq", "except", "filter", "find", "flip", "floor", "format", "ge", "gt", "has", "join", "lcase", 
        "le", "len", "lt", "map", "match", "max", "min", "mod", "mul", "ne", "ord", "pow", "rand", "range", "round", "sin", 
        "slice", "sort", "split", "sub", "token", "type", "ucase", "union", "when", "xor", "zip"};
    private static readonly string[] Commands = { "if", "elif", "else", "for", "while" };
    private static readonly string[] Special = { VariableManager.TemplateTrimBlank };

    private readonly TextStyle functionStyle = new(Brushes.DarkKhaki, null, FontStyle.Regular);
    private readonly TextStyle commentStyle = new(Brushes.Green, null, FontStyle.Regular);
    private readonly TextStyle operatorStyle = new(Brushes.SteelBlue, null, FontStyle.Regular);
    private readonly TextStyle commandStyle = new(Brushes.Plum, null, FontStyle.Regular);
    private readonly TextStyle variableStyle = new(Brushes.IndianRed, null, FontStyle.Regular);
    private readonly TextStyle specialStyle = new(Brushes.Lime, null, FontStyle.Regular);

    private readonly Regex commentRegex = new(@"{\s*_\}", RegexOptions.Multiline | RegexOptions.Compiled);
    private readonly Regex operatorRegex = new(@$"\b(?x: {string.Join(" | ", Operators)})\b", RegexOptions.Singleline | RegexOptions.Compiled);
    private readonly Regex functionRegex = new(@$"\b(?x: {string.Join(" | ", Functions)})\b", RegexOptions.Singleline | RegexOptions.Compiled);
    private readonly Regex commandRegex = new(@$"\b(?x: {string.Join(" | ", Commands)})\b", RegexOptions.Singleline | RegexOptions.Compiled);
    private readonly Regex specialRegex = new(@$"\b(?x: {string.Join(" | ", Special)})\b", RegexOptions.Singleline | RegexOptions.Compiled);

    private readonly Regex variableRegex;
    private bool HasTrimBlank => Template.StartsWith(VariableManager.TemplateTrimBlank, StringComparison.OrdinalIgnoreCase);

    public string Template 
    { 
        get => template.Text;
        set => template.Text = value;
    }
    
    private List<Variable> Variables { get; }

    public TemplateEditor(string template = "")
    {
        InitializeComponent();
        var autocompleteMenu = new AutocompleteMenu(this.template);
        lblTemplateEngineInfo.Text = LanguageManager.Strings.MacroDeckUsesCottleTemplateEngine;
        lblResultLabel.Text = LanguageManager.Strings.Result;
        btnVariables.Text = LanguageManager.Strings.Variable;
        checkTrimBlankLines.Text = LanguageManager.Strings.TrimBlankLines;
        
        Variables = VariableManager.ListVariables.ToList();
        var variablesList = VariableManager.ListVariables.Select(v => v.Name).ToArray();
        
        variableRegex = new Regex(@$"\b(?x: {string.Join(" | ", variablesList)})\b", RegexOptions.Singleline | RegexOptions.Compiled);

        autocompleteMenu.Items.SetAutocompleteItems(variablesList.Concat(Operators).Concat(Functions).Concat(Commands).Concat(Special).ToArray());

        this.template.TextChanged += Template_TextChanged;
        Template = template;
    }

    private void Template_TextChanged(object? sender, TextChangedEventArgs e)
    {
        lblResult.Text = VariableManager.RenderTemplate(Template);

        checkTrimBlankLines.Checked = HasTrimBlank;

        var range = (sender as FastColoredTextBox)!.Range;

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
        var selectionIndex = template.SelectionStart ;
        Template = Template.Insert(selectionIndex, str);
        template.SelectionStart = selectionIndex;
        template.SelectionLength = str.Length;
    }

    private void BtnIf_Click(object sender, EventArgs e)
    {
        Insert(string.Format("{{if VARIABLE: {0}true{0} |else: {0}false{0}}}", Environment.NewLine));
    }

    private void BtnAnd_Click(object sender, EventArgs e)
    {
        Insert("{and(2 < 3, 5 > 1)}");
    }

    private void BtnOr_Click(object sender, EventArgs e)
    {
        Insert("{or(2 = 3, 5 > 1)}");
    }

    private void BtnNot_Click(object sender, EventArgs e)
    {
        Insert("{not(1 = 2)}");
    }
    
    private void BtnVariables_Click(object sender, EventArgs e)
    {
        variablesContextMenu.Items.Clear();
        foreach (var variable in Variables)
        {
            var item = new ToolStripMenuItem
            {
                ForeColor = Color.White,
                Text = variable.Name,
            };
            item.Click += Item_Click;
            variablesContextMenu.Items.Add(item);
        }
        variablesContextMenu.Show(PointToScreen(new Point(((ButtonPrimary)sender).Bounds.Left, ((ButtonPrimary)sender).Bounds.Bottom)));
    }

    private void Item_Click(object? sender, EventArgs e)
    {
        var item = (ToolStripMenuItem)sender!;
        Insert($"{{{item.Text}}}");
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
        DialogResult = DialogResult.OK;
        Close();
    }

    private void CheckTrimBlankLines_CheckedChanged(object sender, EventArgs e)
    {
        var hasTrimBlank = HasTrimBlank; //evaluate once
        if (checkTrimBlankLines.Checked && !hasTrimBlank)
        {
            Template = Template.Insert(0, TrimBlankNewLine);
        }
        else if (!checkTrimBlankLines.Checked && hasTrimBlank)
        {
            Template = Template.Replace(TrimBlankNewLine, string.Empty).Replace(VariableManager.TemplateTrimBlank, string.Empty);
        }
    }
}