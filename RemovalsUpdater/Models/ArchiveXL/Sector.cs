using Newtonsoft.Json;

namespace RemovalsUpdater.Models.ArchiveXL;

public class Sector
{
    [JsonProperty("nodeDeletions")]
    public required List<NodeRemoval> NodeDeletions { get; set; }
    
    [JsonProperty("nodeMutations")]
    public required List<NodeMutation> NodeMutations { get; set; }
    
    [JsonProperty("expectedNodes")]
    public required int ExpectedNodes { get; set; }
    
    [JsonProperty("path")]
    public required string Path { get; set; }
}