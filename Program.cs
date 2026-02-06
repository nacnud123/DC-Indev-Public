// Main class that prints out the controls then runs the game | DA | 2/5/26
using VoxelEngine.Core;

Console.WriteLine("=== Voxel Engine ===");
Console.WriteLine("Controls:");
Console.WriteLine("  WASD      - Move");
Console.WriteLine("  LMB       - Break block / Kill entity");
Console.WriteLine("  RMB       - Place block");
Console.WriteLine("  0-9       - Select block");
Console.WriteLine("  R         - Reset position");
Console.WriteLine("  X         - Wireframe");
Console.WriteLine("  P         - Spawn pig");
Console.WriteLine("  ESC       - Pause");
Console.WriteLine("  Tab       - Release cursor");
Console.WriteLine("  F         - Toggle fly mode");
Console.WriteLine("  E         - Toggle Inventory");
Console.WriteLine("  Space     - Jump / Fly up");
Console.WriteLine("  Ctrl      - Fly down");
Console.WriteLine("  Shift     - Sprint");
Console.WriteLine("  + / -     - Increase / decrease render distance");
Console.WriteLine("  Mouse     - Look");

Console.WriteLine();

using var game = new Game(1280, 720, "DuncanCraft 2000 InDev");
game.Run();
