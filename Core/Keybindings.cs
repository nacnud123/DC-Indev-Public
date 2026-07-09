// Stores and persists all player keybindings. | DA | 3/8/26

using Silk.NET.Input;
using SilkKey = Silk.NET.Input.Key;

namespace VoxelEngine.Core;

/// <summary>
/// Static, process-wide registry mapping abstract player <see cref="Action"/>s to concrete Silk.NET keyboard keys, plus load/save of a simple "Action=Key" text config file so rebindings persist between sessions. Game code should always look up keys through this class (e.g. <see cref="MoveForward"/>) rather than hardcoding a <see cref="SilkKey"/>, so that any future in-game rebind UI works everywhere automatically.
/// </summary>
public static class Keybindings
{
    /// <summary>
    /// Abstract, rebindable player actions. Game logic should branch on these rather than on raw keys, so a user rebind takes effect without touching gameplay code.
    /// </summary>
    public enum Action
    {
        MoveForward,
        MoveBack,
        MoveLeft,
        MoveRight,
        Jump,
        Sprint,
        FlyDown,
        ToggleFly,
        Inventory,
        DropItem,
        Wireframe,
        ResetPosition,
        ToggleCursor,
        Screenshot,
        RenderDistUp,
        RenderDistDown
    }

    // Default bindings, used until/unless overridden by a saved keybindings.cfg file via Load().
    private static readonly Dictionary<Action, SilkKey> Bindings = new()
    {
        [Action.MoveForward] = SilkKey.W,
        [Action.MoveBack] = SilkKey.S,
        [Action.MoveLeft] = SilkKey.A,
        [Action.MoveRight] = SilkKey.D,
        [Action.Jump] = SilkKey.Space,
        [Action.Sprint] = SilkKey.ShiftLeft,
        [Action.FlyDown] = SilkKey.ControlLeft,
        [Action.ToggleFly] = SilkKey.F,
        [Action.Inventory] = SilkKey.E,
        [Action.DropItem] = SilkKey.Q,
        [Action.Wireframe] = SilkKey.X,
        [Action.ResetPosition] = SilkKey.R,
        [Action.ToggleCursor] = SilkKey.Tab,
        [Action.Screenshot] = SilkKey.F7,
        [Action.RenderDistUp] = SilkKey.Equal,
        [Action.RenderDistDown] = SilkKey.Minus,
    };

    /// <summary>Looks up the key currently bound to an action.</summary>
    public static SilkKey Get(Action action) => Bindings[action];
    /// <summary>Rebinds an action to a new key at runtime (not persisted until <see cref="Save"/> is called).</summary>
    public static void Set(Action action, SilkKey key) => Bindings[action] = key;

    // Convenience properties so call sites read as `Keybindings.Jump` instead of `Keybindings.Get(Keybindings.Action.Jump)`.
    public static SilkKey MoveForward => Get(Action.MoveForward);
    public static SilkKey MoveBack => Get(Action.MoveBack);
    public static SilkKey MoveLeft => Get(Action.MoveLeft);
    public static SilkKey MoveRight => Get(Action.MoveRight);
    public static SilkKey Jump => Get(Action.Jump);
    public static SilkKey Sprint => Get(Action.Sprint);
    public static SilkKey FlyDown => Get(Action.FlyDown);
    public static SilkKey ToggleFly => Get(Action.ToggleFly);
    public static SilkKey Inventory => Get(Action.Inventory);
    public static SilkKey DropItem => Get(Action.DropItem);
    public static SilkKey Wireframe => Get(Action.Wireframe);
    public static SilkKey ResetPosition => Get(Action.ResetPosition);
    public static SilkKey ToggleCursor => Get(Action.ToggleCursor);
    public static SilkKey Screenshot => Get(Action.Screenshot);
    public static SilkKey RenderDistUp => Get(Action.RenderDistUp);
    public static SilkKey RenderDistDown => Get(Action.RenderDistDown);

    // Relative path (working directory) to the plain-text keybindings config file.
    private const string SavePath = "keybindings.cfg";

    /// <summary>
    /// Writes all current bindings to <see cref="SavePath"/> as simple "Action=Key" lines (one per binding), using the enum names' ToString() representations so they can be round-tripped by Enum.TryParse in <see cref="Load"/>.
    /// </summary>
    public static void Save()
    {
        using var w = new StreamWriter(SavePath);
        foreach (var (action, key) in Bindings)
            w.WriteLine($"{action}={key}");
    }

    /// <summary>
    /// Reads <see cref="SavePath"/> if it exists and overlays any successfully-parsed bindings on top of the defaults. Malformed lines or unrecognized enum names are silently skipped, leaving the default binding for that action in place (keeps a hand-edited or stale config file from crashing the game on startup).
    /// </summary>
    public static void Load()
    {
        if (!File.Exists(SavePath))
            return;

        foreach (var line in File.ReadAllLines(SavePath))
        {
            var parts = line.Split('=');
            if (parts.Length == 2 && Enum.TryParse<Action>(parts[0], out var action) &&
                Enum.TryParse<SilkKey>(parts[1], out var key))
            {
                Bindings[action] = key;
            }
        }
    }
}
