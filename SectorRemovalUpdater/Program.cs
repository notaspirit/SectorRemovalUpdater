using System.CommandLine.Parsing;
using MessagePack;
using SectorRemovalUpdater.Models.RemovalsUpdater;
using SectorRemovalUpdater.Services;

namespace SectorRemovalUpdater
{
    class Program
    {
        private static SettingsService _settingsService = SettingsService.Instance;
        
        /// <summary>
        /// Starts the interactive mode
        /// </summary>
        /// <param name="gameExePath">path to the game exe</param>
        /// <param name="enableMods">optional: enable mod support, by default false </param>
        /// <returns></returns>
        static async Task StartInteractiveMode(string gameExePath, bool enableMods = false)
        {
            Console.WriteLine("Initializing InteractiveMode...");
            
            if (!WolvenKitWrapper.Initialize(gameExePath, enableMods)) return;
            
            Console.WriteLine("App started. Type 'exit' to quit, 'help' for help.");
            
            while (true)
            {
                Console.Write("> ");
                string? command = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(command))
                    continue;

                if (command.ToLower() == "exit")
                    break;

                await HandleCommand(command);
            }
        
            Console.WriteLine("Exiting...");
        }
        /// <summary>
        /// Dispatches / Runs the command in interactive mode
        /// </summary>
        /// <param name="command">string of arguments</param>
        /// <returns></returns>
        static async Task HandleCommand(string command)
        {
            var commandArray = ParseArguments(command);
            switch (commandArray[0])
            {
                case "HashNodes":
                    var dataBuilder = new DataBuilder();
                    dataBuilder.Initialize();
                    
                    await dataBuilder.BuildDataSet();
                    break;
                case "DataBaseStats":
                    if (commandArray.Length < 2)
                    {
                        Console.WriteLine("DataBaseStats command requires 1 argument: <DatabaseName>");
                        return;
                    }
                    DatabaseService.Instance.Initialize(_settingsService.DatabasePath);
                    var (size, entries) = DatabaseService.Instance.GetStats(commandArray[1]);
                    Console.WriteLine($"Database size: {size} bytes with {entries} entries.");
                    break;
                case "help":
                    Console.WriteLine("Available commands:");
                    Console.WriteLine("  exit: exits the program");
                    Console.WriteLine("  HashNodes: generates hashes for the currently selected game version ");
                    Console.WriteLine("  DataBaseStats <DatabaseName>: prints the size and amount of entries in the database");
                    Console.WriteLine("  help: shows this help message");
                    break;
                default:
                    Console.WriteLine("Unknown command.");
                    break;
            }
        }
        /// <summary>
        /// Splits the string to an array of arguments
        /// </summary>
        /// <param name="input">string to split</param>
        /// <returns></returns>
        static string[] ParseArguments(string input)
        {
            return CommandLineStringSplitter.Instance.Split(input).ToArray();
        }
        
        /// <summary>
        /// Method called when app is run
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        static async Task Main(string[] args)
        { 
            if (args.Length == 0)
            {
                Console.WriteLine("Error: No command provided.");
                Console.WriteLine("Usage: SectorRemovalUpdater <command>");
                Console.WriteLine("Type 'SectorRemovalUpdater help' for a list of available commands.");
                return;
            }
            
            string command = args[0];
            
            switch (command)
            {
                /*
                 * This is only for commands outside the interactive mod
                 * => WolvenKitWrapper is *not* loaded
                 */
                case "start":
                    await StartInteractiveMode(Path.Join(_settingsService.GamePath, "bin", "x64", "Cyberpunk2077.exe"), _settingsService.EnableMods);
                    break;
                case "config":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("config command usage is set <key> <value> | get");
                        return;
                    }
                    
                    switch (args[1])
                    {
                        case "set":
                            if (args.Length < 4)
                            {
                                Console.WriteLine("config set command requires 2 arguments: <key> <value>");
                                return;
                            }
                            _settingsService.SetSetting(args[2], args[3]);
                            break;
                        case "get":
                            var settings = _settingsService.GetSettings();
                            foreach (var setting in settings)
                            {
                                Console.WriteLine($"{setting.Key}: {setting.Value}");
                            };
                            break;
                    }
                    break;
                case "update":
                    if (args.Length < 5)
                    {
                        Console.WriteLine("update command requires 3 arguments: <path> <outPath> <sourceVersion> <targetVersion>");
                        return;
                    }

                    var ps = new XlProcessingService();
                    ps.Process(args[1], args[2], args[3], args[4]);
                    break;
                case "SaveDatabaseToFile":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("SaveDatabaseToFile usage is <Version> <OutputPath>");
                        return;
                    }
                    var dbsW = DatabaseService.Instance;
                    dbsW.Initialize(_settingsService.DatabasePath);
                    dbsW.WriteDataBaseToFile(args[1], args[2]);
                    break;
                case "LoadDatabaseFromFile":
                    if (args.Length < 1)
                    {
                        Console.WriteLine("LoadDatabaseFromFile usage is <FilePath>");
                        return;
                    }
                    var dbsR = DatabaseService.Instance;
                    dbsR.Initialize(_settingsService.DatabasePath);
                    dbsR.LoadDatabaseFromFile(args[1]);
                    break;
                case "ListVersions":
                    var dbsLV = DatabaseService.Instance;
                    dbsLV.Initialize(_settingsService.DatabasePath);
                    Console.WriteLine($"Local Versions: {string.Join(", ", dbsLV.GetDatabaseNames())}");
                    break;
                case "help":
                    Console.WriteLine("Available commands:");
                    Console.WriteLine(" start: Starts the interactive mode");
                    Console.WriteLine(" config <set|get> <key?> <value?>: Adjust config");
                    Console.WriteLine(" update <path> <outPath> <sourceVersion> <targetVersion>:  Updates the removal file");
                    Console.WriteLine(" help:  Displays this help message");
                    break;
                default:
                    Console.WriteLine($"Error: Unknown command '{command}'.");
                    Console.WriteLine("Type 'SectorRemovalUpdater help' for a list of available commands.");
                    break;
            }
        }
    }
}
