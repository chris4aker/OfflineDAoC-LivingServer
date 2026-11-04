using System;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace DOL.Config
{
	/// <summary>
	/// Base configuration for the server.
	/// </summary>
	public class BaseServerConfig
	{
		/// <summary>
		/// Whether or not to try and auto-detect the external IP of the server.
		/// </summary>
		private bool _detectRegionIP;

		/// <summary>
		/// Whether or not to enable UPnP mapping.
		/// </summary>
		private bool _enableUPnP;

		/// <summary>
		/// The listening address of the server.
		/// </summary>
		private IPAddress _ip;

		/// <summary>
		/// The listening port of the server.
		/// </summary>
		private ushort _port;

		/// <summary>
		/// The region (external) address of the server.
		/// </summary>
		private IPAddress _regionIP;

		/// <summary>
		/// The region (external) port of the server.
		/// </summary>
		private ushort _regionPort;

		/// <summary>
		/// The UDP listening address of the server.
		/// </summary>
		private IPAddress _udpIP;

		/// <summary>
		/// The UDP listening port of the server.
		/// </summary>
		private ushort _udpPort;

		/// <summary>
		/// Constructs a server configuration with default values.
		/// Living Server 1.1.2 (audit 2026-11-03): the defaults reach this PC only. The upstream defaults listened on every
		/// network (0.0.0.0) and asked the router to open the ports to the internet (UPnP), so a lost serverconfig.xml was
		/// rewritten with a server open to the internet. Hosting friends is a choice made in serverconfig.xml.
		/// </summary>
		protected BaseServerConfig()
		{
			_port = 10300;
			_ip = IPAddress.Loopback;
			_regionIP = IPAddress.Loopback;
			_regionPort = 10400;
			_udpIP = IPAddress.Loopback;
			_udpPort = 10400;
			_detectRegionIP = false;
			_enableUPnP = false;
		}

		/// <summary>
		/// Gets/sets the listening port for the server.
		/// </summary>
		public ushort Port
		{
			get { return _port; }
			set { _port = value; }
		}

		/// <summary>
		/// Gets/sets the listening address for the server.
		/// </summary>
		public IPAddress IP
		{
			get { return _ip; }
			set { _ip = value; }
		}

		/// <summary>p
		/// Gets/sets the region (external) address for the server.
		/// </summary>
		public IPAddress RegionIP
		{
			get { return _regionIP; }
			set { _regionIP = value; }
		}

		/// <summary>
		/// Gets/sets the region (external) port for the server.
		/// </summary>
		public ushort RegionPort
		{
			get { return _regionPort; }
			set { _regionPort = value; }
		}

		/// <summary>
		/// Gets/sets the UDP listening address for the server.
		/// </summary>
		public IPAddress UDPIP
		{
			get { return _udpIP; }
			set { _udpIP = value; }
		}

		/// <summary>
		/// Gets/sets the UDP listening port for the server.
		/// </summary>
		public ushort UDPPort
		{
			get { return _udpPort; }
			set { _udpPort = value; }
		}

		/// <summary>
		/// Whether or not to enable UPnP mapping.
		/// </summary>
		public bool EnableUPnP
		{
			get { return _enableUPnP; }
			set { _enableUPnP = value; }
		}

		/// <summary>
		/// Whether or not to try and auto-detect the external IP of the server.
		/// </summary>
		public bool DetectRegionIP
		{
			get { return _detectRegionIP; }
			set { _detectRegionIP = value; }
		}

		/// <summary>
		/// Loads the configuration values from the given configuration element.
		/// </summary>
		/// <param name="root">the root config element</param>
		protected virtual void LoadFromConfig(ConfigElement root)
		{
			// A setting the file leaves out takes the safe default above ("any" is still read as 0.0.0.0).
			string ip = root["Server"]["IP"].GetString(_ip.ToString());
			_ip = ip == "any" ? IPAddress.Any : IPAddress.Parse(ip);
			_port = (ushort) root["Server"]["Port"].GetInt(_port);

			ip = root["Server"]["RegionIP"].GetString(_regionIP.ToString());
			_regionIP = ip == "any" ? IPAddress.Any : IPAddress.Parse(ip);
			_regionPort = (ushort) root["Server"]["RegionPort"].GetInt(_regionPort);

			ip = root["Server"]["UdpIP"].GetString(_udpIP.ToString());

			_udpIP = ip == "any" ? IPAddress.Any : IPAddress.Parse(ip);
			_udpPort = (ushort) root["Server"]["UdpPort"].GetInt(_udpPort);

			_enableUPnP = root["Server"]["EnableUPnP"].GetBoolean(_enableUPnP);
			_detectRegionIP = root["Server"]["DetectRegionIP"].GetBoolean(_detectRegionIP);


		}

		/// <summary>
		/// Plain-English warnings for a configuration that can be reached from beyond this PC (logged at every start).
		/// Empty when the server listens on this PC only and UPnP is off.
		/// </summary>
		public IReadOnlyList<string> ExposureWarnings()
		{
			var warnings = new List<string>();
			var open = new List<string>();
			if (!IPAddress.IsLoopback(_ip))
				open.Add($"TCP {Describe(_ip)}:{_port}");
			if (!IPAddress.IsLoopback(_udpIP))
				open.Add($"UDP {Describe(_udpIP)}:{_udpPort}");
			if (open.Count > 0)
				warnings.Add("Your server can be reached from other computers: it listens on " + string.Join(" and ", open) +
					", not only on this PC (127.0.0.1). If you don't host friends, set <IP> and <UdpIP> to 127.0.0.1 in " +
					@"runtime\server\config\serverconfig.xml (server stopped).");
			if (_enableUPnP)
				warnings.Add($"UPnP is on: while it runs, the server asks your router to open TCP {_port} and UDP {_udpPort} to the internet " +
					@"(they are closed again when it stops). If you don't host friends over the internet, set <EnableUPnP>False</EnableUPnP> in runtime\server\config\serverconfig.xml.");
			return warnings;

			static string Describe(IPAddress address) => address.Equals(IPAddress.Any) ? "0.0.0.0 (every network this PC is on)" : address.ToString();
		}

		/// <summary>
		/// Load the configuration from an XML source file.
		/// </summary>
		/// <param name="configFile">the file to load from</param>
		public void LoadFromXMLFile(FileInfo configFile)
		{
			if (configFile == null)
				throw new ArgumentNullException("configFile");

			XmlConfigFile xmlConfig = XmlConfigFile.ParseXMLFile(configFile);
			LoadFromConfig(xmlConfig);
		}

		/// <summary>
		/// Saves the values to the given configuration element.
		/// </summary>
		/// <param name="root">the configuration element to save to</param>
		protected virtual void SaveToConfig(ConfigElement root)
		{
			root["Server"]["Port"].Set(_port);
			root["Server"]["IP"].Set(_ip);
			root["Server"]["RegionIP"].Set(_regionIP);
			root["Server"]["RegionPort"].Set(_regionPort);
			root["Server"]["UdpIP"].Set(_udpIP);
			root["Server"]["UdpPort"].Set(_udpPort);
			root["Server"]["EnableUPnP"].Set(_enableUPnP);
			root["Server"]["DetectRegionIP"].Set(_detectRegionIP);
		}

		/// <summary>
		/// Saves the values to the given XML configuration file.
		/// </summary>
		/// <param name="configFile">the file to save to</param>
		public void SaveToXMLFile(FileInfo configFile)
		{
			if (configFile == null)
				throw new ArgumentNullException("configFile");

			var config = new XmlConfigFile();
			SaveToConfig(config);

			config.Save(configFile);
		}
	}
}