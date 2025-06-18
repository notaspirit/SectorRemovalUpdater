using Newtonsoft.Json;

namespace RemovalsUpdater.Models.ArchiveXL;

public class NodeMutation : BaseNode
{
    [JsonProperty("position")]
    public float[]? Position { get; set; }
    
    [JsonProperty("orientation")]
    public float[]? Orientation { get; set; }
    
    [JsonProperty("scale")]
    public float[]? Scale { get; set; }

    [JsonProperty("nbNodesUnderProxyDiff")]
    public int? NbNodesUnderProxyDiff { get; set; }
}