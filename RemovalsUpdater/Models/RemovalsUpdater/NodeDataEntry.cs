

using MessagePack;

namespace RemovalsUpdater.Models.RemovalsUpdater;

[MessagePackObject]
public class NodeDataEntry
{
    [Key(0)]
    public required ulong Hash { get; set; }
    
    [Key(1)]
    public ulong[]? ActorHashes { get; set; }
}