using Newtonsoft.Json;

namespace RemovalsUpdater.Models.ArchiveXL;

public class ArchiveXLFile
{
    [JsonProperty("streaming")]
    public required Streaming Streaming { get; set; }
}