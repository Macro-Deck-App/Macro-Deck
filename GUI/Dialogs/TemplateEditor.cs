using FastColoredTextBoxNS;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Variables;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    public partial class TemplateEditor : DialogForm
    {

        TextStyle lightYellowStyle = new TextStyle(Brushes.LightYellow, null, FontStyle.Regular);
        TextStyle commentStyle = new TextStyle(Brushes.Green, null, FontStyle.Regular);
        TextStyle royaleBlueStyle = new TextStyle(Brushes.RoyalBlue, null, FontStyle.Regular);
        TextStyle blueVioletStyle = new TextStyle(Brushes.BlueViolet, null, FontStyle.Regular);

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
            this.lblTemplateEngineInfo.Text = LanguageManager.Strings.MacroDeckUsesCottleTemplateEngine;
            this.lblResultLabel.Text = LanguageManager.Strings.Result;
            this.btnVariables.Text = LanguageManager.Strings.Variable;


            this.template.TextChanged += Template_TextChanged;
            this.Template = template;
        }

        private void Template_TextChanged(object sender, TextChangedEventArgs e)
        {
            this.lblResult.Text = VariableManager.RenderTemplate(this.template.Text);
            FastColoredTextBoxNS.Range range = (sender as FastColoredTextBox).VisibleRange;

            //clear style of changed range
            range.ClearStyle(lightYellowStyle);
            range.ClearStyle(commentStyle);
            range.ClearStyle(royaleBlueStyle);
            //comment highlighting
            range.SetStyle(commentStyle, @"{\s*_\}", RegexOptions.Multiline);

            range.SetStyle(royaleBlueStyle, @"(?x: and | cmp | default | defined| eq | ge | gt | has | le | lt | ne 
                                    | not | or | xor | when | declare | as | dump | echo | empty | set | to | return 
                                    | true | false | void)", RegexOptions.Singleline);

            range.SetStyle(lightYellowStyle, @"(?x:
                                      abs | add | call | cast | cat | ceil | char | cmp | cos | cross | default
                                    | defined | div | eq | except | filter | find | flip | floor | format | ge
                                    | gt | has | join | lcase | le | len | lt | map | match | max | min | mod
                                    | mul | ne | ord | pow | rand | range | round | sin | slice | sort | split
                                    | sub | token | type | ucase | union | when | xor | zip
                                    )", RegexOptions.Singleline);

            range.SetStyle(blueVioletStyle, @"(?x: if | elif | else | for | while)", RegexOptions.Singleline);

        }
        
        private void Insert(string str)
        {
            var selectionIndex = this.template.SelectionStart ;
            this.template.Text = this.template.Text.Insert(selectionIndex, str);
            this.template.SelectionStart = selectionIndex + str.Length;
        }

        private void BtnIf_Click(object sender, EventArgs e)
        {
            var dummyVariable = VariableManager.Variables.Find(x => x.Type == VariableType.Bool.ToString());
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
            foreach (Variable variable in VariableManager.Variables)
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

        private void template_Load(object sender, EventArgs e)
        {

        }
    }
}
