using System.Text;
using System.Text.Json;
using DynamicData;
using MessagePack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RemovalsUpdater.JsonConverters;
using RemovalsUpdater.Models.ArchiveXL;
using RemovalsUpdater.Models.RemovalsUpdater;

namespace RemovalsUpdater.Services;

public class XlProcessingService
{
    private DatabaseService _dbs;
    private SettingsService _settingsService;

    private static JsonSerializerSettings joptions = new JsonSerializerSettings()
    {
        Formatting = Formatting.Indented,
        Converters = { new NodeRemovalConverter() }
    };
    
    public XlProcessingService()
    {
        _dbs = DatabaseService.Instance;
        _settingsService = SettingsService.Instance;
        _dbs.Initialize(_settingsService.DatabasePath);
    }
    
    
    public void Process(string xlFilePath, string outputPath)
    {
        var (json, sectors) = GetJsonElements(xlFilePath);
        var pSectors = ProcessSectors(sectors);
        WriteJson(json, pSectors, outputPath);
    }

    private List<Sector> ProcessSectors(List<Sector> sectors)
    {
        List<Sector> outSectors = new();

        foreach (var sector in sectors)
        {
            // TODO: Add logic to DBs to have multiple dbs
            // For testing add a method to scramble newHashes around (switch indexes, add some remove some etc.)
            var sectorBytes = _dbs.GetEntry(Encoding.UTF8.GetBytes(sector.Path));
            if (sectorBytes == null)
            {
                Console.WriteLine($"Failed to get sector {sector.Path}");
                continue;   
            }
            
            var oldHashes = MessagePackSerializer.Deserialize<NodeDataEntry[]>(sectorBytes);
            var newHashes = MessagePackSerializer.Deserialize<NodeDataEntry[]>(sectorBytes);

            UtilService.Shuffle(newHashes);
            
            var newSector = new Sector
            {
                ExpectedNodes = newHashes.Length,
                Path = sector.Path,
                NodeDeletions = new List<NodeRemoval>(),
                NodeMutations = new List<NodeMutation>()
            };
            
            outSectors.Add(newSector);
            
            Dictionary<NodeRemoval, NodeDataEntry> unresolvedNodes = new();
            
            foreach (var node in sector.NodeDeletions)
            {
                var oldHash = oldHashes[node.Index];
                if (CompareHashes(oldHash, newHashes[node.Index]))
                {
                    // Console.WriteLine($"{oldHash.ActorHashes.Length} {newHashes[node.Index].ActorHashes.Length} {node.GetType()}");
                    if (oldHash.ActorHashes == null || newHashes[node.Index].ActorHashes == null || node is not InstancedNodeRemoval inr)
                    {
                        newSector.NodeDeletions.Add(node);
                        continue;
                    }
                    
                    inr.ExpectedActors = newHashes[node.Index].ActorHashes!.Length;
                    var relevantIndicies = oldHash.ActorHashes
                        .Select((h, i) => new { Index = i, Hash = h })
                        .Where(x => inr.ActorDeletions.Contains(x.Index))
                        .Select(x => new KeyValuePair<int, ulong>(x.Index, x.Hash))
                        .ToList();
                    inr.ActorDeletions = MatchActors(relevantIndicies, newHashes[node.Index].ActorHashes!.ToList(), sector.Path, node.Index);
                    
                    newSector.NodeDeletions.Add(inr);
                    continue;
                }
                
                foreach (var newHash in newHashes)
                {
                    if (!CompareHashes(oldHash, newHash))
                        continue;
                    
                    node.Index = newHashes.IndexOf(newHash);
                    
                    if (oldHash.ActorHashes == null || newHash.ActorHashes == null || node is not InstancedNodeRemoval inr)
                    {
                        newSector.NodeDeletions.Add(node);
                        goto continueOuter;
                    }

                    inr.ExpectedActors = newHash.ActorHashes!.Length;
                    var relevantIndicies = oldHash.ActorHashes
                        .Select((h, i) => new { Index = i, Hash = h })
                        .Where(x => inr.ActorDeletions.Contains(x.Index))
                        .Select(x => new KeyValuePair<int, ulong>(x.Index, x.Hash))
                        .ToList();
                    inr.ActorDeletions = MatchActors(relevantIndicies, newHash.ActorHashes!.ToList(), sector.Path, node.Index);
                    
                    newSector.NodeDeletions.Add(inr);
                    goto continueOuter;
                }
                
                unresolvedNodes.Add(node, oldHash);
                
                continueOuter:
                continue;
            }
            
            ResolveUnResolvedNodes(ref outSectors, unresolvedNodes, sector.Path);
        }
        
        return outSectors;
    }

    private void ResolveUnResolvedNodes(ref List<Sector> sectors,
        Dictionary<NodeRemoval, NodeDataEntry> unresolvedNodes, string sectorPath)
    {
        if (unresolvedNodes.Count == 0)
            return;
        
        var sectorInfo = GetSectorInfo(sectorPath);
        foreach (var sectorX in UtilService.ClosestSteps(sectorInfo.X, _settingsService.MaxSectorDepth))
            foreach (var sectorY in UtilService.ClosestSteps(sectorInfo.Y, _settingsService.MaxSectorDepth))
                foreach (var sectorZ in UtilService.ClosestSteps(sectorInfo.Z, _settingsService.MaxSectorDepth))
                {
                    var sector = _dbs.GetEntry(Encoding.UTF8.GetBytes(sectorPath));
                    if (sector == null)
                    {
                        Console.WriteLine($"Failed to get sector {sectorPath}");
                        continue;   
                    }
                    var newHashes = MessagePackSerializer.Deserialize<NodeDataEntry[]>(sector);
                    foreach (var newHash in newHashes)
                    {
                        var match = unresolvedNodes.Values.FirstOrDefault(h => CompareHashes(h, newHash));
                        if (match == null)
                            continue;
                        
                        var interatedSectorInfo = new SectorInfo()
                        {
                            X = sectorX,
                            Y = sectorY,
                            Z = sectorZ,
                            LOD = sectorInfo.LOD
                        };
                        
                        var node = unresolvedNodes.First(n => n.Value == match).Key;

                        unresolvedNodes.Remove(node);
                        
                        var originalIndex = node.Index;
                        node.Index = newHashes.IndexOf(newHash);

                        var newSector = sectors.FirstOrDefault(s => s.Path == GetSectorPath(sectorPath, interatedSectorInfo));

                        if (newSector == null)
                        {
                            newSector = new Sector()
                            {
                                Path = GetSectorPath(sectorPath, sectorInfo),
                                ExpectedNodes = newHashes.Length,
                                NodeDeletions = new List<NodeRemoval>(),
                                NodeMutations = new List<NodeMutation>()
                            };
                            sectors.Add(newSector);
                        }
                        
                        if (match.ActorHashes == null || newHash.ActorHashes == null || node is not InstancedNodeRemoval inr)
                        {
                            newSector.NodeDeletions.Add(node);
                            if (unresolvedNodes.Count == 0)
                                return;
                            continue;
                        }

                        inr.ExpectedActors = newHash.ActorHashes!.Length;
                        var oldActors = match.ActorHashes
                            .Select((h, i) => new { Index = i, Hash = h })
                            .Where(x => inr.ActorDeletions.Contains(x.Index))
                            .Select(x => new KeyValuePair<int, ulong>(x.Index, x.Hash))
                            .ToList();

                        inr.ActorDeletions = MatchActors(oldActors, newHash.ActorHashes!.ToList(), sectorPath, originalIndex);
                    
                        newSector.NodeDeletions.Add(inr);
                        if (unresolvedNodes.Count == 0)
                            return;
                    }
                }
        Console.WriteLine($"Could not resolve {unresolvedNodes.Count} nodes in {sectorPath} and neighboring sectors.");
        Console.WriteLine($"Unresolved nodeData Indices are: { string.Join(", ", unresolvedNodes.Keys.Select(x => x.Index).ToList())}");
    }

    private static string GetSectorPath(string oldPath, SectorInfo sectorInfo)
    {
        var prefix = string.Join(@"\", oldPath.Split(@"\").Take(oldPath.Split(@"\").Length - 1));
        return $"{prefix}_{sectorInfo.X}_{sectorInfo.Y}_{sectorInfo.Z}_{sectorInfo.LOD}.streamingsector";
    }
    
    private static SectorInfo GetSectorInfo(string sectorPath)
    {
        var sectorSplit = sectorPath.Split(@"\")[^1].Split(".")[0].Split("_");
        return new()
        {
            X = int.Parse(sectorSplit[1]),
            Y = int.Parse(sectorSplit[2]),
            Z = int.Parse(sectorSplit[3]),
            LOD = int.Parse(sectorSplit[4])
        };
    }
    
    private static List<int> MatchActors(List<KeyValuePair<int,ulong>> oldHashes, List<ulong> newHash, string sectorPath, int nodeIndex)
    {
        List<int> matchedActors = new();
        foreach (var oldActor in oldHashes)
        {
            var index = newHash.IndexOf(oldActor.Value);
            if (index == -1)
            {
                Console.WriteLine($"No matching actor found for {oldActor.Key} in {sectorPath} at index {nodeIndex}!");
                continue;
            }
            matchedActors.Add(index);
        }
        return matchedActors;
    }
    
    private static bool CompareHashes(NodeDataEntry oldHash, NodeDataEntry newHash)
    {
        // Checking actor hashes as well since instanced nodes are often identical apart from their actors, however this will miss instanced nodes where the actors have changed
        var oldActorHashes = oldHash.ActorHashes ?? new ulong[0];
        var newActorHashes = newHash.ActorHashes ?? new ulong[0];
        if (oldActorHashes.Length != newActorHashes.Length)
            return false;
        return oldHash.NodeType == newHash.NodeType && oldHash.Hash == newHash.Hash && oldActorHashes.All(oldActor => newActorHashes.Contains(oldActor));
    }
    
    private static (JObject, List<Sector>) GetJsonElements(string xlFilePath)
    {
        if (!File.Exists(xlFilePath))
            throw new FileNotFoundException("File not found.");
        
        var fileContent = File.ReadAllText(xlFilePath);
        var json = JObject.Parse(fileContent);
        var xl = JsonConvert.DeserializeObject<ArchiveXLFile>(fileContent, joptions);
        
        if (xl == null)
            throw new Exception("Failed to deserialize ArchiveXLFile");
        
        return (json, xl.Streaming.Sectors);
    }

    private static void WriteJson(JObject json, List<Sector> sectors, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? throw new InvalidOperationException());
        json["streaming"]["sectors"] = JArray.FromObject(sectors);
        File.WriteAllText(outputPath, json.ToString());
    }
}