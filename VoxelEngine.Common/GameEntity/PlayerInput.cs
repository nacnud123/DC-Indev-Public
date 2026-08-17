// What the player is asking to do this tick, independent of how it was asked. | DA

namespace VoxelEngine.GameEntity;

/// <summary>
/// One tick's worth of movement intent.
///
/// <c>Player</c> used to read the keyboard directly through <c>Game.Instance.IsKeyDown(...)</c>,
/// which tied movement to Silk.NET's <c>Key</c> type and to a live input device. Neither exists
/// on a dedicated server, where the same movement code has to run driven by packets instead.
///
/// So the keyboard read moves out to the host: the client fills this in from
/// <c>Keybindings</c> each frame, a server fills it in from what the client sent, and
/// <c>Player</c> just consumes intent. This is the same shape Beta used - the wire protocol
/// carries player *state*, and the movement code doesn't care where it came from.
/// </summary>
public struct PlayerInput
{
    public bool MoveForward;
    public bool MoveBack;
    public bool MoveLeft;
    public bool MoveRight;

    /// <summary>Held: rises while flying and while swimming.</summary>
    public bool Jump;

    /// <summary>
    /// Edge-triggered jump, for walking on the ground. Kept separate from held <see cref="Jump"/>
    /// because the two behave differently: holding the key should keep you rising in water, but
    /// must not re-trigger a ground jump every tick.
    /// </summary>
    public bool JumpPressed;

    /// <summary>Held: descends while flying.</summary>
    public bool Descend;

    public bool Sprint;

    /// <summary>
    /// Held: scales movement input to 0.3x, lowers the eye, and stops the player walking off a
    /// ledge. Takes priority over <see cref="Sprint"/> when both are held.
    /// </summary>
    public bool Sneak;

    /// <summary>
    /// Edge-triggered, not held: true only on the tick the toggle was first pressed. The host is
    /// responsible for that edge detection, since only it knows when a tick began.
    /// </summary>
    public bool ToggleFly;

    /// <summary>No keys held. What a server assumes for a player it hasn't heard from.</summary>
    public static readonly PlayerInput None = default;

    /// <summary>True if any directional key is held - used to drive walk animations.</summary>
    public readonly bool HasMovement => MoveForward || MoveBack || MoveLeft || MoveRight;
}
