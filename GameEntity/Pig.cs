// Pig entity class, holds stuff related to pig model, texture, and settings | DA | 2/5/26
using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.GameEntity.AI;
using VoxelEngine.GameEntity.AI.Pathfinding;
using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity;

public class Pig : Entity
{
    private const string MODEL_PATH = "Resources/Entities/Pig/Pig.obj";
    private const string TEXTURE_PATH = "Resources/Entities/Pig/body.png";
    
    public override float Width { get => 0.9f; set { } }
    public override float Height { get => 0.9f; set { } }
    public override float Scale { get => 4f; set { } }
    public override float WalkSpeed { get => 2f; set { } }

    public override int Health { get; set; } = 20;

    public Pig(Vector3 position)
    {
        Position = position;
        InitShader();
        Model = EntityModel.Load(MODEL_PATH, TEXTURE_PATH);
        CurrentAI = new PassiveEntityAi(this);
    }

    public override void Tick(World world)
    {
        base.Tick(world);
        CurrentAI.Tick(world);
    }
}
