using Newtonsoft.Json;

namespace RemovalsUpdater.Models.ArchiveXL;

public class InstancedNodeRemoval : NodeRemoval
{
    [JsonProperty("actorDeletions")]
    public List<int>? ActorDeletions;
    
    [JsonProperty("expectedActors")]
    public int? ExpectedActors { get; set; }
}