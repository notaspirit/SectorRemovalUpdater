using System.Text;
using System.Text.Json;
using DynamicData;
using MessagePack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SectorRemovalUpdater.JsonConverters;
using SectorRemovalUpdater.YamlConverters;
using SectorRemovalUpdater.Models.ArchiveXL;
using SectorRemovalUpdater.Models.RemovalsUpdater;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using JsonSerializer = Newtonsoft.Json.JsonSerializer;

namespace SectorRemovalUpdater.Services;

public class XlProcessingService
{
    private DatabaseService _dbs;
    private SettingsService _settingsService;

    private static JsonSerializerSettings joptions = new JsonSerializerSettings()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        Converters = { new JsonConverters.NodeRemovalConverter() }
    };
    
    private static ISerializer yserializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new YamlConverters.NodeRemovalConverter())
        .Build();
    private static IDeserializer ydeserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new YamlConverters.NodeRemovalConverter())
        .Build();
    
    public XlProcessingService()
    {
        _dbs = DatabaseService.Instance;
        _settingsService = SettingsService.Instance;
        _dbs.Initialize(_settingsService.DatabasePath);
    }
    
    
    public void Process(string xlFilePath, string outputPath, string toVersion)
    {
        JObject? json = null;
        YamlNode? yaml = null;
        List<Sector>? sectors = null;
        try
        {
            (json, sectors) = GetJsonElements(xlFilePath);
        }
        catch (Exception) { }
        if (json == null || sectors == null)
            try
            {
                (yaml, sectors) = GetYamlElements(xlFilePath);
            }
            catch (Exception e) { }
        if (sectors == null)
            throw new Exception("Failed to parse XL file.");
        
        var fromVersion = UtilService.GetGameVersion(_settingsService.GamePath);
        if (fromVersion == null)
            throw new Exception("Game executable not found!");
        
        var pSectors = ProcessSectors(sectors, fromVersion, toVersion);
        if (json != null)
            WriteJson(json, pSectors, outputPath);
        if (yaml != null)
            WriteYaml(yaml, pSectors, outputPath);
    }

    private List<Sector> ProcessSectors(List<Sector> sectors, string fromVersion, string toVersion)
    {
        List<Sector> outSectors = new();

        foreach (var sector in sectors)
        {
            
            var oldSectorBytes = _dbs.GetEntry(Encoding.UTF8.GetBytes(UtilService.GetAbbreviatedSectorPath(sector.Path)), fromVersion);
            var newSectorBytes = _dbs.GetEntry(Encoding.UTF8.GetBytes(UtilService.GetAbbreviatedSectorPath(sector.Path)), toVersion);
            if (oldSectorBytes == null || newSectorBytes == null)
            {
                Console.WriteLine($"Failed to get sector {sector.Path}");
                continue;   
            }
            
            var oldHashes = MessagePackSerializer.Deserialize<NodeDataEntry[]>(oldSectorBytes);
            var newHashes = MessagePackSerializer.Deserialize<NodeDataEntry[]>(newSectorBytes);
            
            // TODO: Remove shuffling for production build
            UtilService.Shuffle(newHashes);
            
            var newSector = outSectors.FirstOrDefault(s => s.Path == sector.Path);

            if (newSector == null)
            {
                newSector = new Sector
                {
                    ExpectedNodes = newHashes.Length,
                    Path = sector.Path,
                    NodeDeletions = new List<NodeRemoval>(),
                    NodeMutations = new List<NodeMutation>()
                };

                outSectors.Add(newSector);
            }

            Dictionary<NodeRemoval, NodeDataEntry> unresolvedRemovals = new();
            Dictionary<NodeMutation, NodeDataEntry> unresolvedMutations = new();

            if (sector.NodeDeletions != null)
            {
                foreach (var node in sector.NodeDeletions)
                {
                    var oldHash = oldHashes[node.Index];
                    if (ProcessNode(node, oldHash, newHashes[node.Index], node.Index, ref newSector))
                        continue;

                    foreach (var newHash in newHashes)
                    {
                        if (ProcessNode(node, oldHash, newHash, newHashes.IndexOf(newHash), ref newSector))
                            goto continueOuter;
                    }

                    unresolvedRemovals.Add(node, oldHash);

                    continueOuter:
                    continue;
                }
            }

            if (sector.NodeMutations != null)
            {
                foreach (var node in sector.NodeMutations)
                {
                    var oldHash = oldHashes[node.Index];
                    if (ProcessNode(node, oldHash, newHashes[node.Index], node.Index, ref newSector))
                        continue;
                
                    foreach (var newHash in newHashes)
                    {
                        if (ProcessNode(node, oldHash, newHash, newHashes.IndexOf(newHash), ref newSector))
                            goto continueOuter; 
                    }
                
                    unresolvedMutations.Add(node, oldHash);
                
                    continueOuter:
                    continue;
                }

            }
            ResolveUnResolvedNodes(ref outSectors, unresolvedRemovals, unresolvedMutations, sector.Path, toVersion);
        }
        
        return outSectors;
    }

    private void ResolveUnResolvedNodes(ref List<Sector> sectors,
        Dictionary<NodeRemoval, NodeDataEntry> unresolvedNodes,
        Dictionary<NodeMutation, NodeDataEntry> unresolvedMutations, string sectorPath, string toVersion)
    {
        if (unresolvedNodes.Count + unresolvedMutations.Count == 0)
            return;
        
        var sectorInfo = GetSectorInfo(sectorPath);
        foreach (var sectorX in UtilService.ClosestSteps(sectorInfo.X, _settingsService.MaxSectorDepth))
            foreach (var sectorY in UtilService.ClosestSteps(sectorInfo.Y, _settingsService.MaxSectorDepth))
                foreach (var sectorZ in UtilService.ClosestSteps(sectorInfo.Z, _settingsService.MaxSectorDepth))
                {
                    var sector = _dbs.GetEntry(Encoding.UTF8.GetBytes(UtilService.GetAbbreviatedSectorPath(sectorPath)), toVersion);
                    if (sector == null)
                    {
                        Console.WriteLine($"Failed to get sector {sectorPath}");
                        continue;   
                    }
                    var newHashes = MessagePackSerializer.Deserialize<NodeDataEntry[]>(sector);
                    foreach (var newHash in newHashes)
                    {
                        var matchRemoval = unresolvedNodes.Values.FirstOrDefault(h => CompareHashes(h, newHash));
                        var matchMutation = unresolvedMutations.Values.FirstOrDefault(h => CompareHashes(h, newHash));
                        
                        if (matchRemoval == null && matchMutation == null)
                            continue;
                        
                        var interatedSectorInfo = new SectorInfo()
                        {
                            X = sectorX,
                            Y = sectorY,
                            Z = sectorZ,
                            LOD = sectorInfo.LOD
                        };
                        
                        var nodeRemoval = unresolvedNodes.FirstOrDefault(n => n.Value == matchRemoval).Key;
                        var nodeMutation = unresolvedMutations.FirstOrDefault(n => n.Value == matchMutation).Key;
                        
                        if (nodeRemoval != null)
                            unresolvedNodes.Remove(nodeRemoval);
                        if (nodeMutation != null)
                            unresolvedMutations.Remove(nodeMutation);

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

                        if (nodeRemoval != null && matchRemoval != null)
                            ProcessNode(nodeRemoval, matchRemoval, newHash, newHashes.IndexOf(newHash), ref newSector);
                        if (nodeMutation != null && matchMutation != null)
                            ProcessNode(nodeMutation, matchMutation, newHash, newHashes.IndexOf(newHash), ref newSector);    
                        
                        if (unresolvedNodes.Count + unresolvedMutations.Count == 0)
                            return;
                    }
                }
        Console.WriteLine($"Could not resolve {unresolvedNodes.Count} nodes in {sectorPath} and neighboring sectors.");
        Console.WriteLine($"Unresolved nodeData Indices are: { string.Join(", ", unresolvedNodes.Keys.Select(x => x.Index).ToList())}");
    }
    
    private static bool ProcessNode(BaseNode node, NodeDataEntry oldHash, NodeDataEntry newHash, int newHashIndex, ref Sector sector)
    {
        if (!CompareHashes(oldHash, newHash))
            return false;
        
        var oldNodeIndex = node.Index;
        node.Index = newHashIndex;
        
        if (oldHash.ActorHashes == null || newHash.ActorHashes == null || node is not InstancedNodeRemoval inr)
        {
            AddNode(node, ref sector);
            return true;
        }
        
        inr.ExpectedActors = newHash.ActorHashes!.Length;
        var relevantIndicies = oldHash.ActorHashes
            .Select((h, i) => new { Index = i, Hash = h })
            .Where(x => inr.ActorDeletions.Contains(x.Index))
            .Select(x => new KeyValuePair<int, ulong>(x.Index, x.Hash))
            .ToList();
        inr.ActorDeletions = MatchActors(relevantIndicies, newHash.ActorHashes!.ToList(), sector.Path, oldNodeIndex);
                    
        AddNode(node, ref sector);
        return true;
    }

    private static void AddNode(BaseNode node, ref Sector sector)
    {
        switch (node)
        {
            case InstancedNodeRemoval inr:
                sector.NodeDeletions.Add(inr);
                break;
            case NodeRemoval nr:
                sector.NodeDeletions.Add(nr);
                break;
            case NodeMutation nm:
                sector.NodeMutations.Add(nm);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(node), node, null);
        }
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
        return oldHash.Hash == newHash.Hash && oldActorHashes.All(oldActor => newActorHashes.Contains(oldActor));
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
        json["streaming"]["sectors"] = JArray.FromObject(sectors, JsonSerializer.Create(joptions));
        File.WriteAllText(outputPath, JsonConvert.SerializeObject(json, joptions));
    }
    
    private static (YamlNode, List<Sector>) GetYamlElements(string yamlFilePath)
    {
        if (!File.Exists(yamlFilePath))
            throw new FileNotFoundException("File not found.");
    
        var fileContent = File.ReadAllText(yamlFilePath);
        
        var yaml = new YamlStream();
        using (var reader = new StringReader(fileContent))
        {
            yaml.Load(reader);
        }
        var yamlDocument = yaml.Documents[0].RootNode;
        
        var xl = ydeserializer.Deserialize<ArchiveXLFile>(fileContent);
    
        if (xl == null)
            throw new Exception("Failed to deserialize ArchiveXLFile");
    
        return (yamlDocument, xl.Streaming.Sectors);
    }
    
    private static void WriteYaml(YamlNode yaml, List<Sector> sectors, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? throw new InvalidOperationException());
        
        var yamlMapping = (YamlMappingNode)yaml;
        var streamingNode = (YamlMappingNode)yamlMapping["streaming"];
        
        var sectorsYaml = yserializer.Serialize(sectors);
        var sectorsStream = new YamlStream();
        using (var reader = new StringReader(sectorsYaml))
        {
            sectorsStream.Load(reader);
        }
        var sectorsNode = sectorsStream.Documents[0].RootNode;
        
        streamingNode.Children[new YamlScalarNode("sectors")] = sectorsNode;
        
        using (var writer = new StreamWriter(outputPath))
        {
            var yamlStream = new YamlStream(new YamlDocument(yamlMapping));
            yamlStream.Save(writer, false);
        }
    }
}