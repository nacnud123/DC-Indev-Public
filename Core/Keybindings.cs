// Stores and persists all player keybindings. | DA | 3/8/26

using OpenTK.Windowing.GraphicsLibraryFramework;

namespace VoxelEngine.Core;

public static class Keybindings
{
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

    private static readonly Dictionary<Action, Keys> Bindings = new()
    {
        [Action.MoveForward] = Keys.W,
        [Action.MoveBack] = Keys.S,
        [Action.MoveLeft] = Keys.A,
        [Action.MoveRight] = Keys.D,
        [Action.Jump] = Keys.Space,
        [Action.Sprint] = Keys.LeftShift,
        [Action.FlyDown] = Keys.LeftControl,
        [Action.ToggleFly] = Keys.F,
        [Action.Inventory] = Keys.E,
        [Action.DropItem] = Keys.Q,
        [Action.Wireframe] = Keys.X,
        [Action.ResetPosition] = Keys.R,
        [Action.ToggleCursor] = Keys.Tab,
        [Action.Screenshot] = Keys.F7,
        [Action.RenderDistUp] = Keys.Equal,
        [Action.RenderDistDown] = Keys.Minus,
    };

    public static Keys Get(Action action) => Bindings[action];
    public static void Set(Action action, Keys key) => Bindings[action] = key;

    public static Keys MoveForward => Get(Action.MoveForward);
    public static Keys MoveBack => Get(Action.MoveBack);
    public static Keys MoveLeft => Get(Action.MoveLeft);
    public static Keys MoveRight => Get(Action.MoveRight);
    public static Keys Jump => Get(Action.Jump);
    public static Keys Sprint => Get(Action.Sprint);
    public static Keys FlyDown => Get(Action.FlyDown);
    public static Keys ToggleFly => Get(Action.ToggleFly);
    public static Keys Inventory => Get(Action.Inventory);
    public static Keys DropItem => Get(Action.DropItem);
    public static Keys Wireframe => Get(Action.Wireframe);
    public static Keys ResetPosition => Get(Action.ResetPosition);
    public static Keys ToggleCursor => Get(Action.ToggleCursor);
    public static Keys Screenshot => Get(Action.Screenshot);
    public static Keys RenderDistUp => Get(Action.RenderDistUp);
    public static Keys RenderDistDown => Get(Action.RenderDistDown);

    private const string SavePath = "keybindings.cfg";

    public static void Save()
    {
        using var w = new StreamWriter(SavePath);
        foreach (var (action, key) in Bindings)
            w.WriteLine($"{action}={key}");
    }

    public static void Load()
    {
        if (!File.Exists(SavePath))
            return;

        foreach (var line in File.ReadAllLines(SavePath))
        {
            var parts = line.Split('=');
            if (parts.Length == 2 && Enum.TryParse<Action>(parts[0], out var action) &&
                Enum.TryParse<Keys>(parts[1], out var key))
            {
                Bindings[action] = key;
            }
        }
    }
}