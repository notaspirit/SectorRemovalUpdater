using Newtonsoft.Json;

namespace SectorRemovalUpdater.Models.ArchiveXL;

public class InstancedNodeRemoval : NodeRemoval
{
    [JsonProperty("actorDeletions")]
    public List<int>? ActorDeletions;
    
    [JsonProperty("expectedActors")]
    public int? ExpectedActors { get; set; }
}