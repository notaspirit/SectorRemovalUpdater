using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SectorRemovalUpdater.Models.ArchiveXL;

namespace SectorRemovalUpdater.JsonConverters;

public class NodeRemovalConverter: JsonConverter<NodeRemoval>
{
    public override bool CanWrite => true;
    
    public override NodeRemoval? ReadJson(
        JsonReader reader,
        Type objectType,
        NodeRemoval? existingValue,
        bool hasExistingValue,
        JsonSerializer serializer)
    {
        var obj = JObject.Load(reader);
        
        var expectedActors = obj["expectedActors"]?.Value<int?>();
        var actorDeletionsToken = obj["actorDeletions"];
        var expectedInstances = obj["expectedInstances"]?.Value<int?>();
        var instanceDeletionsToken = obj["instanceDeletions"];
        
        List<int>? actorDeletions = actorDeletionsToken is JArray actorArray
            ? actorArray.ToObject<List<int>>()
            : null;

        List<int>? instanceDeletions = instanceDeletionsToken is JArray instanceArray
            ? instanceArray.ToObject<List<int>>()
            : null;

        NodeRemoval removal;
        
        if ((expectedActors != null && actorDeletions != null) ||
            (expectedInstances != null && instanceDeletions != null))
        {
            removal = new InstancedNodeRemoval
            {
                ExpectedActors = expectedActors ?? expectedInstances ?? -1,
                ActorDeletions = actorDeletions ?? instanceDeletions ?? new List<int>()
            };
        }
        else
        {
            removal = new NodeRemoval();
        }
        
        try
        {
            using var jsonReader = obj.CreateReader();
            serializer.Populate(jsonReader, removal);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to read node removal: {ex}");
        }
        return removal;
    }

    public override void WriteJson(JsonWriter writer, NodeRemoval? value, JsonSerializer serializer)
    {
        var jObject = JObject.FromObject(value, JsonSerializer.CreateDefault());
        jObject.WriteTo(writer);
    }
}