using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading;
using DOL.GameServerConsole;
using DOL.GS;

namespace DOL.DOLServer.Actions
{
    /// <summary>
    /// Handles console start requests of the gameserver
    /// </summary>
    public class ConsoleStart : IAction
    {
        /// <summary>
        /// returns the name of this action
        /// </summary>
        public string Name
        {
            get { return "--start"; }
        }

        /// <summary>
        /// returns the syntax of this action
        /// </summary>
        public string Syntax
        {
            get { return "--start [-config=./config/serverconfig.xml]"; }
        }

        /// <summary>
        /// returns the description of this action
        /// </summary>
        public string Description
        {
            get { return "Starts the DOL server in console mode"; }
        }

        private bool crashOnFail = false;


        private static bool StartServer()
        {
            Console.WriteLine("Starting GameServer");
            bool start = GameServer.Instance.Start();
            return start;
        }

        public void OnAction(Hashtable parameters)
        {
            Console.WriteLine("Starting...");
            FileInfo configFile;
            FileInfo currentAssembly = null;
            if (parameters["-config"] != null)
            {
                Console.WriteLine("Using config file: " + parameters["-config"]);
                configFile = new FileInfo((String) parameters["-config"]);
            }
            else
            {
                currentAssembly = new FileInfo(Assembly.GetEntryAssembly().Location);
                configFile = new FileInfo(currentAssembly.DirectoryName + Path.DirectorySeparatorChar + "config" + Path.DirectorySeparatorChar + "serverconfig.xml");
            }
            if (parameters.ContainsKey("-crashonfail"))
                crashOnFail = true;

            var config = new GameServerConfiguration();
            if (configFile.Exists)
            {
                config.LoadFromXMLFile(configFile);
            }
            else
            {
                if (!configFile.Directory.Exists)
                    configFile.Directory.Create();
                // Living Server 1.1.2: the code defaults are this PC only (127.0.0.1), UPnP off and the database in WAL,
                // so a lost serverconfig.xml no longer comes back open to the network.
                config.SaveToXMLFile(configFile);
                Console.WriteLine($"No config file found: wrote {configFile.FullName} with the safe defaults (this PC only, UPnP off).");
                if (currentAssembly != null && File.Exists(currentAssembly.DirectoryName + Path.DirectorySeparatorChar + "DOLConfig.exe"))
                {
                    Console.WriteLine("No config file found, launching with default config and embedded database... (SQLite)");
                }
            }

            GameServer.CreateInstance(config);
            StartServer();

            if (crashOnFail && GameServer.Instance.ServerStatus == EGameServerStatus.GSS_Closed)
            {
                throw new ApplicationException("Server did not start properly.");
            }

            bool run = true;

            while (run)
            {
                string line = Console.ReadLine();

                if (line == null)
                {
                    // Standard input has closed (the launcher exited, or the server runs as a service with
                    // stdin at end-of-file). Back off instead of spinning a CPU core; input can still resume.
                    Thread.Sleep(1000);
                    continue;
                }

                switch (line.ToLower())
                {
                    case "exit":
                        run = false;
                        break;
                    case "clear":
                        Console.Clear();
                        break;
                    default:
                    {
                        if (line.Length <= 0)
                            break;

                        if (line[0] != '/')
                            line = $"/{line}";

                        GameClient client = ConsoleCommandClient.Create();
                        client.Out = new ConsolePacketLib();

                        try
                        {
                            if (!ScriptMgr.HandleCommand(client, $"&{line[1..]}"))
                                Console.WriteLine($"Unknown command: {line}");
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.ToString());
                        }

                        break;
                    }
                }
            }

            GameServer.Instance?.Stop();
        }
    }
}
