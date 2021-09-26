using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.InitialSetupPages
{
    public partial class SetupPage2 : UserControl
    {

        private InitialSetup initialSetup;
        public SetupPage2(InitialSetup initialSetup)
        {
            InitializeComponent();
            this.initialSetup = initialSetup;
            this.lblConfigureNetwork.Text = Language.LanguageManager.Strings.InitialSetupConfigureNetworkSettings;
            this.lblNetworkAdapter.Text = Language.LanguageManager.Strings.NetworkAdapter;
            this.lblIpAddress.Text = Language.LanguageManager.Strings.IPAddress;
            this.lblPort.Text = Language.LanguageManager.Strings.Port;
            this.groupInfo.Text = Language.LanguageManager.Strings.Info;
            this.lblInfo.Text = Language.LanguageManager.Strings.ConfigureNetworkInfo;
        }

        private void lblIpAddress_Click(object sender, EventArgs e)
        {

        }

        private void SetupPage2_Load(object sender, EventArgs e)
        {
            //this.adapter.Items.Add("All");
            try
            {
                NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
                foreach (NetworkInterface adapter in adapters)
                {
                    this.adapter.Items.Add(adapter.Name);
                }
                this.adapter.Text = this.GetAdapterFromIPAddress(this.GetDefaultIPAddress().ToString());
            } catch { }   
        }

        private void Adapter_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (adapter.SelectedItem.ToString().Equals("All"))
            {
                this.iPAddress.Text = "0.0.0.0";
            } else
            {
                this.iPAddress.Text = this.GetIPAddressFromAdapter(adapter.SelectedItem.ToString());
            }
            this.initialSetup.configuration.Host_Address = this.iPAddress.Text;
        }

        private void port_ValueChanged(object sender, EventArgs e)
        {
            this.initialSetup.configuration.Host_Port = (int)this.port.Value;
        }


        private String GetAdapterFromIPAddress(string address)
        {
            NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface adapter in adapters)
            {
                foreach (UnicastIPAddressInformation ip in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && ip.Address.ToString().Equals(GetDefaultIPAddress().ToString()))
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
            IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());

            return host
                .AddressList
                .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork);
        }

        public String GetIPAddressFromAdapter(string adapterName)
        {
            NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface adapter in adapters)
            {
                if (adapter.Name.Equals(adapterName))
                {
                    foreach (UnicastIPAddressInformation ip in adapter.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                            return ip.Address.ToString();
                    }
                }
            }
            return "0.0.0.0";
        }
    }
}
