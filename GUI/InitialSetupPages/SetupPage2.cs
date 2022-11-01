using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Forms;
using SuchByte.MacroDeck.Language;

namespace SuchByte.MacroDeck.GUI.InitialSetupPages;

public partial class SetupPage2 : UserControl
{

    private InitialSetup initialSetup;
    public SetupPage2(InitialSetup initialSetup)
    {
        InitializeComponent();
        this.initialSetup = initialSetup;
        lblConfigureNetwork.Text = LanguageManager.Strings.InitialSetupConfigureNetworkSettings;
        lblNetworkAdapter.Text = LanguageManager.Strings.NetworkAdapter;
        lblIpAddress.Text = LanguageManager.Strings.IPAddress;
        lblPort.Text = LanguageManager.Strings.Port;
        groupInfo.Text = LanguageManager.Strings.Info;
        lblInfo.Text = LanguageManager.Strings.ConfigureNetworkInfo;
    }

    private void lblIpAddress_Click(object sender, EventArgs e)
    {

    }

    private void SetupPage2_Load(object sender, EventArgs e)
    {
        //this.adapter.Items.Add("All");
        try
        {
            var adapters = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var adapter in adapters)
            {
                this.adapter.Items.Add(adapter.Name);
            }
            this.adapter.Text = GetAdapterFromIPAddress(GetDefaultIPAddress().ToString());
        } catch { }   
    }

    private void Adapter_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (adapter.SelectedItem.ToString().Equals("All"))
        {
            iPAddress.Text = "0.0.0.0";
        } else
        {
            iPAddress.Text = GetIPAddressFromAdapter(adapter.SelectedItem.ToString());
        }
        initialSetup.configuration.HostAddress = iPAddress.Text;
    }

    private void port_ValueChanged(object sender, EventArgs e)
    {
        initialSetup.configuration.HostPort = (int)port.Value;
    }


    private string GetAdapterFromIPAddress(string address)
    {
        var adapters = NetworkInterface.GetAllNetworkInterfaces();
        foreach (var adapter in adapters)
        {
            foreach (var ip in adapter.GetIPProperties().UnicastAddresses)
            {
                if (ip.Address.AddressFamily == AddressFamily.InterNetwork && ip.Address.ToString().Equals(GetDefaultIPAddress().ToString()))
                {
                    return adapter.Name;
                }
            }
        }

        return "";
    }

    public IPAddress GetDefaultIPAddress()
    {
        if (!NetworkInterface.GetIsNetworkAvailable())
        {
            return null;
        }
        var host = Dns.GetHostEntry(Dns.GetHostName());

        return host
            .AddressList
            .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork);
    }

    public string GetIPAddressFromAdapter(string adapterName)
    {
        var adapters = NetworkInterface.GetAllNetworkInterfaces();
        foreach (var adapter in adapters)
        {
            if (adapter.Name.Equals(adapterName))
            {
                foreach (var ip in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        return ip.Address.ToString();
                }
            }
        }
        return "0.0.0.0";
    }
}