using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using SectorRemovalUpdater.Models.ArchiveXL;
using YamlDotNet.Serialization.NamingConventions;

namespace SectorRemovalUpdater.YamlConverters;

public class NodeRemovalConverter : IYamlTypeConverter
{
    public bool Accepts(Type type)
    {
        return type == typeof(NodeRemoval) || type.IsSubclassOf(typeof(NodeRemoval));
    }

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer nestedObjectDeserializer)
    {
        // Create a temporary deserializer WITHOUT this converter to avoid recursion
        var tempDeserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        // Deserialize the current node as a dictionary
        var tempObj = tempDeserializer.Deserialize<Dictionary<string, object>>(parser);
        if (tempObj == null)
            return null;

        // Decide target type based on keys in the dictionary
        bool hasExpectedActors = tempObj.ContainsKey("expectedActors") && tempObj.ContainsKey("actorDeletions");
        bool hasExpectedInstances = tempObj.ContainsKey("expectedInstances") && tempObj.ContainsKey("instanceDeletions");

        Type targetType = (hasExpectedActors || hasExpectedInstances)
            ? typeof(InstancedNodeRemoval)
            : typeof(NodeRemoval);

        // Now convert the dictionary to the target type
        // Since you already have the dictionary, use another deserializer on serialized YAML string
        var serializer = new SerializerBuilder().Build();
        var yamlString = serializer.Serialize(tempObj);

        var finalDeserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        return finalDeserializer.Deserialize(new StringReader(yamlString), targetType);
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer nestedObjectSerializer)
    {
        var node = (NodeRemoval?)value ?? (InstancedNodeRemoval?)value ?? throw new ArgumentOutOfRangeException(nameof(value));
        
        emitter.Emit(new MappingStart());
        
        emitter.Emit(new Scalar("type"));
        emitter.Emit(new Scalar(node.Type ?? "null"));
        
        emitter.Emit(new Scalar("index"));
        emitter.Emit(new Scalar(node.Index.ToString()));
        
        if (node is InstancedNodeRemoval { ActorDeletions: not null, ExpectedActors: not null } inr)
        {
            emitter.Emit(new Scalar("actorDeletions"));
            emitter.Emit(new SequenceStart(null, null, false, SequenceStyle.Block));
            foreach (var actorIndex in inr.ActorDeletions)
            {
                emitter.Emit(new Scalar(actorIndex.ToString()));
            }
            emitter.Emit(new SequenceEnd());
            
            emitter.Emit(new Scalar("expectedActors"));
            emitter.Emit(new Scalar(inr.ExpectedActors.Value.ToString()));
        }
        
        emitter.Emit(new MappingEnd());
    }
}