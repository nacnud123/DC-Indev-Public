// Chat and slash commands. Beta's command set was small and all server-side. | Stage 8

using VoxelEngine.Net;

namespace VoxelEngine.Server;

public sealed partial class DuncanCraftServer
{
    private const int MAX_CHAT_LENGTH = 100;      // b1.7.3's NetServerHandler.handleChat limit

    private void HandleChat(ServerPlayer player, string message)
    {
        if (message.Length > MAX_CHAT_LENGTH)
        {
            player.Connection.Kick("Chat message too long");
            return;
        }

        message = message.Trim();

        if (message.Length == 0)
            return;

        // b1.7.3 validated against its font's allowed-character set, which excludes §. Skipping that
        // check is how a modified client sends "§f<Notch> hi" and forges a message from anyone.
        if (message.Any(c => c == '§' || c < ' ' || c == '\u007F'))
        {
            player.Connection.Kick("Illegal characters in chat");
            return;
        }

        if (message.StartsWith('/'))
        {
            RunCommand(player, message[1..]);
            return;
        }

        string line = $"<{player.Name}> {message}";
        mLog.Log(LogLevel.Chat, line);
        Broadcast(PacketId.ChatMessage, w => w.WriteString(line));
    }

    /// §c is the red colour code, used for errors. A null sender means the server console, which is
    /// always op.
    private void RunCommand(ServerPlayer? sender, string command)
    {
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return;

        bool isOp = sender?.IsOp ?? true;

        void Reply(string text)
        {
            if (sender != null)
                SendTo(sender, PacketId.ChatMessage, w => w.WriteString(text));
            else
                mLog.Log(LogLevel.Info, text);
        }

        switch (parts[0].ToLowerInvariant())
        {
            case "help":
                Reply("Commands: help, list, me, tell" +
                      (isOp ? ", kick, ban, op, tp, time, save-all, stop" : ""));
                break;

            case "list":
                Reply($"Connected players: {string.Join(", ", mPlayers.Select(p => p.Name))}");
                break;

            case "me" when parts.Length >= 2:
                Broadcast(PacketId.ChatMessage,
                    w => w.WriteString($"* {sender?.Name ?? "Server"} {string.Join(' ', parts[1..])}"));
                break;

            case "tell" when parts.Length >= 3:
            {
                var target = Find(parts[1]);
                if (target == null) { Reply("§cThere's no player by that name online."); break; }

                SendTo(target, PacketId.ChatMessage,
                    w => w.WriteString($"§7{sender?.Name ?? "Server"} whispers {string.Join(' ', parts[2..])}"));
                break;
            }

            case "kick" when isOp && parts.Length >= 2:
                Find(parts[1])?.Connection.Kick("You have been kicked from the game");
                Reply($"Kicked {parts[1]}");
                break;

            case "ban" when isOp && parts.Length >= 2:
                mProps.Ban(parts[1]);
                Find(parts[1])?.Connection.Kick("You are banned from this server!");
                Reply($"Banned {parts[1]}");
                break;

            case "op" when isOp && parts.Length >= 2:
                mProps.AddOp(parts[1]);
                if (Find(parts[1]) is { } opped) opped.IsOp = true;
                Reply($"Opped {parts[1]}");
                break;

            case "tp" when isOp && parts.Length >= 3:
            {
                var from = Find(parts[1]);
                var to = Find(parts[2]);
                if (from == null || to == null) { Reply("§cThere's no player by that name online."); break; }

                TeleportPlayer(from, to.Position);
                Reply($"Teleported {from.Name} to {to.Name}");
                break;
            }

            case "time" when isOp && parts.Length >= 2:
                mWorldTime = parts[1] switch
                {
                    "day" => 0,
                    "night" => 13000,
                    _ => long.TryParse(parts[1], out var t) ? t : mWorldTime,
                };
                Broadcast(PacketId.TimeUpdate, w => w.WriteLong(mWorldTime));
                break;

            case "save-all" when isOp:
                SaveEverything();
                Reply("Save complete.");
                break;

            case "stop" when isOp:
                Shutdown();
                break;

            default:
                Reply("§cUnknown command. Type \"help\" for help.");
                break;
        }
    }

    private ServerPlayer? Find(string name) =>
        mPlayers.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
