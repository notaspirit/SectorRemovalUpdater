using System.CommandLine.Parsing;
using MessagePack;
using RemovalsUpdater.Models.RemovalsUpdater;
using RemovalsUpdater.Services;

namespace RemovalsUpdater
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
                /*
                 * Add new Commands here
                 * Make sure to check for argument length before using them
                 * Add a short description to the help command
                 * Here WolvenKitWrapper is loaded
                 * 
                 * It is best to have little logic here and just call a method
                 */
                case "hello":
                    if (commandArray.Length < 2)
                    {
                        Console.WriteLine("This command requires 1 parameter!");
                        return;
                    }
                    Console.WriteLine($"Hello, {commandArray[1]}!");
                    break;
                case "testSectorDuplication":
                    Console.WriteLine($"Starting TestSectorDuplication...");
                    var wkit = WolvenKitWrapper.Instance;
                    var duplicates = wkit?.ArchiveManager.GetGameArchives()
                        .SelectMany(x =>
                            x.Files.Values
                                .Where(y => y.Extension == ".streamingsector")
                                .Select(y => new
                                {
                                    FullPath = y.FileName,
                                    PartialKey = y.FileName.Split(@"\").Last().Split('.').First()
                                }))
                        .GroupBy(f => f.PartialKey)
                        .Where(g => g.Count() > 1)
                        .ToList();
                    if (duplicates?.Count > 0)
                    {
                        foreach (var duplicate in duplicates)
                        {
                            Console.WriteLine($"{duplicate.Key}: {duplicate.Count()}");
                            foreach (var item in duplicate)
                            {
                                Console.Write($"Full Path: {item.FullPath}");
                            }
                        }
                        Console.WriteLine($"Found {duplicates.Count} duplicates:");
                    }
                    else
                    {
                        Console.WriteLine("No duplicates found.");
                    }
                    break;
                case "HashNodes":
                    if (commandArray.Length < 2)
                    {
                        Console.WriteLine("This command requires 1 parameter!");
                        return;
                    }
                    
                    var dataBuilder = new DataBuilder();
                    dataBuilder.Initialize(commandArray[1]);
                    
                    await dataBuilder.BuildDataSet();
                    break;
                case "DataBaseStats":
                    if (commandArray.Length < 2)
                    {
                        Console.WriteLine("This command requires 1 parameter!");
                        return;
                    }
                    DatabaseService.Instance.Initialize(commandArray[1]);
                    var (size, entries) = DatabaseService.Instance.GetStats();
                    Console.WriteLine($"Database size: {size} bytes with {entries} entries.");
                    break;
                case "DumpDB":
                    if (commandArray.Length < 2)
                    {
                        Console.WriteLine("This command requires 1 parameter!");
                        return;
                    }
                    DatabaseService.Instance.Initialize(commandArray[1]);
                    DatabaseService.Instance.DumpDB();
                    break;
                case "CheckCollision":
                    if (commandArray.Length < 2)
                    {
                        Console.WriteLine("This command requires 1 parameter!");
                        return;
                    }
                    DatabaseService.Instance.Initialize(commandArray[1]);

                    var hashFrequency = new Dictionary<ulong, ushort>();
                    
                    var sectors = DatabaseService.Instance.DumpDB()?.Select(s => s.Value);

                    for(int i = 0; i < sectors?.Count(); i++)
                    {
                        var sector = sectors.ElementAt(i);
                        var deserialized = MessagePackSerializer.Deserialize<NodeDataEntry[]>(sector);
                        sector = null;
                        foreach (var nodeData in deserialized)
                        {
                            if (!hashFrequency.TryAdd(nodeData.Hash, 1))
                            {
                                hashFrequency[nodeData.Hash]++;
                            }
                        }
                        deserialized = null;
                    }

                    var dupeCount = 0;
                    foreach (var hash in hashFrequency)
                    {
                        if (hash.Value > 1)
                        {
                            dupeCount++;
                        }
                    }

                    Console.WriteLine(dupeCount > 0
                        ? $"Found {dupeCount} duplicate nodes or hash collisions."
                        : "No duplicate nodes or hash collisions found.");
                    break;
                case "help":
                    Console.WriteLine("Available commands:");
                    Console.WriteLine("  exit: exit - exits the program");
                    Console.WriteLine("  hello: hello - greets you");
                    Console.WriteLine("  help: help - shows this help message");
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
                Console.WriteLine("Usage: RemovalsUpdater.exe <command>");
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
                case "updateRemoval":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("update command requires 2 arguments: <path> <outPath>");
                        return;
                    }

                    var ps = new XlProcessingService();
                    ps.Process(args[1], args[2]);
                    break;
                case "help":
                    Console.WriteLine("Available commands:");
                    Console.WriteLine(" start - Starts the interactive mode");
                    Console.WriteLine(" config - Adjust config");
                    Console.WriteLine(" help - Displays this help message");
                    break;
                default:
                    Console.WriteLine($"Error: Unknown command '{command}'.");
                    Console.WriteLine("Type 'RemovalsUpdater.exe help' for a list of available commands.");
                    break;
            }
        }
    }
}
