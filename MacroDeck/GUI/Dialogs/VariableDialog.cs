using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Variables;
using MessageBox = SuchByte.MacroDeck.GUI.CustomControls.MessageBox;

namespace SuchByte.MacroDeck.GUI.Dialogs;

public partial class VariableDialog : DialogForm
{
    public Variable Variable;

    private readonly bool _protected;
    private readonly bool _edit;

    public VariableDialog(Variable variable = null)
    {
        InitializeComponent();
        lblType.Text = LanguageManager.Strings.Type;
        lblName.Text = LanguageManager.Strings.Name;
        lblValue.Text = LanguageManager.Strings.Value;
        btnDelete.Text = LanguageManager.Strings.DeleteVariable;
        btnOk.Text = LanguageManager.Strings.Ok;
        if (variable == null)
        {
            Variable = new Variable();
            variableName.Enabled = true;
            _edit = false;
        } else
        {
            Variable = variable;
            variableName.Enabled = false;
            _edit = true;
        }
            
        _protected = (Variable.Creator != "User");
        variableType.Enabled = !_protected;
        variableValue.Enabled = !_protected;
    }


    private void VariableName_TextChanged(object sender, EventArgs e)
    {
            
    }

    private void BtnOk_Click(object sender, EventArgs e)
    {
        if (_protected)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        if (!_edit)
        {
            if (variableName.Text.Length == 0)
            {
                variableName.Text = "new_variable";
            }

            var variableCount = VariableManager.ListVariables.Count(v => v.Name == variableName.Text);
            if (variableCount > 0)
            {
                variableName.Text = string.Format(variableName.Text + " _{0}", variableCount);
            }
            Variable.Name = VariableManager.ConvertNameString(variableName.Text);
        }

        Variable.Type = variableType.Text;
        switch (Variable.Type)
        {
            case "Bool":
                Variable.Value = (variableValue.Text.ToLower().Equals("true") ? true : false).ToString();
                break;
            case "Integer":
                int.TryParse(variableValue.Text, out var intVal);
                Variable.Value = intVal.ToString();
                break;
            case "Float":
                float.TryParse(variableValue.Text, out var floatVal);
                Variable.Value = floatVal.ToString();
                break;
            case "String":
                Variable.Value = variableValue.Text;
                break;
        }

        VariableManager.SetValue(Variable.Name, Variable.Value, Variable.VariableType, Variable.Creator);

        DialogResult = DialogResult.OK;
        Close();
    }

    private void VariableDialog_Load(object sender, EventArgs e)
    {
        foreach (var name in Enum.GetNames(typeof(VariableType)))
        {
            variableType.Items.Add(name);
        }
        variableType.Text = Variable.Type;
        variableName.Text = Variable.Name;
        variableValue.Text = Variable.Value;
        CenterToScreen();
    }

    private void BtnDelete_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
        using var msgBox = new MessageBox();
        if(msgBox.ShowDialog(LanguageManager.Strings.AreYouSure, string.Format(LanguageManager.Strings.VariableXGetsDeleted, Variable.Name), MessageBoxButtons.YesNo) == DialogResult.Yes)
        {
            VariableManager.DeleteVariable(Variable.Name);
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }

    private void variableType_SelectedIndexChanged(object sender, EventArgs e)
    {
        switch (variableType.Text)
        {
            case "Bool":
                variableValue.Text = "false";
                break;
            case "Integer":
                variableValue.Text = "0";
                break;
            case "Float":
                variableValue.Text = "0.0";
                break;
            case "String":
                variableValue.Text = "";
                break;
        }
    }
}