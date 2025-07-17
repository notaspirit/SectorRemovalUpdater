using System.Diagnostics;
using System.Text;
using MessagePack;
using Newtonsoft.Json;
using SectorRemovalUpdater.Models.RemovalsUpdater;
using WolvenKit.RED4.Archive.Buffer;
using WolvenKit.RED4.Types;
using XXHash3NET;
using Enums = SectorRemovalUpdater.Models.RemovalsUpdater.Enums;
using RemovalsUpdater_Enums = SectorRemovalUpdater.Models.RemovalsUpdater.Enums;

namespace SectorRemovalUpdater.Services;

public class DataBuilder
{
    private WolvenKitWrapper? _wkit;
    private DatabaseService _dbs;
    private SettingsService _settings;
    
    public void Initialize()
    {
        _wkit = WolvenKitWrapper.Instance;
        _dbs = DatabaseService.Instance;
        _settings = SettingsService.Instance;
        Directory.CreateDirectory(_settings.DatabasePath);
        _dbs.Initialize(_settings.DatabasePath);
    }

    public async Task BuildDataSet(string dbns)
    {
        if (!Enum.TryParse(typeof(RemovalsUpdater_Enums.DatabaseNames), dbns, out var dbn) && dbn is not RemovalsUpdater_Enums.DatabaseNames)
        {
            throw new Exception($"Invalid database name: {dbns}");
        }
        
        Console.WriteLine("Starting build process...");
        
        if (_wkit == null)
            throw new Exception("WolvenkitWrapper instance is not initialized.");

        var vanillaSectors = _wkit.ArchiveManager.GetGameArchives()
            .SelectMany(x =>
            x.Files.Values.Where(y => y.Extension == ".streamingsector")
                .Select(y => y.FileName)).Distinct().ToList();

        Console.WriteLine($"Found {vanillaSectors.Count} vanilla sector files.");
        var tasks = vanillaSectors.Select(s => Task.Run(() => ProcessSector(s, (RemovalsUpdater_Enums.DatabaseNames)dbn)));
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
        Console.WriteLine("Finished build process.");
    }

    private void ProcessSector(string sectorPath, RemovalsUpdater_Enums.DatabaseNames dbn)
    {
        try
        {
            if (_dbs.GetEntry(Encoding.UTF8.GetBytes(UtilService.GetAbbreviatedSectorPath(sectorPath)), dbn) is { Length: > 0 })
                return;

            Console.WriteLine($"Getting {sectorPath}...");
            var gameFile = _wkit!.ArchiveManager.GetCR2WFile(sectorPath);

            if (gameFile is not { RootChunk: worldStreamingSector worldSector })
                return;
            var nodeData = worldSector.NodeData.Data as CArray<worldNodeData> ?? new CArray<worldNodeData>();

            var outNodeData = new NodeDataEntry[nodeData.Count];
            var nodeDataIndex = 0;
            // Console.WriteLine($"Processing {sectorPath}...");
            foreach (var nodeDataE in nodeData)
            {
                outNodeData[nodeDataIndex] = ProcessNodeData(nodeDataE, worldSector.Nodes[nodeDataE.NodeIndex]);
                nodeDataIndex++;
            }

            // Console.WriteLine($"Serializing {sectorPath} which contains {outNodeData.Length} nodes...");

            _dbs.WriteEntry(Encoding.UTF8.GetBytes(UtilService.GetAbbreviatedSectorPath(sectorPath)), MessagePackSerializer.Serialize(outNodeData), dbn);

            Console.WriteLine($"Finished {sectorPath}.");
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private static NodeDataEntry ProcessNodeData(worldNodeData nodeData, worldNode node)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        
        WriteVector4(bw, nodeData.Position);
        WriteQuaternion(bw, nodeData.Orientation);
        WriteVector3(bw, nodeData.Scale);
        WriteVector3(bw, nodeData.Pivot);
        WriteVector4(bw, nodeData.Bounds.Min);
        WriteVector4(bw, nodeData.Bounds.Max);
        
        bw.Write((ulong)nodeData.QuestPrefabRefHash);
        bw.Write((ulong)nodeData.UkHash1);
        bw.Write(nodeData.MaxStreamingDistance);
        bw.Write(nodeData.UkFloat1);
        bw.Write(nodeData.Uk10);
        bw.Write(nodeData.Uk11);
        bw.Write(nodeData.Uk12);
        bw.Write(nodeData.Uk13);
        bw.Write(nodeData.Uk14);
        
        ulong[] actorHashes = [];
        
        WriteNode(bw, node, ref actorHashes);
        
        var output = new NodeDataEntry
        {
            Hash = XXHash64.Compute(ms.GetBuffer()),
            ActorHashes = actorHashes
        };
        
        // Console.WriteLine($"NodeType: {output.NodeType} Hash: {output.Hash}");
        return output;
    }

    private static void WriteNode(BinaryWriter bw, worldNode node, ref ulong[] actorHashes)
    {
        WriteBaseNode(bw, node);

        switch (node)
        {
            case worldCollisionNode collisionNode:
                WriteCollisionNode(bw,  collisionNode, ref actorHashes);
                break;
            case worldInstancedDestructibleMeshNode instDestructibleMeshNode:
                WriteMeshNode(bw, instDestructibleMeshNode);
                WriteInstDestructibleMeshNode(bw, instDestructibleMeshNode, ref actorHashes);;
                break;
            case worldInstancedMeshNode instMeshNode:
                WriteInstMeshNode(bw, instMeshNode, ref actorHashes);;
                break;
            case worldMeshNode meshNode:
                WriteMeshNode(bw, meshNode);
                break;
            case worldReflectionProbeNode reflectionProbeNode:
                WriteReflectionProbeNode(bw, reflectionProbeNode);
                break;
            case worldEffectNode effectNode:
                WriteEffectNode(bw, effectNode);
                break;
            case worldEntityNode entityNode:
                WriteEntityNode(bw, entityNode);
                break;
            case worldPhysicalDestructionNode physicalDestructionNode:
                WritePhysicalDestructionNode(bw, physicalDestructionNode);
                break;
            case worldTerrainCollisionNode terrainCollisionNode:
                WriteTerrainCollisionNode(bw, terrainCollisionNode);
                break;
            case worldStaticDecalNode staticDecalNode:
                WriteStaticDecalNode(bw, staticDecalNode);
                break;
            case worldStaticLightNode staticLightNode:
                WriteStaticLightNode(bw, staticLightNode);
                break;
            case worldStaticOccluderMeshNode staticOccluderMeshNode:
                WriteStaticOccluderMeshNode(bw, staticOccluderMeshNode);
                break;
            case worldInstancedOccluderNode instOccluderNode:
                WriteInstancedOccluderNode(bw, instOccluderNode);
                break;
            case worldFoliageNode foliageNode:
                WriteFoliageNode(bw, foliageNode);
                break;
            case worldStaticFogVolumeNode staticFogVolumeNode:
                WriteStaticFogVolumeNode(bw, staticFogVolumeNode);
                break;
            case worldTrafficCompiledNode trafficCompiledNode:
                WriteTrafficCompiledNode(bw, trafficCompiledNode);
                break;
            case worldInteriorMapNode interiorMapNode:
                WriteInteriorMapNode(bw, interiorMapNode);
                break;
            case worldPhysicalFractureFieldNode physicalFractureFieldNode:
                WritePhysicalFractureFieldNode(bw, physicalFractureFieldNode);
                break;
            case worldStaticStickerNode staticStickerNode:
                WriteStaticStickerNode(bw, staticStickerNode);
                break;
            case worldStaticSoundEmitterNode staticSoundEmitterNode:
                WriteStaticSoundEmitterNode(bw, staticSoundEmitterNode);
                break;
            case worldCommunityRegistryNode communityRegistryNode:
                WriteCommunityRegistryNode(bw, communityRegistryNode);
                break;
            case worldAudioTagNode audioTagNode:
                WriteAudioTagNode(bw, audioTagNode);
                break;
            case worldAcousticSectorNode acousticSectorNode:
                WriteAcousticSectorNode(bw, acousticSectorNode);
                break;
            case worldBendedMeshNode bendedMeshNode:
                WriteBendedMeshNode(bw, bendedMeshNode);
                break;
            case worldGeometryShapeNode geometryShapeNode:
                WriteGeometryShapeNode(bw, geometryShapeNode);
                break;
            case worldCompiledCrowdParkingSpaceNode compiledCrowdParkingSpaceNode:
                WriteCompiledCrowdParkingSpaceNode(bw, compiledCrowdParkingSpaceNode);
                break;
            case worldAcousticPortalNode acousticPortalNode:
                WriteAcousticPortalNode(bw, acousticPortalNode);
                break;
            case worldAcousticZoneNode acousticZoneNode:
                WriteAcousticZoneNode(bw, acousticZoneNode);
                break;
            case worldCompiledSmartObjectsNode compiledSmartObjectsNode:
                WriteCompiledSmartObjectsNode(bw, compiledSmartObjectsNode);
                break;
            case worldTerrainMeshNode terrainMeshNode:
                WriteTerrainMeshNode(bw, terrainMeshNode);
                break;
            case worldAreaShapeNode areaShapeNode:
                WriteAreaShapeNode(bw, areaShapeNode);
                break;
            case worldDistantGINode distantGINode:
                WriteDistantGINode(bw, distantGINode);
                break;
            case worldDistantLightsNode distantLightsNode:
                WriteDistantLightsNode(bw, distantLightsNode);
                break;
            case worldTrafficPersistentNode trafficPersistentNode:
                WriteTrafficPersistentNode(bw, trafficPersistentNode);
                break;
            case worldTrafficCollisionGroupNode trafficCollisionGroupNode:
                WriteTrafficCollisionGroupNode(bw, trafficCollisionGroupNode);
                break;
            case worldCompiledCommunityAreaNode compiledCommunityAreaNode:
                WriteCompiledCommunityAreaNode(bw, compiledCommunityAreaNode);
                break;
            case worldStaticVectorFieldNode staticVectorFieldNode:
                WriteStaticVectorFieldNode(bw, staticVectorFieldNode);
                break;
            case worldStaticQuestMarkerNode staticQuestMarkerNode:
                WriteStaticQuestMarkerNode(bw, staticQuestMarkerNode);
                break;
            case worldPhysicalTriggerAreaNode physicalTriggerAreaNode:
                WritePhysicalTriggerAreaNode(bw, physicalTriggerAreaNode);
                break;
            case worldStaticParticleNode staticParticleNode:
                WriteStaticParticleNode(bw, staticParticleNode);
                break;
            case worldGINode gINode:
                WriteGINode(bw, gINode);
                break;
            case worldNavigationNode navigationNode:
                WriteNavigationNode(bw, navigationNode);
                break;
            case worldPopulationSpawnerNode populationSpawnerNode:
                WritePopulationSpawnerNode(bw, populationSpawnerNode);
                break;
            case worldPrefabNode prefabNode:
                WritePrefabNode(bw, prefabNode);
                break;
        }
    }

    private static void WritePrefabNode(BinaryWriter bw, worldPrefabNode prefabNode)
    {
        bw.Write((string?)prefabNode.Prefab.DepotPath ?? "");
        bw.Write((bool)prefabNode.CanBeToggledInGame);
        bw.Write((bool)prefabNode.NoCollisions);
        bw.Write((bool)prefabNode.EnableRenderSceneLayerOverride);
        bw.Write(prefabNode.RenderSceneLayerMask.ToBitFieldString());
        bw.Write((byte)prefabNode.StreamingImportance.GetEnumValue());
        bw.Write((byte)prefabNode.StreamingOcclusionOverride.GetEnumValue());
        bw.Write((byte)prefabNode.InteriorMapContribution.GetEnumValue());
        bw.Write((bool)prefabNode.IgnoreMeshEmbeddedOccluders);
        bw.Write((bool)prefabNode.IgnoreAllOccluders);
        bw.Write(prefabNode.OccluderAutoHideDistanceScale);
        bw.Write((byte)prefabNode.ProxyMeshOnly.GetEnumValue());
        bw.Write((bool)prefabNode.ProxyScaleOverride);
        WriteVector3(bw, prefabNode.ProxyScale);
        bw.Write((bool)prefabNode.ApplyMaxStreamingDistance);
    }
    
    private static void WritePopulationSpawnerNode(BinaryWriter bw, worldPopulationSpawnerNode populationSpawnerNode)
    {
        bw.Write((ulong)populationSpawnerNode.ObjectRecordId);
        bw.Write((string?)populationSpawnerNode.AppearanceName ?? "");
        bw.Write((bool)populationSpawnerNode.SpawnOnStart);
        bw.Write((byte)populationSpawnerNode.AlwaysSpawned.GetEnumValue());
        bw.Write((byte)populationSpawnerNode.SpawnInView.GetEnumValue());
        bw.Write((bool)populationSpawnerNode.PrefetchAppearance);
        bw.Write((bool)populationSpawnerNode.IsVehicle);
    }
    
    private static void WriteNavigationNode(BinaryWriter bw, worldNavigationNode navigationNode)
    {
        bw.Write((string?)navigationNode.NavigationTileResource.DepotPath ?? "");
    }

    private static void WriteGINode(BinaryWriter bw, worldGINode GINode)
    {
        bw.Write((string?)GINode.Data.DepotPath ?? "");
        foreach (var location in GINode.Location)
        {
            bw.Write(location);
        }
    }
    
    private static void WriteStaticParticleNode(BinaryWriter bw, worldStaticParticleNode staticParticleNode)
    {
        bw.Write(staticParticleNode.EmissionRate);
        bw.Write((string?)staticParticleNode.ParticleSystem.DepotPath ?? "");
        bw.Write(staticParticleNode.ForcedAutoHideDistance);
        bw.Write(staticParticleNode.ForcedAutoHideRange);
    }

    private static void WritePhysicalTriggerAreaNode(BinaryWriter bw,
        worldPhysicalTriggerAreaNode physicalTriggerAreaNode)
    {
        bw.Write((byte)physicalTriggerAreaNode.SimulationType.GetEnumValue());
        WritePhysicsTriggerShape(bw, physicalTriggerAreaNode.Shape);
    }
    
    private static void WriteStaticQuestMarkerNode(BinaryWriter bw, worldStaticQuestMarkerNode staticQuestMarkerNode)
    {
        bw.Write((byte)staticQuestMarkerNode.QuestType.GetEnumValue());
        bw.Write((string?)staticQuestMarkerNode.QuestLabel ?? "");
        bw.Write((string?)staticQuestMarkerNode.MapFilteringTag ?? "");
        bw.Write(staticQuestMarkerNode.QuestMarkerHeight);
    }
    
    private static void WriteStaticVectorFieldNode(BinaryWriter bw, worldStaticVectorFieldNode staticVectorFieldNode)
    {
        WriteVector3(bw, staticVectorFieldNode.Direction);
        bw.Write(staticVectorFieldNode.AutoHideDistance);
    }
    
    private static void WriteCompiledCommunityAreaNode(BinaryWriter bw,
        worldCompiledCommunityAreaNode compiledCommunityAreaNode)
    {
        bw.Write(compiledCommunityAreaNode.SourceObjectId.Hash);
    }
    
    private static void WriteTrafficCollisionGroupNode(BinaryWriter bw,
        worldTrafficCollisionGroupNode trafficCollisionGroupNode)
    {
        foreach (var entry in trafficCollisionGroupNode.CollisionEntries)
        {
            bw.Write(entry.NeRef.GetHashCode());
            bw.Write((bool)entry.Reversed);
        }
    }
    
    private static void WriteTrafficPersistentNode(BinaryWriter bw, worldTrafficPersistentNode trafficPersistentNode)
    {
        bw.Write((string?)trafficPersistentNode.Resource.DepotPath ?? "");
    }
    
    private static void WriteDistantLightsNode(BinaryWriter bw, worldDistantLightsNode distantLightsNode)
    {
        bw.Write((string?)distantLightsNode.Data.DepotPath ?? "");
    }
    
    private static void WriteDistantGINode(BinaryWriter bw, worldDistantGINode distantGINode)
    {
        bw.Write((string?)distantGINode.DataAlbedo.DepotPath ?? "");
        bw.Write((string?)distantGINode.DataNormal.DepotPath ?? "");
        bw.Write((string?)distantGINode.DataHeight.DepotPath ?? "");
        WriteVector4(bw, distantGINode.SectorSpan);
    }

    
    private static void WriteAreaShapeNode(BinaryWriter bw, worldAreaShapeNode areaShapeNode)
    {
        WriteCColor(bw, areaShapeNode.Color);
        WriteAreaShapeOutline(bw, areaShapeNode.Outline);
    }

    private static void WriteAreaShapeOutline(BinaryWriter bw, AreaShapeOutline outline)
    {
        bw.Write(outline.Points.Count);
        foreach (var point in outline.Points)
        {
            WriteVector3(bw, point);
        }
        bw.Write(outline.Height);
    }
    
    private static void WriteTerrainMeshNode(BinaryWriter bw, worldTerrainMeshNode terrainMeshNode)
    {
        bw.Write((string?)terrainMeshNode.MeshRef.DepotPath ?? "");
    }
    
    private static void WriteCompiledSmartObjectsNode(BinaryWriter bw, worldCompiledSmartObjectsNode node)
    {
        bw.Write((string?)node.Resource.DepotPath ?? "");
    }
    
    private static void WriteAcousticZoneNode(BinaryWriter bw, worldAcousticZoneNode acousticZoneNode)
    {
        bw.Write((bool)acousticZoneNode.IsBlocker);
        bw.Write((string?)acousticZoneNode.TagName ?? "");
        bw.Write(acousticZoneNode.TagSpread);
    }
    
    private static void WriteAcousticPortalNode(BinaryWriter bw, worldAcousticPortalNode acousticPortalNode)
    {
        bw.Write(acousticPortalNode.Radius);
        bw.Write(acousticPortalNode.NominalRadius);
    }
    
    private static void WriteCompiledCrowdParkingSpaceNode(BinaryWriter bw, worldCompiledCrowdParkingSpaceNode crowdParkingSpaceNode)
    {
        bw.Write(crowdParkingSpaceNode.CrowdCreationIndex);
        bw.Write(crowdParkingSpaceNode.ParkingSpaceId);
    }
    
    private static void WriteGeometryShapeNode(BinaryWriter bw, worldGeometryShapeNode geometryShapeNode)
    {
        WriteCColor(bw, geometryShapeNode.Color);
    
        if (geometryShapeNode.Shape?.GetValue() != null)
        {
            bw.Write(true); // Has shape
            WriteGeometryShape(bw, geometryShapeNode.Shape);
        }
        else
        {
            bw.Write(false); // No shape
        }
    }

    private static void WriteGeometryShape(BinaryWriter bw, GeometryShape shape)
    {
        // Write vertices
        bw.Write(shape.Vertices.Count);
        foreach (var vertex in shape.Vertices)
        {
            WriteVector3(bw, vertex);
        }
    
        // Write indices
        bw.Write(shape.Indices.Count);
        foreach (var index in shape.Indices)
        {
            bw.Write(index);
        }
    
        // Write faces
        bw.Write(shape.Faces.Count);
        foreach (var face in shape.Faces)
        {
            WriteGeometryShapeFace(bw, face);
        }
    }

    private static void WriteGeometryShapeFace(BinaryWriter bw, GeometryShapeFace face)
    {
        bw.Write(face.Indices.Count);
        foreach (var index in face.Indices)
        {
            bw.Write(index);
        }
    }
    
    private static void WriteBendedMeshNode(BinaryWriter bw, worldBendedMeshNode bendedMeshNode)
    {
        bw.Write((string?)bendedMeshNode.Mesh.DepotPath ?? "");
        bw.Write((string?)bendedMeshNode.MeshAppearance ?? "");
        
        bw.Write(bendedMeshNode.DeformationData.Count);
        foreach (var matrix in bendedMeshNode.DeformationData)
        {
            WriteCMatrix(bw, matrix);
        }
    
        WriteBox(bw, bendedMeshNode.DeformedBox);
        bw.Write((bool)bendedMeshNode.IsBendedRoad);
        bw.Write((byte)bendedMeshNode.CastShadows.GetEnumValue());
        bw.Write((byte)bendedMeshNode.CastLocalShadows.GetEnumValue());
        bw.Write((bool)bendedMeshNode.RemoveFromRainMap);
        bw.Write((ushort)bendedMeshNode.NavigationSetting.NavmeshImpact.GetEnumValue());
        bw.Write(bendedMeshNode.Version);
    }

    private static void WriteCMatrix(BinaryWriter bw, CMatrix matrix)
    {
        WriteVector4(bw, matrix.X);
        WriteVector4(bw, matrix.Y);
        WriteVector4(bw, matrix.Z);
        WriteVector4(bw, matrix.W);
    }
    
    private static void WriteAcousticSectorNode(BinaryWriter bw, worldAcousticSectorNode acousticSectorNode)
    {
        bw.Write((string?)acousticSectorNode.Data.DepotPath ?? "");
        bw.Write(acousticSectorNode.InSectorCoordsX);
        bw.Write(acousticSectorNode.InSectorCoordsY);
        bw.Write(acousticSectorNode.InSectorCoordsZ);
        bw.Write(acousticSectorNode.GeneratorId);
        bw.Write(acousticSectorNode.EdgeMask);
    }
    
    private static void WriteAudioTagNode(BinaryWriter bw, worldAudioTagNode audioTagNode)
    {
        bw.Write((string?)audioTagNode.AudioTag ?? "");
        bw.Write(audioTagNode.Radius);
    }
    
    private static void WriteCommunityRegistryNode(BinaryWriter bw, worldCommunityRegistryNode communityRegistryNode)
    {
        bw.Write(communityRegistryNode.SpawnSetNameToCommunityID.Entries.Count);
        bw.Write(communityRegistryNode.CommunitiesData.Count);
        bw.Write(communityRegistryNode.WorkspotsPersistentData.Count);
        bw.Write((bool)communityRegistryNode.RepresentsCrowd);
    }
    private static void WriteStaticSoundEmitterNode(BinaryWriter bw, worldStaticSoundEmitterNode staticSoundEmitterNode)
    {
        bw.Write(staticSoundEmitterNode.Radius);
        bw.Write((string?)staticSoundEmitterNode.AudioName ?? "");
        bw.Write((bool)staticSoundEmitterNode.UsePhysicsObstruction);
        bw.Write((bool)staticSoundEmitterNode.OcclusionEnabled);
        bw.Write((bool)staticSoundEmitterNode.AcousticRepositioningEnabled);
        bw.Write(staticSoundEmitterNode.ObstructionChangeTime);
        bw.Write((bool)staticSoundEmitterNode.UseDoppler);
        bw.Write(staticSoundEmitterNode.DopplerFactor);
        bw.Write((bool)staticSoundEmitterNode.SetOpenDoorEmitter);
        bw.Write((string?)staticSoundEmitterNode.EmitterMetadataName ?? "");
        bw.Write((bool)staticSoundEmitterNode.OverrideRolloff);
        bw.Write(staticSoundEmitterNode.RolloffOverride);
        bw.Write((string?)staticSoundEmitterNode.AmbientPaletteTag ?? "");
    }
    
    private static void WriteStaticStickerNode(BinaryWriter bw, worldStaticStickerNode stickerNode)
    {
        bw.Write(stickerNode.Labels.Count);
        foreach (var label in stickerNode.Labels)
        {
            bw.Write(label);
        }
        bw.Write((bool)stickerNode.ShowBackground);
        WriteCColor(bw, stickerNode.TextColor);
        WriteCColor(bw, stickerNode.BackgroundColor);
        bw.Write(stickerNode.Sprites.Count);
        foreach (var sprite in stickerNode.Sprites)
        {
            bw.Write((string?)sprite.DepotPath ?? "");
        }
        bw.Write(stickerNode.SpriteSize);
        bw.Write((bool)stickerNode.AlignSpritesHorizontally);
        bw.Write(stickerNode.Scale);
        bw.Write(stickerNode.VisibilityDistance);
    }
    
    private static void WritePhysicalFractureFieldNode(BinaryWriter bw, worldPhysicalFractureFieldNode fractureFieldNode)
    {
        WritePhysicsTriggerShape(bw, fractureFieldNode.Shape);
        WritePhysicsFractureFieldParams(bw, fractureFieldNode.FractureFieldParams);
    }

    private static void WritePhysicsTriggerShape(BinaryWriter bw, physicsTriggerShape triggerShape)
    {
        bw.Write((byte)triggerShape.ShapeType.GetEnumValue());
        WriteVector3(bw, triggerShape.ShapeSize);
        WriteTransform(bw, triggerShape.ShapeLocalPose);
    }

    private static void WriteTransform(BinaryWriter bw, Transform transform)
    {
        WriteVector4(bw, transform.Position);
        WriteQuaternion(bw, transform.Orientation);
    }
    
    private static void WritePhysicsFractureFieldParams(BinaryWriter bw, physicsFractureFieldParams fractureParams)
    {
        WriteVector3(bw, fractureParams.Origin);
        bw.Write(fractureParams.FractureFieldValue);
        bw.Write(fractureParams.DestructionTypeMask.ToBitFieldString());
        bw.Write(fractureParams.FractureFieldTypeMask.ToBitFieldString());
        bw.Write(fractureParams.FractureFieldOptionsMask.ToBitFieldString());
        bw.Write((byte)fractureParams.FractureFieldEffect.GetEnumValue());
        bw.Write((byte)fractureParams.FractureFieldValueType.GetEnumValue());
    }

    
    private static void WriteInteriorMapNode(BinaryWriter bw, worldInteriorMapNode interiorMapNode)
    {
        bw.Write(interiorMapNode.Version);
        bw.Write(interiorMapNode.Coords);
    }
    
    private static void WriteTrafficCompiledNode(BinaryWriter bw, worldTrafficCompiledNode trafficNode)
    {
        WriteBox(bw, trafficNode.Aabb);
    }
    
    private static void WriteStaticFogVolumeNode(BinaryWriter bw, worldStaticFogVolumeNode fogVolumeNode)
    {
        bw.Write(fogVolumeNode.Priority);
        bw.Write((bool)fogVolumeNode.Absolute);
        bw.Write((bool)fogVolumeNode.ApplyHeightFalloff);
        bw.Write(fogVolumeNode.DensityFalloff);
        bw.Write(fogVolumeNode.BlendFalloff);
        bw.Write(fogVolumeNode.DensityFactor);
        bw.Write(fogVolumeNode.Absorption);
        bw.Write(fogVolumeNode.StreamingDistance);
        bw.Write(fogVolumeNode.AmbientScale);
        bw.Write((byte)fogVolumeNode.EnvColorGroup.GetEnumValue());
        WriteCColor(bw, fogVolumeNode.Color);
        bw.Write(fogVolumeNode.LightChannels.ToBitFieldString());
    }
    
    private static void WriteFoliageNode(BinaryWriter bw, worldFoliageNode foliageNode)
    {
        bw.Write((string?)foliageNode.Mesh.DepotPath ?? "");
        bw.Write((string?)foliageNode.MeshAppearance ?? "");
        bw.Write((string?)foliageNode.FoliageResource.DepotPath ?? "");
        WriteBox(bw, foliageNode.FoliageLocalBounds);
        bw.Write(foliageNode.AutoHideDistanceScale);
        bw.Write(foliageNode.LodDistanceScale);
        bw.Write(foliageNode.StreamingDistance);
        WriteFoliagePopulationSpanInfo(bw, foliageNode.PopulationSpanInfo);
        bw.Write(foliageNode.DestructionHash);
        bw.Write(foliageNode.MeshHeight);
    }

    private static void WriteFoliagePopulationSpanInfo(BinaryWriter bw, worldFoliagePopulationSpanInfo spanInfo)
    {
        bw.Write(spanInfo.StancesBegin);
        bw.Write(spanInfo.CketBegin);
        bw.Write(spanInfo.StancesCount);
        bw.Write(spanInfo.CketCount);
    }
    
    private static void WriteInstancedOccluderNode(BinaryWriter bw, worldInstancedOccluderNode occluderNode)
    {
        bw.Write(HashOccluderInstances(occluderNode.Buffer));
        WriteBox(bw, occluderNode.WorldBounds);
        bw.Write((byte)occluderNode.OccluderType.GetEnumValue());
        bw.Write(occluderNode.AutohideDistanceScale);
        bw.Write((string?)occluderNode.Mesh.DepotPath ?? "");
    }

    private static ulong HashOccluderInstances(CArray<worldInstancedOccluderNode_Buffer> occluderNodeBuffer)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        foreach (var transform in occluderNodeBuffer)
        {
            WriteVector4(bw, transform.Unknown1);
            WriteVector4(bw, transform.Unknown2);
            WriteVector4(bw, transform.Unknown3);
            WriteVector4(bw, transform.Unknown4);
        }
        
        return XXHash64.Compute(ms.GetBuffer());
    }
    
    private static void WriteBox(BinaryWriter bw, Box box)
    {
        WriteVector4(bw, box.Min);
        WriteVector4(bw, box.Max);
    }
    
    private static void WriteStaticOccluderMeshNode(BinaryWriter bw, worldStaticOccluderMeshNode occluderNode)
    {
        bw.Write((byte)occluderNode.OccluderType.GetEnumValue());
        WriteCColor(bw, occluderNode.Color);
        bw.Write(occluderNode.AutohideDistanceScale);
        bw.Write((string?)occluderNode.Mesh.DepotPath ?? "");
    }
    
    private static void WriteStaticLightNode(BinaryWriter bw, worldStaticLightNode lightNode)
    {
        bw.Write((int)lightNode.Type.GetEnumValue());
        WriteCColor(bw, lightNode.Color);
        bw.Write(lightNode.Radius);
        bw.Write((int)lightNode.Unit.GetEnumValue());
        bw.Write(lightNode.Intensity);
        bw.Write(lightNode.EV);
        bw.Write(lightNode.Temperature);
        bw.Write(lightNode.LightChannel.ToBitFieldString());
        bw.Write((bool)lightNode.SceneDiffuse);
        bw.Write(lightNode.SceneSpecularScale);
        bw.Write((bool)lightNode.Directional);
        bw.Write(lightNode.RoughnessBias);
        bw.Write(lightNode.ScaleGI);
        bw.Write(lightNode.ScaleEnvProbes);
        bw.Write((bool)lightNode.UseInTransparents);
        bw.Write(lightNode.ScaleVolFog);
        bw.Write((bool)lightNode.UseInParticles);
        bw.Write((byte)lightNode.Attenuation.GetEnumValue());
        bw.Write((bool)lightNode.ClampAttenuation);
        bw.Write((byte)lightNode.Group.GetEnumValue());
        bw.Write((int)lightNode.AreaShape.GetEnumValue());
        bw.Write((bool)lightNode.AreaTwoSided);
        bw.Write((bool)lightNode.SpotCapsule);
        bw.Write(lightNode.SourceRadius);
        bw.Write(lightNode.CapsuleLength);
        bw.Write(lightNode.AreaRectSideA);
        bw.Write(lightNode.AreaRectSideB);
        bw.Write(lightNode.InnerAngle);
        bw.Write(lightNode.OuterAngle);
        bw.Write(lightNode.Softness);
        bw.Write((bool)lightNode.EnableLocalShadows);
        bw.Write((bool)lightNode.EnableLocalShadowsForceStaticsOnly);
        bw.Write((byte)lightNode.ContactShadows.GetEnumValue());
        bw.Write(lightNode.ShadowAngle);
        bw.Write(lightNode.ShadowRadius);
        bw.Write(lightNode.ShadowFadeDistance);
        bw.Write(lightNode.ShadowFadeRange);
        bw.Write((int)lightNode.ShadowSoftnessMode.GetEnumValue());
        bw.Write((byte)lightNode.RayTracedShadowsPlatform.GetEnumValue());
        bw.Write(lightNode.RayTracingLightSourceRadius);
        bw.Write(lightNode.RayTracingContactShadowRange);
        bw.Write((string?)lightNode.IesProfile.DepotPath ?? "");
        bw.Write((byte)lightNode.EnvColorGroup.GetEnumValue());
        bw.Write(lightNode.ColorGroupSaturation);
        bw.Write(lightNode.PortalAngleCutoff);
        bw.Write((bool)lightNode.AllowDistantLight);
        bw.Write(lightNode.RayTracingIntensityScale);
        bw.Write((byte)lightNode.PathTracingLightUsage.GetEnumValue());
        bw.Write((bool)lightNode.PathTracingOverrideScaleGI);
        bw.Write(lightNode.RtxdiShadowStartingDistance);
        bw.Write(lightNode.AutoHideDistance);
    }
    
    private static void WriteStaticDecalNode(BinaryWriter bw, worldStaticDecalNode decalNode)
    {
        bw.Write((string?)decalNode.Material.DepotPath ?? "");
        bw.Write(decalNode.AutoHideDistance);
        bw.Write((bool)decalNode.VerticalFlip);
        bw.Write((bool)decalNode.HorizontalFlip);
        bw.Write(decalNode.Alpha);
        bw.Write(decalNode.NormalThreshold);
        bw.Write(decalNode.RoughnessScale);
        WriteHDRColor(bw, decalNode.DiffuseColorScale);
        bw.Write((bool)decalNode.IsStretchingEnabled);
        bw.Write((bool)decalNode.EnableNormalTreshold);
        bw.Write(decalNode.OrderNo);
        bw.Write((byte)decalNode.SurfaceType.GetEnumValue());
        bw.Write((byte)decalNode.NormalsBlendingMode.GetEnumValue());
        bw.Write((byte)decalNode.DecalRenderMode.GetEnumValue());
        bw.Write((bool)decalNode.ShouldCollectWithRayTracing);
        bw.Write(decalNode.ForcedAutoHideDistance);
        bw.Write(decalNode.DecalNodeVersion);
    }

    
    private static void WriteTerrainCollisionNode(BinaryWriter bw, worldTerrainCollisionNode terrainCollisionNode)
    {
        bw.Write(string.Join(",", terrainCollisionNode.Materials.Select(m => (string?)m ?? "")));
        bw.Write(string.Join(",", terrainCollisionNode.MaterialIndices.Select(m => ((byte)m).ToString())));
        WriteWorldTransform(bw, terrainCollisionNode.ActorTransform);
        WriteVector4(bw, terrainCollisionNode.Extents);
        bw.Write(terrainCollisionNode.StreamingDistance);
        bw.Write(terrainCollisionNode.RowScale);
        bw.Write(terrainCollisionNode.ColumnScale);
        bw.Write(terrainCollisionNode.HeightScale);
        bw.Write((bool)terrainCollisionNode.IncreaseStreamingDistance);
    }

    private static void WriteWorldTransform(BinaryWriter bw, WorldTransform transform)
    {
        WriteWorldPosition(bw, transform.Position);
        WriteQuaternion(bw, transform.Orientation);
    }

    
    private static void WritePhysicalDestructionNode(BinaryWriter bw, worldPhysicalDestructionNode destructionNode)
    {
        bw.Write((string?)destructionNode.Mesh.DepotPath ?? "");
        bw.Write((string?)destructionNode.MeshAppearance ?? "");
        bw.Write(destructionNode.ForceLODLevel);
        bw.Write(destructionNode.ForceAutoHideDistance);
        bw.Write(JsonConvert.SerializeObject(destructionNode.DestructionParams));
        bw.Write(JsonConvert.SerializeObject(destructionNode.DestructionLevelData));
        bw.Write((string?)destructionNode.AudioMetadata ?? "");
        bw.Write((ushort)destructionNode.NavigationSetting.NavmeshImpact.GetEnumValue());
        bw.Write((bool)destructionNode.UseMeshNavmeshSettings);
        bw.Write(destructionNode.SystemsToNotifyFlags);
    }
    
    private static void WriteEntityNode(BinaryWriter bw, worldEntityNode entityNode)
    {
        bw.Write((string?)entityNode.EntityTemplate.DepotPath ?? "");
        bw.Write((string?)entityNode.AppearanceName ?? "");
        bw.Write((byte)entityNode.IoPriority.GetEnumValue());
        bw.Write(entityNode.EntityLod);
    }
    
    private static void WriteEffectNode(BinaryWriter bw, worldEffectNode effectNode)
    {
        bw.Write((string?)effectNode.Effect.DepotPath ?? "");
        bw.Write(effectNode.StreamingDistanceOverride);
    }
    
    private static void WriteReflectionProbeNode(BinaryWriter bw, worldReflectionProbeNode reflectionProbeNode)
    {
        bw.Write((string?)reflectionProbeNode.ProbeDataRef.DepotPath ?? "");
        bw.Write(reflectionProbeNode.Priority);
        bw.Write((bool)reflectionProbeNode.GlobalProbe);
        bw.Write((bool)reflectionProbeNode.BoxProjection);
        bw.Write((byte)reflectionProbeNode.NeighborMode.GetEnumValue());
        WriteVector3(bw, reflectionProbeNode.EdgeScale);
        bw.Write(reflectionProbeNode.LightChannels.ToBitFieldString());
        bw.Write(reflectionProbeNode.EmissiveScale);
        bw.Write(reflectionProbeNode.ReflectionDimming);
        WriteHDRColor(bw, reflectionProbeNode.SimpleFogColor);
        bw.Write(reflectionProbeNode.SimpleFogDensity);
        bw.Write(reflectionProbeNode.SkyScale);
        bw.Write((bool)reflectionProbeNode.AllInShadow);
        bw.Write((bool)reflectionProbeNode.HideSkyColor);
        bw.Write((bool)reflectionProbeNode.VolFogAmbient);
        bw.Write(reflectionProbeNode.BrightnessEVClamp);
        bw.Write((byte)reflectionProbeNode.AmbientMode.GetEnumValue());
        WriteVector3(bw, reflectionProbeNode.CaptureOffset);
        bw.Write(reflectionProbeNode.NearClipDistance);
        bw.Write(reflectionProbeNode.FarClipDistance);
        bw.Write(reflectionProbeNode.VolumeChannels.ToBitFieldString());
        bw.Write(reflectionProbeNode.BlendRange);
        bw.Write(reflectionProbeNode.StreamingDistance);
        bw.Write(reflectionProbeNode.StreamingHeight);
        bw.Write((bool)reflectionProbeNode.SubScene);
        bw.Write((bool)reflectionProbeNode.NoFadeBlend);
    }

    private static void WriteHDRColor(BinaryWriter bw, HDRColor color)
    {
        bw.Write(color.Red);
        bw.Write(color.Green);
        bw.Write(color.Blue);
        bw.Write(color.Alpha);
    }
    
    private static void WriteCColor(BinaryWriter bw, CColor color)
    {
        bw.Write(color.Red);
        bw.Write(color.Green);
        bw.Write(color.Blue);
        bw.Write(color.Alpha);
    }


    
    private static void WriteInstMeshNode(BinaryWriter bw, worldInstancedMeshNode istMeshNode, ref ulong[] actorHashes)
    {
        HashMeshTransformsBuffer(istMeshNode.WorldTransformsBuffer, ref actorHashes);
        
        bw.Write((string?)istMeshNode.Mesh.DepotPath ?? "");
        bw.Write((string?)istMeshNode.MeshAppearance ?? "");
        bw.Write((byte)istMeshNode.CastShadows.GetEnumValue());
        bw.Write((byte)istMeshNode.CastLocalShadows.GetEnumValue());
        bw.Write((byte)istMeshNode.OccluderType.GetEnumValue());
        bw.Write(istMeshNode.MeshLODScales);
        bw.Write(istMeshNode.OccluderAutohideDistanceScale);
        bw.Write(istMeshNode.Version);

    }

    private static void HashMeshTransformsBuffer(worldRenderProxyTransformBuffer wtb, ref ulong[] actorHashes)
    {
        var transformSpan = ((WorldTransformsBuffer)wtb.SharedDataBuffer.Chunk.Buffer.Data).Transforms
            .ToArray().AsSpan((int)(uint)wtb.StartIndex, (int)(uint)wtb.NumElements);

        actorHashes = transformSpan.ToArray().Select(HashNodeTransform).ToArray();
    }

    private static ulong HashNodeTransform(worldNodeTransform transform)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        
        WriteVector3(bw, transform.Translation);
        WriteQuaternion(bw, transform.Rotation);
        WriteVector3(bw, transform.Scale);
        
        return XXHash64.Compute(ms.GetBuffer());
    }
    
    private static void WriteInstDestructibleMeshNode(BinaryWriter bw, worldInstancedDestructibleMeshNode istDestNode, ref ulong[] actorHashes)
    {
        HashWorldDestTransformsBuffer(istDestNode.CookedInstanceTransforms, ref actorHashes);;
        
        bw.Write((string?)istDestNode.StaticMesh.DepotPath ?? "");
        bw.Write((string?)istDestNode.StaticMeshAppearance ?? "");
        bw.Write((byte)istDestNode.SimulationType.GetEnumValue());
        bw.Write((byte)istDestNode.FilterDataSource.GetEnumValue());
        bw.Write((bool)istDestNode.StartInactive);
        bw.Write((bool)istDestNode.TurnDynamicOnImpulse);
        bw.Write((bool)istDestNode.UseAggregate);
        bw.Write((bool)istDestNode.EnableSelfCollisionInAggregate);
        bw.Write((bool)istDestNode.IsDestructible);
        bw.Write(JsonConvert.SerializeObject(istDestNode.FilterData));
        bw.Write(istDestNode.DamageThreshold);
        bw.Write(istDestNode.DamageEndurance);
        bw.Write((bool)istDestNode.AccumulateDamage);
        bw.Write(istDestNode.ImpulseToDamage);
        bw.Write((string?)istDestNode.FracturingEffect.DepotPath ?? "");
        bw.Write((string?)istDestNode.IdleEffect.DepotPath ?? "");
        bw.Write((bool)istDestNode.IsPierceable);
        bw.Write((bool)istDestNode.IsWorkspot);
        
        bw.Write((ushort)istDestNode.NavigationSetting.NavmeshImpact.GetEnumValue());
    
        bw.Write((bool)istDestNode.UseMeshNavmeshSettings);
        bw.Write(istDestNode.SystemsToNotifyFlags);
    }

    private static void HashWorldDestTransformsBuffer(worldTransformBuffer wtb, ref ulong[] actorHashes)
    {
        var transformSpan = ((CookedInstanceTransformsBuffer)wtb.SharedDataBuffer.Chunk.Buffer.Data).Transforms
            .ToArray().AsSpan((int)(uint)wtb.StartIndex, (int)(uint)wtb.NumElements);

        actorHashes = transformSpan.ToArray().Select(HashTransform).ToArray();
    }

    private static ulong HashTransform(Transform transform)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        
        WriteVector4(bw, transform.Position);
        WriteQuaternion(bw, transform.Orientation);
        
        return XXHash64.Compute(ms.GetBuffer());
    }
    
    private static void WriteCollisionNode(BinaryWriter bw, worldCollisionNode collisionNode, ref ulong[] actorHashes)
    {
        var colBuffer = collisionNode.CompiledData.Data as CollisionBuffer;
        actorHashes = colBuffer?.Actors.Select(HashCollisionActor).ToArray() ?? [];
        
        bw.Write(collisionNode.NumActors);
        bw.Write(collisionNode.NumShapeInfos);
        bw.Write(collisionNode.NumShapePositions);
        bw.Write(collisionNode.NumShapeRotations);
        bw.Write(collisionNode.NumScales);
        bw.Write(collisionNode.NumMaterials);
        bw.Write(collisionNode.NumPresets);
        bw.Write(collisionNode.NumMaterialIndices);
        bw.Write(collisionNode.NumShapeIndices);
        bw.Write(collisionNode.SectorHash);
        WriteVector4(bw, collisionNode.Extents);
        bw.Write(collisionNode.Lod);
        bw.Write(collisionNode.ResourceVersion);
    }

    private static ulong HashCollisionActor(CollisionActor actor)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        
        WriteWorldPosition(bw, actor.Position);
        WriteQuaternion(bw, actor.Orientation);
        bw.Write(HashCollisionShapes(actor.Shapes));
        WriteVector3(bw, actor.Scale);
        bw.Write(actor.Uk1);
        bw.Write(actor.Uk2);
        
        return XXHash64.Compute(ms.GetBuffer());
    }

    private static ulong HashCollisionShapes(CArray<CollisionShape> shapes)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        
        foreach (var shape in shapes)
        {
            bw.Write((byte)shape.ShapeType.GetEnumValue());
            WriteVector3(bw, shape.Position);
            WriteQuaternion(bw, shape.Rotation);
            bw.Write((string?)shape.Preset ?? "");
            bw.Write((byte)shape.ProxyType.GetEnumValue());
            bw.Write(string.Join(" ", shape.Materials));
            bw.Write(shape.Uk1);
            bw.Write(shape.Uk2);
            bw.Write(shape.Uk3);

            switch (shape)
            {
                case CollisionShapeMesh meshShape:
                    bw.Write(meshShape.Hash);
                    break;
                case CollisionShapeSimple simpleShape:
                    WriteVector3(bw, simpleShape.Size);
                    break;
            }
        }

        return XXHash64.Compute(ms.GetBuffer());
    }
    
    private static void WriteMeshNode(BinaryWriter bw, worldMeshNode meshNode)
    {
        bw.Write((string?)meshNode.Mesh.DepotPath ?? "");
        bw.Write((string?)meshNode.MeshAppearance ?? "");
        bw.Write(meshNode.ForceAutoHideDistance);
        bw.Write((byte)meshNode.OccluderType.GetEnumValue());
        bw.Write(meshNode.OccluderAutohideDistanceScale);
        bw.Write((byte)meshNode.CastLocalShadows.GetEnumValue());
        bw.Write((byte)meshNode.CastShadows.GetEnumValue());
        bw.Write((byte)meshNode.CastLocalShadows.GetEnumValue());
        bw.Write((byte)meshNode.CastRayTracedGlobalShadows.GetEnumValue());
        bw.Write((byte)meshNode.CastRayTracedLocalShadows.GetEnumValue());
        bw.Write((bool)meshNode.WindImpulseEnabled);
        bw.Write((bool)meshNode.RemoveFromRainMap);
        bw.Write(meshNode.RenderSceneLayerMask.ToBitFieldString());
        bw.Write(meshNode.LodLevelScales);
        bw.Write(meshNode.Version);
    }
    
    private static void WriteBaseNode(BinaryWriter bw, worldNode node)
    {
        bw.Write(node.GetType().ToString());
        bw.Write((string?)node.DebugName ?? "");
        bw.Write(node.SourcePrefabHash);
        WriteVector3(bw, node.ProxyScale);
        bw.Write((int)node.Tag.GetEnumValue());
        bw.Write((int)node.TagExt.GetEnumValue());
        bw.Write((bool)node.IsVisibleInGame);
        bw.Write((bool)node.IsHostOnly);
    }
    
    private static void WriteVector3(BinaryWriter bw, Vector3 v)
    {
        if (v == null)
            return;
        
        bw.Write(v.X);
        bw.Write(v.Y);
        bw.Write(v.Z);
    }
    
    private static void WriteVector4(BinaryWriter bw, Vector4 v)
    {
        bw.Write(v.X);
        bw.Write(v.Y);
        bw.Write(v.Z);
        bw.Write(v.W);
    }

    private static void WriteWorldPosition(BinaryWriter bw, WorldPosition v)
    {
        bw.Write(v.X);
        bw.Write(v.Y);
        bw.Write(v.Z);
    }
    
    private static void WriteQuaternion(BinaryWriter bw, Quaternion q)
    {
        bw.Write(q.I);
        bw.Write(q.J);
        bw.Write(q.K);
        bw.Write(q.R);
    }
    
    /// <summary>
    /// Assumes a valid game exe path, and does not do its own checks
    /// </summary>
    /// <param name="gameExePath"></param>
    /// <returns></returns>
    private string? GetGameVersion(string gameExePath)
    {
        var fileVerInfo = FileVersionInfo.GetVersionInfo(gameExePath);
        return fileVerInfo.ProductVersion;
    }
    
}