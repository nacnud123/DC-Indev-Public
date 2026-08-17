namespace VoxelEngine.Net;

/// Beta 1.7.3 (protocol 14) packet ids. Keeping the real numbers costs nothing and makes any
/// reference material about the era directly applicable.
public enum PacketId : byte
{
    KeepAlive = 0x00, // no payload in beta - just the id byte
    LoginRequest = 0x01, // C->S: protocol, username, mapSeed(long), dimension(byte)

    // S->C: entityId, "", mapSeed(long), dimension(byte)
    Handshake = 0x02, // C->S: username | S->C: connection hash ("-" = offline mode)
    ChatMessage = 0x03, // string, max 119 chars
    TimeUpdate = 0x04, // long
    EntityEquipment = 0x05, // S->C: entityId, slot(short), itemId(short) - beta's held-item packet
    SpawnPosition = 0x06, // int x, y, z
    UseEntity = 0x07, // C->S: int fromEntity, int targetEntity, bool leftClick (attack vs interact)
    UpdateHealth = 0x08, // short
    Respawn = 0x09,
    Player = 0x0A, // bool onGround
    PlayerPosition = 0x0B, // double x, stance, y, z + bool
    PlayerLook = 0x0C, // float yaw, pitch + bool
    PlayerPositionLook = 0x0D, // both of the above
    PlayerDigging = 0x0E, // byte status, int x, byte y, int z, byte face
    PlayerBlockPlacement = 0x0F, // int x, byte y, int z, byte direction, item
    HoldingChange = 0x10, // short slot
    Animation = 0x12, // int entityId, byte animate
    NamedEntitySpawn = 0x14, // int id, string name, int x,y,z (1/32), byte yaw, pitch, short item
    PickupSpawn = 0x15,
    CollectItem = 0x16,
    AddObject = 0x17,
    MobSpawn = 0x18,
    EntityPainting = 0x19, // int id, string art, int x, y, z, int facing
    EntityVelocity = 0x1C,
    DestroyEntity = 0x1D, // int entityId
    Entity = 0x1E, // int entityId - "still alive", no movement
    EntityRelativeMove = 0x1F, // int id, sbyte dx, dy, dz
    EntityLook = 0x20,
    EntityLookRelMove = 0x21,
    EntityTeleport = 0x22, // int id, int x,y,z (1/32), byte yaw, pitch
    PreChunk = 0x32, // int x, int z, bool mode (true=allocate, false=drop)
    MapChunk = 0x33, // int x, short y, int z, byte sx-1, sy-1, sz-1, int len, byte[] data
    MultiBlockChange = 0x34,
    BlockChange = 0x35, // int x, byte y, int z, byte type, byte metadata
    Explosion = 0x3C,
    SoundEffect = 0x3D, // int effectId, int x, byte y, int z, int data
    OpenWindow = 0x64,
    CloseWindow = 0x65,
    WindowClick = 0x66,
    SetSlot = 0x67,
    WindowItems = 0x68,
    UpdateProgressBar = 0x69, // byte windowId, short bar, short value - furnace cook/fuel bars
    Transaction = 0x6A, // window confirm - beta's inventory ack
    UpdateSign = 0x82,

    // Past beta's id range, so a packet log makes it obvious this one is ours. Both directions carry
    // entityId then the PNG blob - the proposal's C->S sketch omits the entityId, but one id means
    // one shape in PacketLayout, so the client writes its own (0 before login assigns one).
    PlayerSkin = 0x86, // int entityId, int length, byte[] png

    DisconnectKick = 0xFF, // string reason
}