using System.CommandLine.Parsing;
using System.Text;
using DiffPlex.DiffBuilder;
using DynamicData;
using MessagePack;
using Newtonsoft.Json;
using SectorRemovalUpdater.Models.ArchiveXL;
using SectorRemovalUpdater.Models.RemovalsUpdater;
using SectorRemovalUpdater.Services;
using WolvenKit.RED4.Archive.Buffer;
using WolvenKit.RED4.CR2W.JSON;
using WolvenKit.RED4.Types;
using ChangeType = DiffPlex.DiffBuilder.Model.ChangeType;

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
                case "DiffDuplicates":
                    var matchedNodesCSV = File.ReadAllLines("F:\\dev0\\prjk\\SRU Testing\\nodeHashesChecked.csv");

                    var hashesWithDiff = new List<HashDuplicate>();
                    
                    foreach (var line in matchedNodesCSV)
                    {
                        if (matchedNodesCSV.IndexOf(line) == 0)
                            continue;
                        
                        var columns = line.Split(',');

                        var hashWithDiff = new HashDuplicate()
                        {
                            Hash = ulong.Parse(columns[0])
                        };
                        var occuranceCount = columns.Length - 2 / 2;
                        
                        for (int i = 2; i < occuranceCount; i += 2)
                        {
                            hashWithDiff.HashOccurances.Add(new SourceAndDiff()
                            {
                                Index = int.Parse(columns[i+1]),
                                SectorPath = UtilService.GetSectorPath(columns[i])
                            });
                        }
                        hashesWithDiff.Add(hashWithDiff);
                    }
                    
                    var sectorToHashOccurrences = hashesWithDiff
                        .SelectMany(hd => hd.HashOccurances.Select(so => new KeyValuePair<ulong, SourceAndDiff>(hd.Hash, so)))
                        .GroupBy(kvp => kvp.Value.SectorPath)
                        .ToDictionary(
                            g => g.Key,
                            g => g.ToList()
                        );


                    var wkit = WolvenKitWrapper.Instance;
                    
                    var firstHashNode = new Dictionary<ulong, (string, string)>();
                    
                    foreach (var sector in sectorToHashOccurrences)
                    {
                        var sectorCR2W = wkit.ArchiveManager.GetCR2WFile(sector.Key);
                        if (sectorCR2W is not { RootChunk: worldStreamingSector wse })
                        {
                            Console.WriteLine($"Failed to get sector {sector.Key}");
                            continue;
                        }

                        var nodeData = wse.NodeData.Data as CArray<worldNodeData>;
                        
                        foreach (var hashOccurance in sector.Value)
                        {
                            var nodeDataEntry = nodeData[hashOccurance.Value.Index];
                            var nodeEntry = wse.Nodes[nodeDataEntry.NodeIndex].Chunk;

                            hashOccurance.Value.NodeType = nodeEntry.GetType().Name;

                            var hashFirstInstances = firstHashNode.TryGetValue(hashOccurance.Key, out var firstInstance) ? firstInstance : (null, null);
                            
                            if (string.IsNullOrWhiteSpace(hashFirstInstances.Item1) && string.IsNullOrWhiteSpace(hashFirstInstances.Item2))
                            {
                                var firstNodeInstanceSerialized = RedJsonSerializer.Serialize(nodeEntry);
                                var firstNodeDataInstanceSerialized = RedJsonSerializer.Serialize(nodeDataEntry);
                                hashOccurance.Value.JsonDiffNode = "";
                                hashOccurance.Value.JsonDiffNodeData = "";
                                
                                firstHashNode.Add(hashOccurance.Key, (firstNodeInstanceSerialized, firstNodeDataInstanceSerialized));
                                continue;
                            }
                            
                            var newNodeInstanceSerialized = RedJsonSerializer.Serialize(nodeEntry);
                            var newDataInstanceSerialized = RedJsonSerializer.Serialize(nodeDataEntry);

                            hashOccurance.Value.JsonDiffNode = "";
                            hashOccurance.Value.JsonDiffNodeData = "";
                            var diffBuilder = new InlineDiffBuilder(new DiffPlex.Differ());
                            if (hashFirstInstances.Item1 != newNodeInstanceSerialized)
                            {
                                var diffNode = diffBuilder.BuildDiffModel(hashFirstInstances.Item1, newNodeInstanceSerialized);
                                foreach (var line in diffNode.Lines)
                                {
                                    if (line.Type != ChangeType.Unchanged)
                                    {
                                        hashOccurance.Value.JsonDiffNode += line.Text + Environment.NewLine;
                                    }
                                }  
                            }

                            if (hashFirstInstances.Item2 != newDataInstanceSerialized)
                            {
                                var diffNodeData = diffBuilder.BuildDiffModel(hashFirstInstances.Item2, newDataInstanceSerialized);
                                foreach (var line in diffNodeData.Lines)
                                {
                                    if (line.Type != ChangeType.Unchanged)
                                    {
                                        hashOccurance.Value.JsonDiffNodeData += line.Text + Environment.NewLine;
                                    }
                                }
                            }
                        }
                    }
                    
                    
                    File.WriteAllText("F:\\dev0\\prjk\\SRU Testing\\nodeHashesWithDiff.json", JsonConvert.SerializeObject(hashesWithDiff, Formatting.Indented));
                    Console.WriteLine("Finished Diffing Hashes.");
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
                case "CheckHashDuplicates":
                    if (args.Length < 1)
                    {
                        Console.WriteLine("CheckHashDuplicates usage is <Version>");
                        return;
                    }
                    var dbsCHD = DatabaseService.Instance;
                    dbsCHD.Initialize(_settingsService.DatabasePath);
                    Console.WriteLine($"Checking For Duplicates in Version {args[1]}...");
                    var nodeHashCount = new Dictionary<ulong, int>();
                    var actorHashCount = new Dictionary<ulong, int>();
                    var allEntries = dbsCHD.DumpDB(args[1]);
                    if (allEntries == null)
                    {
                        Console.WriteLine("Failed to load database!");
                        return;
                    }
                    foreach (var entry in allEntries)
                    {
                        var sector = MessagePackSerializer.Deserialize<NodeDataEntry[]>(entry.Value);
                        foreach (var hash in sector)
                        {
                            if (hash.ActorHashes == null || hash.ActorHashes?.Length == 0)
                            {
                                if (nodeHashCount.TryGetValue(hash.Hash, out var count))
                                {
                                    count += 1;
                                    nodeHashCount[hash.Hash] = count;
                                }
                                else
                                {
                                    nodeHashCount.Add(hash.Hash, 1);
                                }
                            }
                            else
                            {
                                foreach (var actorHash in hash?.ActorHashes)
                                {
                                    if (actorHashCount.TryGetValue(actorHash, out var count))
                                    {
                                        count += 1;
                                        actorHashCount[actorHash] = count;
                                    }
                                    else
                                    {
                                        actorHashCount.Add(actorHash, 1);
                                    }
                                }
                            }
                        }
                    }

                    var sbNode = new StringBuilder();
                    sbNode.AppendLine("Hash, Count");
                    foreach (var node in nodeHashCount.Where(node => node.Value > 1))
                    {
                        sbNode.AppendLine($"{node.Key},{node.Value}");
                    }
                    File.WriteAllText("F:\\dev0\\prjk\\SRU Testing\\nodeDuplicates.csv", sbNode.ToString());
                    
                    var sbActor = new StringBuilder();
                    sbActor.AppendLine("Hash, Count");
                    foreach (var node in actorHashCount.Where(node => node.Value > 1))
                    {
                        sbNode.AppendLine($"{node.Key},{node.Value}");
                    }
                    File.WriteAllText("F:\\dev0\\prjk\\SRU Testing\\actorDuplicates.csv", sbActor.ToString());
                    
                    Console.WriteLine("Finished Checking for Duplicates.");
                    break;
                case "MatchHashes":
                    if (args.Length < 1)
                    {
                        Console.WriteLine("MatchHashes usage is <Version>");
                        return;
                    }
                    Console.WriteLine($"Matching Hashes to Sector and Index for {args[1]}...");
                    var dbsMH = DatabaseService.Instance;
                    dbsMH.Initialize(_settingsService.DatabasePath);
                    var nodeHashes = File.ReadAllLines("F:\\dev0\\prjk\\SRU Testing\\nodeDuplicates.csv");
                    var allEntriesMH = dbsMH.DumpDB(args[1]);
                    if (allEntriesMH == null)
                    {
                        Console.WriteLine("Failed to load database!");
                        return;
                    }

                    nodeHashes[0] += ",Sector,Index";
                    
                    foreach (var entry in allEntriesMH)
                    {
                        Console.WriteLine($"Processing [{allEntriesMH.IndexOf(entry) + 1} / {allEntriesMH.Count}] {entry.Key}...");
                        var sector = MessagePackSerializer.Deserialize<NodeDataEntry[]>(entry.Value);
                        foreach (var node in sector)
                        {
                            var matchingHash = nodeHashes.FirstOrDefault(h => h.Split(',')[0] == node.Hash.ToString());
                            if (matchingHash == null)
                                continue;

                            var index = nodeHashes.IndexOf(matchingHash);
                            
                            Console.WriteLine($"Found node for {node.Hash}, {sector.IndexOf(node)} {entry.Key}");
                            
                            matchingHash += $", {entry.Key}, {sector.IndexOf(node)}";
                            nodeHashes[index] = matchingHash;
                        }
                    }
                    
                    File.WriteAllLines("F:\\dev0\\prjk\\SRU Testing\\nodeHashesChecked.csv", nodeHashes);
                    Console.WriteLine("Finished Matching Hashes.");
                    break;
                case "RemoveIdenticalNodes":
                    var allHashDupes = JsonConvert.DeserializeObject<List<HashDuplicate>>(File.ReadAllText("F:\\dev0\\prjk\\SRU Testing\\nodeHashesWithDiff.json"));
                    var nodesWithNonEmptyDiff = allHashDupes.Where(hd => hd.HashOccurances.Any(so => !string.IsNullOrWhiteSpace(so.JsonDiffNode) || !isOnlyIdOrNodeIndex(so.JsonDiffNodeData))).ToList();
                    File.WriteAllText("F:\\dev0\\prjk\\SRU Testing\\nodesWithNonEmptyDiff.json", JsonConvert.SerializeObject(nodesWithNonEmptyDiff, Formatting.Indented));
                    Console.WriteLine("Finished Removing Identical Nodes.");
                    break;

                    bool isOnlyIdOrNodeIndex(string diff)
                    {
                        var parts = diff.Split(@",");
                        if (string.IsNullOrWhiteSpace(diff))
                        {
                            return true;
                        }
                        Console.WriteLine(diff);
                        Console.WriteLine(parts.Length);
                        if (parts.Length == 1)
                        {
                            // Console.WriteLine("Only 1 line");
                            return true;
                        }
                        foreach (var part in parts)
                        {
                            if (!part.Contains("Id") && !part.Contains("NodeIndex") && !string.IsNullOrWhiteSpace(part.Trim()))
                            {
                                Console.WriteLine($"Part: {part} - Not Id or NodeIndex");
                                return false;   
                            }
                        }
                        Console.WriteLine("Only Id or NodeIndex");
                        return true;
                    }
                case "BreakIntoChunkBasedOnNodeType":
                    var allHashDupes2 = JsonConvert.DeserializeObject<List<HashDuplicate>>(File.ReadAllText("F:\\dev0\\prjk\\SRU Testing\\nodesWithNonEmptyDiff.json"));
                    var sortByNodeType = allHashDupes2.GroupBy(hd => hd.HashOccurances.First().NodeType).ToDictionary(g => g.Key, g => g.ToList());
                    foreach (var nodeType in sortByNodeType)
                    {
                        File.WriteAllText($"F:\\dev0\\prjk\\SRU Testing\\nodesWithNonEmptyDiff_{nodeType.Key}.json", JsonConvert.SerializeObject(nodeType.Value, Formatting.Indented));
                    }
                    Console.WriteLine("Finished Breaking Into Chunk Based On Node Type.");
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
