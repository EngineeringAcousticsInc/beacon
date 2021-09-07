using System;
using System.Collections.Generic;
using System.Net;
using System.Windows;

using BeaconLib;

using BeaconWpfDialog;

namespace BeaconDemo
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		private List<Beacon> beacons = new List<Beacon>();
		private Random r = new Random();
		private string _beaconKey = "com.eai.nvit";

		public MainWindow()
		{
			InitializeComponent();
		}

		private void serverButton_Click(object sender, RoutedEventArgs e)
		{
			// We need a random port number otherwise all beacons will be the same
			var b = new Beacon(_beaconKey, (ushort)r.Next(2048, 60000))
			{
				BeaconData = "Beacon at " + DateTime.Now + " on " + Dns.GetHostName()
			};
			b.Start();
			beacons.Add(b);
		}

		private void clientButton_Click(object sender, RoutedEventArgs e)
		{
			var w = new ConnectionWindow(_beaconKey) { ConnectMessage = "Pick a demo beacon" };
			if (w.ShowDialog() ?? false)
			{
				MessageBox.Show("You selected: " + w.Address);
			}
		}
	}
}
