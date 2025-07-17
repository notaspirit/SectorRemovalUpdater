using MessagePack;

namespace SectorRemovalUpdater.Models.RemovalsUpdater;

[MessagePackObject]
public class DatabaseFile
{
    [Key(0)]
    public string GameVersion { get; set; }
    
    [Key(1)]
    public Dictionary<string, NodeDataEntry[]> NodeDataEntries { get; set; }
}