using System.Text;
using System.Text.Json;
using DynamicData;
using MessagePack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RemovalsUpdater.Models.ArchiveXL;
using RemovalsUpdater.Models.RemovalsUpdater;

namespace RemovalsUpdater.Services;

public class XlProcessingService
{
    private DatabaseService _dbs;
    
    public XlProcessingService()
    {
        _dbs = DatabaseService.Instance;
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
            var oldHashes = MessagePackSerializer.Deserialize<List<NodeDataEntry>>(_dbs.GetEntry(Encoding.UTF8.GetBytes(sector.Path)));
            var newHashes = MessagePackSerializer.Deserialize<List<NodeDataEntry>>(_dbs.GetEntry(Encoding.UTF8.GetBytes(sector.Path)));

            var newSector = new Sector
            {
                ExpectedNodes = newHashes.Count,
                Path = sector.Path,
                NodeDeletions = new List<NodeRemoval>(),
                NodeMutations = new List<NodeMutation>()
            };
            
            Dictionary<NodeRemoval, NodeDataEntry> unresolvedNodes = new();
            
            foreach (var node in sector.NodeDeletions)
            {
                var oldHash = oldHashes[node.Index];
                if (CompareHashes(oldHash, newHashes[node.Index]))
                {
                    if (oldHash.ActorHashes == null || newHashes[node.Index].ActorHashes == null || node is not InstancedNodeRemoval inr)
                    {
                        newSector.NodeDeletions.Add(node);
                        continue;
                    }

                    inr.ExpectedActors = newHashes[node.Index].ActorHashes!.Length;
                    inr.ActorDeletions = MatchActors(oldHash.ActorHashes.Where(h => inr.ActorDeletions.Contains(oldHash.ActorHashes.IndexOf(h))).ToList(), newHashes[node.Index].ActorHashes!.ToList(), sector.Path, node.Index);
                    
                    newSector.NodeDeletions.Add(inr);
                    continue;
                }

                foreach (var newHash in newHashes)
                {
                    if (!CompareHashes(oldHash, newHash))
                        continue;
                    
                    if (oldHash.ActorHashes == null || newHashes[node.Index].ActorHashes == null || node is not InstancedNodeRemoval inr)
                    {
                        newSector.NodeDeletions.Add(node);
                        goto continueOuter;
                    }

                    inr.ExpectedActors = newHashes[node.Index].ActorHashes!.Length;
                    inr.ActorDeletions = MatchActors(oldHash.ActorHashes.Where(h => inr.ActorDeletions.Contains(oldHash.ActorHashes.IndexOf(h))).ToList(), newHashes[node.Index].ActorHashes!.ToList(), sector.Path, node.Index);
                    
                    newSector.NodeDeletions.Add(inr);
                    goto continueOuter;
                }
                
                unresolvedNodes.Add(node, oldHash);
                
                continueOuter:
                continue;
            }
            
            // TODO: Process Unresolved nodes by checking nearby sectors upto a given depth.
            
            outSectors.Add(newSector);
        }
        
        return outSectors;
    }

    private static List<int> MatchActors(List<ulong> oldHashes, List<ulong> newHash, string sectorPath, int nodeIndex)
    {
        List<int> matchedActors = new();
        foreach (var oldActor in oldHashes)
        {
            var index = newHash.IndexOf(oldActor);
            if (index == -1)
            {
                Console.WriteLine($"Actor {oldHashes.IndexOf(oldActor)} not found in {sectorPath} node {nodeIndex}!");
                continue;
            }
            matchedActors.Add(index);
        }
        return matchedActors;
    }
    
    private static bool CompareHashes(NodeDataEntry oldHash, NodeDataEntry newHash)
    {
        if (oldHash.NodeType != newHash.NodeType)
            return false;
        if (oldHash.Hash != newHash.Hash)
            return false;
        return true;
    }
    
    private static (JObject, List<Sector>) GetJsonElements(string xlFilePath)
    {
        if (!File.Exists(xlFilePath))
            throw new FileNotFoundException("File not found.");
        
        var fileContent = File.ReadAllText(xlFilePath);
        var json = JObject.Parse(fileContent);
        var xl = JsonConvert.DeserializeObject<ArchiveXLFile>(fileContent);
        
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