using System;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.Variables;

namespace SuchByte.MacroDeck.GUI.CustomControls;

public partial class VariableItem : RoundedUserControl
{

    public Variable Variable;

    public VariableItem(Variable variable)
    {
        Variable = variable;
        InitializeComponent();
    }

    public new void Update()
    {
        Invoke(() =>
        {
            lblName.Text = Variable.Name;
            lblType.Text = Variable.Type;
            lblValue.Text = Variable.Value;
            lblCreator.Text = Variable.Creator;
        });
    }

    private void BtnEdit_Click(object sender, EventArgs e)
    {
        using var variableDialog = new VariableDialog(Variable);
        variableDialog.ShowDialog();
    }

    private void VariableItem_Load(object sender, EventArgs e)
    {
        Update();
    }
}