using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RemovalsUpdater.Models.ArchiveXL;

namespace RemovalsUpdater.JsonConverters;

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
        
        NodeRemoval removal;
        if ((obj["expectedActors"]?.Value<int>() != null && (obj["actorDeletions"] is JArray actorArray ? actorArray.ToObject<List<int>>() : null) != null) ||
            (obj["expectedInstances"]?.Value<int>() != null && (obj["instanceDeletions"] is JArray instanceArray ? instanceArray.ToObject<List<int>>() : null) != null))
            removal = new InstancedNodeRemoval
            {
                ExpectedActors = obj["expectedActors"]?.Value<int>() ?? obj["expectedInstances"]?.Value<int>() ?? -1,
                ActorDeletions = obj["actorDeletions"]?.Value<List<int>>() ?? obj["instanceDeletions"]?.Value<List<int>>() ?? new List<int>(),
            };
        else
            removal = new NodeRemoval();
        
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