using Newtonsoft.Json;

namespace SectorRemovalUpdater.Models.ArchiveXL;

public class Streaming
{
    [JsonProperty("sectors")]
    public required List<Sector> Sectors { get; set; }
}