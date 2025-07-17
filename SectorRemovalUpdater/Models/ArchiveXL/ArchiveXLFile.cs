using Newtonsoft.Json;

namespace SectorRemovalUpdater.Models.ArchiveXL;

public class ArchiveXLFile
{
    [JsonProperty("streaming")]
    public required Streaming Streaming { get; set; }
}