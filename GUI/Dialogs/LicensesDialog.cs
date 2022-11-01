using System;
using System.IO;
using System.Xml;
using SuchByte.MacroDeck.GUI.CustomControls;

namespace SuchByte.MacroDeck.GUI.Dialogs;

public partial class LicensesDialog : DialogForm
{
    public LicensesDialog()
    {
        InitializeComponent();
    }

    private void label3_Click(object sender, EventArgs e)
    {

    }

    private void LicensesDialog_Load(object sender, EventArgs e)
    {
        var result = "";
        var assembly = typeof(MacroDeck).Assembly;

        var licensesFileName = "SuchByte.MacroDeck.Resources.Licenses.xml";

        using var resourceStream = assembly.GetManifestResourceStream(licensesFileName);
        using var streamReader = new StreamReader(resourceStream);
        result= streamReader.ReadToEnd();

        var doc = new XmlDocument();
        doc.LoadXml(result);
        foreach (XmlNode node in doc.DocumentElement.ChildNodes)
        {
            var licenseItem = new LicenseItem(node.Name, node.SelectSingleNode("License").InnerText, node.SelectSingleNode("Author").InnerText, node.SelectSingleNode("Repository").InnerText);
            licensesPanel.Controls.Add(licenseItem);
        }
    }
}