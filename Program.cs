// Main class that prints out the controls then runs the game | DA | 2/5/26
using VoxelEngine.Core;

Console.WriteLine("=== DC Indef ===");
Console.WriteLine("By: Duncan Armstrong");
Console.WriteLine("Don't forget to star the repo and have fun!");

Console.WriteLine();

using var game = new Game(1280, 720, "DuncanCraft 2000 InDev");
game.Run();
