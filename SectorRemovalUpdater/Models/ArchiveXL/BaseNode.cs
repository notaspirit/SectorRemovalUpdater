using Newtonsoft.Json;

namespace SectorRemovalUpdater.Models.ArchiveXL;

public class BaseNode
{
    [JsonProperty("type")]
    public string Type { get; set; }
    
    [JsonProperty("index")]
    public int Index { get; set; }
}