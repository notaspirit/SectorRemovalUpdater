using Newtonsoft.Json;

namespace RemovalsUpdater.Models.ArchiveXL;

public class Streaming
{
    [JsonProperty("sectors")]
    public required List<Sector> Sectors { get; set; }
}