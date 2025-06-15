using Newtonsoft.Json;

namespace RemovalsUpdater.Models.ArchiveXL;

public class BaseNode
{
    [JsonProperty("type")]
    public string Type { get; set; }
    
    [JsonProperty("index")]
    public int Index { get; set; }
}