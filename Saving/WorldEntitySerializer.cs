// Entity save/load logic extracted from Game.cs | DA | 2026
using System.Collections.Generic;

using VoxelEngine.GameEntity;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;

namespace VoxelEngine.Saving;

/// <summary>
/// Converts live `Entity` instances to/from `SavedEntity` DTOs (the flat, XML-serializable shape stored in world_info.xml). Each entity type needs an explicit case here on both Save and Load - there's no reflection/polymorphic serialization, so adding a new persistable mob type means adding a branch in both methods.
/// </summary>
internal static class WorldEntitySerializer
{
    // NOTE: Spider is not handled here (no case in this switch, nor in Load below), so spiders are silently dropped on world save/reload. If that's unintentional, add a Spider case mirroring Zombie/Skeleton/Stalker in both Save and Load.
    internal static List<SavedEntity> Save(IEnumerable<Entity> entities)
    {
        var list = new List<SavedEntity>();
        foreach (var entity in entities)
        {
            switch (entity)
            {
                case PaintingEntity:
                    break; // paintings saved separately in WorldSaveData.Paintings

                case Pig pig:
                    list.Add(MakeSavedMob("Pig", pig));
                    break;

                case Sheep sheep:
                    var savedSheep = MakeSavedMob("Sheep", sheep);
                    savedSheep.IsSheared = sheep.IsSheared;
                    list.Add(savedSheep);
                    break;

                case Zombie zombie:
                    list.Add(MakeSavedMob("Zombie", zombie));
                    break;

                case Skeleton skeleton:
                    list.Add(MakeSavedMob("Skeleton", skeleton));
                    break;

                case Stalker stalker:
                    list.Add(MakeSavedMob("Stalker", stalker));
                    break;

                case DroppedItemEntity drop:
                    list.Add(new SavedEntity
                    {
                        Type = "DroppedItem",
                        X = drop.Position.X,
                        Y = drop.Position.Y,
                        Z = drop.Position.Z,
                        Stack = SerializableStack.From(drop.Stack),
                    });
                    break;
            }
        }
        return list;
    }

    // Reconstructs entities from their saved DTOs and re-adds them to the world. Unknown/unhandled Type strings (or a DroppedItem with a null Stack) resolve to `null` and are silently skipped.
    internal static void Load(List<SavedEntity> saved, World world, Texture worldTexture)
    {
        foreach (var se in saved)
        {
            var pos = new Vector3(se.X, se.Y, se.Z);

            Entity? entity = se.Type switch
            {
                "Pig"         => new Pig(pos)      { Yaw = se.Yaw, Health = se.Health },
                "Sheep"       => new Sheep(pos)    { Yaw = se.Yaw, Health = se.Health, IsSheared = se.IsSheared },
                "Zombie"      => new Zombie(pos)   { Yaw = se.Yaw, Health = se.Health },
                "Skeleton"    => new Skeleton(pos) { Yaw = se.Yaw, Health = se.Health },
                "Stalker"     => new Stalker(pos)  { Yaw = se.Yaw, Health = se.Health },
                "DroppedItem" => se.Stack != null
                    ? new DroppedItemEntity(pos, se.Stack.ToItemStack(), worldTexture)
                    : null,
                _ => null,
            };

            if (entity != null)
                world.AddEntity(entity);
        }
    }

    // Common fields shared by every simple mob (position/yaw/health); type-specific extras like Sheep.IsSheared are set by the caller after this returns.
    private static SavedEntity MakeSavedMob(string type, Entity e) => new()
    {
        Type = type,
        X = e.Position.X,
        Y = e.Position.Y,
        Z = e.Position.Z,
        Yaw = e.Yaw,
        Health = e.Health,
    };
}
