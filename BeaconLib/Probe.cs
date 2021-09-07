using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace BeaconLib
{
	/// <summary>
	/// Counterpart of the beacon, searches for beacons
	/// </summary>
	/// <remarks>
	/// The beacon list event will not be raised on your main thread!
	/// </remarks>
	public class Probe : IDisposable
	{

		/// <summary>
		/// Raised when the beacon list is updated. Will be raised from a background thread.
		/// </summary>
		public event Action<IEnumerable<BeaconLocation>> BeaconsUpdated;

		/// <summary>
		/// Is true after the probe thread has been started, and false after it stops.
		/// </summary>
		public bool Running { get; private set; } = false;

		/// <summary>
		/// Remove beacons older than this
		/// </summary>
		private static readonly TimeSpan BeaconTimeout = new TimeSpan(0, 0, 0, 5); // seconds

		private readonly Thread thread;
		private readonly CancellationTokenSource threadStopSource;
		private readonly CancellationToken threadStop;
		private readonly EventWaitHandle waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
		private readonly UdpClient udp = new UdpClient();
		private IEnumerable<BeaconLocation> currentBeacons = Enumerable.Empty<BeaconLocation>();

		public Probe(string beaconType)
		{
			udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

			BeaconType = beaconType;
			thread = new Thread(BackgroundLoop) { IsBackground = true };
			threadStopSource = new CancellationTokenSource();
			threadStop = threadStopSource.Token;

			udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
			try
			{
				udp.AllowNatTraversal(true);
			}
			catch (Exception ex)
			{
				Debug.WriteLine("Error switching on NAT traversal: " + ex.Message);
			}

			udp.BeginReceive(ResponseReceived, null);
		}

		public void Start()
		{
			if (Running)
			{
				return;
			}

			Running = true;
			waitHandle.Set();

			if (thread.IsAlive == false)
			{
				thread.Start();
			}
		}

		private void ResponseReceived(IAsyncResult ar)
		{
			var remote = new IPEndPoint(IPAddress.Any, 0);
			var bytes = udp.EndReceive(ar, ref remote);

			var typeBytes = Beacon.Encode(BeaconType).ToList();
			Debug.WriteLine(string.Join(", ", typeBytes.Select(_ => (char)_)));
			if (Beacon.HasPrefix(bytes, typeBytes))
			{
				try
				{
					var portBytes = bytes.Skip(typeBytes.Count()).Take(2).ToArray();
					var port = (ushort)IPAddress.NetworkToHostOrder((short)BitConverter.ToUInt16(portBytes, 0));
					var payload = Beacon.Decode(bytes.Skip(typeBytes.Count() + 2));
					NewBeacon(new BeaconLocation(new IPEndPoint(remote.Address, port), payload, DateTime.Now));
				}
				catch (Exception ex)
				{
					Debug.WriteLine(ex);
				}
			}

			udp.BeginReceive(ResponseReceived, null);
		}

		public string BeaconType { get; private set; }

		private void BackgroundLoop()
		{
			while (true)
			{
				if (threadStop.IsCancellationRequested)
				{
					break;
				}

				if (Running == false)
				{
					waitHandle.WaitOne(2000);
					continue;
				}

				try
				{
					BroadcastProbe();
				}
				catch (Exception ex)
				{
					Debug.WriteLine(ex);
				}

				waitHandle.WaitOne(2000);
				PruneBeacons();
			}
		}

		private void BroadcastProbe()
		{
			var probe = Beacon.Encode(BeaconType).ToArray();
			udp.Send(probe, probe.Length, new IPEndPoint(IPAddress.Broadcast, Beacon.DiscoveryPort));
		}

		private void PruneBeacons()
		{
			var cutOff = DateTime.Now - BeaconTimeout;
			var oldBeacons = currentBeacons.ToList();
			var newBeacons = oldBeacons.Where(_ => _.LastAdvertised >= cutOff).ToList();
			if (EnumsEqual(oldBeacons, newBeacons))
				return;

			var u = BeaconsUpdated;
			if (u != null)
				u(newBeacons);
			currentBeacons = newBeacons;
		}

		private void NewBeacon(BeaconLocation newBeacon)
		{
			var newBeacons = currentBeacons
				.Where(_ => !_.Equals(newBeacon))
				.Concat(new[] { newBeacon })
				.OrderBy(_ => _.Data)
				.ThenBy(_ => _.Address, IPEndPointComparer.Instance)
				.ToList();
			var u = BeaconsUpdated;
			if (u != null)
				u(newBeacons);
			currentBeacons = newBeacons;
		}

		private static bool EnumsEqual<T>(IEnumerable<T> xs, IEnumerable<T> ys)
		{
			return xs.Zip(ys, (x, y) => x.Equals(y)).Count() == xs.Count();
		}

		public void Stop()
		{
			Running = false;
			waitHandle.Set();
			currentBeacons = new List<BeaconLocation>();
		}

		public void Dispose()
		{
			try
			{
				Stop();
				threadStopSource.Cancel();
				thread.Join();
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex);
			}
		}
	}
}
