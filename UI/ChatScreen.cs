// In-game chat: history that fades when idle, and an input line. | Stage 8

using System.Text;
using ImGuiNET;

namespace VoxelEngine.UI;

/// <summary>
/// Beta's chat: T opens the input line, / opens it pre-filled with a slash, Esc cancels, Enter sends.
/// History fades out after a few seconds while the input is closed.
/// </summary>
public sealed class ChatScreen
{
    private const int MAX_HISTORY = 100;
    private const int VISIBLE_LINES_CLOSED = 10;
    private const float FADE_AFTER_SECONDS = 10f;
    private const float FADE_DURATION = 2f;

    public bool IsOpen { get; private set; }
    public event Action<string>? OnSendMessage;

    private readonly List<(string text, float receivedAt)> mHistory = new();
    private readonly byte[] mInputBuffer = new byte[128];
    private float mNow;

    public void AddMessage(string text)
    {
        mHistory.Add((text, mNow));
        if (mHistory.Count > MAX_HISTORY)
            mHistory.RemoveAt(0);
    }

    public void Open(bool withSlash)
    {
        IsOpen = true;
        Array.Clear(mInputBuffer);
        if (withSlash)
            mInputBuffer[0] = (byte)'/';
    }

    public void Close()
    {
        IsOpen = false;
        Array.Clear(mInputBuffer);
    }

    public void Clear()
    {
        mHistory.Clear();
        Close();
    }

    public void Render(float deltaTime)
    {
        mNow += deltaTime;

        var io = ImGui.GetIO();
        float chatWidth = io.DisplaySize.X * 0.5f;
        float lineHeight = ImGui.GetTextLineHeightWithSpacing();
        float height = lineHeight * (VISIBLE_LINES_CLOSED + 1);
        float bottom = io.DisplaySize.Y - 60f;

        ImGui.SetNextWindowPos(new Vector2(10f, bottom - height));
        ImGui.SetNextWindowSize(new Vector2(chatWidth, height));

        // NoInputs while closed, or the invisible window would eat clicks meant for the world.
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
                    ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBackground |
                    ImGuiWindowFlags.NoSavedSettings |
                    (IsOpen ? ImGuiWindowFlags.None : ImGuiWindowFlags.NoInputs);

        ImGui.Begin("Chat", flags);

        DrawHistory();

        if (IsOpen)
        {
            ImGui.SetNextItemWidth(chatWidth - 20f);
            if (!ImGui.IsAnyItemActive())
                ImGui.SetKeyboardFocusHere();

            if (ImGui.InputText("##chatinput", mInputBuffer, (uint)mInputBuffer.Length,
                                ImGuiInputTextFlags.EnterReturnsTrue))
            {
                string text = ReadBuffer(mInputBuffer).Trim();
                if (text.Length > 0)
                    OnSendMessage?.Invoke(text);

                Close();
            }

            if (ImGui.IsKeyPressed(ImGuiKey.Escape))
                Close();
        }

        ImGui.End();
    }

    // Newest at the bottom, so walk backwards and collect before drawing.
    private void DrawHistory()
    {
        int limit = IsOpen ? MAX_HISTORY : VISIBLE_LINES_CLOSED;
        var visible = new List<(string text, float alpha)>();

        for (int i = mHistory.Count - 1; i >= 0 && visible.Count < limit; i--)
        {
            float age = mNow - mHistory[i].receivedAt;

            if (!IsOpen && age > FADE_AFTER_SECONDS + FADE_DURATION)
                break;                          // older entries are older still; stop scanning

            float alpha = IsOpen
                ? 1f
                : Math.Clamp(1f - (age - FADE_AFTER_SECONDS) / FADE_DURATION, 0f, 1f);

            visible.Add((mHistory[i].text, alpha));
        }

        for (int i = visible.Count - 1; i >= 0; i--)
            DrawColorCodedLine(visible[i].text, visible[i].alpha);
    }

    /// Beta used §0-§f colour codes inline. Split on them and colour each run.
    private static void DrawColorCodedLine(string line, float alpha)
    {
        var colour = new Vector4(1f, 1f, 1f, alpha);
        bool first = true;
        int i = 0;

        while (i < line.Length)
        {
            int next = line.IndexOf('§', i);

            // No more codes: the rest of the line is one run.
            if (next < 0)
            {
                Segment(line[i..], colour, ref first);
                break;
            }

            if (next > i)
                Segment(line[i..next], colour, ref first);

            // A trailing § with no code after it - nothing left to colour.
            if (next + 1 >= line.Length)
                break;

            colour = CodeToColour(line[next + 1], alpha);
            i = next + 2;
        }

        // A line that was nothing but colour codes still has to advance the cursor.
        if (first)
            ImGui.TextUnformatted(" ");
    }

    private static void Segment(string text, Vector4 colour, ref bool first)
    {
        if (!first)
            ImGui.SameLine(0f, 0f);

        ImGui.TextColored(colour, text);
        first = false;
    }

    // Minecraft's 16-colour palette, close enough for chat.
    private static Vector4 CodeToColour(char code, float alpha) => char.ToLowerInvariant(code) switch
    {
        '0' => new Vector4(0.00f, 0.00f, 0.00f, alpha),
        '1' => new Vector4(0.00f, 0.00f, 0.67f, alpha),
        '2' => new Vector4(0.00f, 0.67f, 0.00f, alpha),
        '3' => new Vector4(0.00f, 0.67f, 0.67f, alpha),
        '4' => new Vector4(0.67f, 0.00f, 0.00f, alpha),
        '5' => new Vector4(0.67f, 0.00f, 0.67f, alpha),
        '6' => new Vector4(1.00f, 0.67f, 0.00f, alpha),
        '7' => new Vector4(0.67f, 0.67f, 0.67f, alpha),
        '8' => new Vector4(0.33f, 0.33f, 0.33f, alpha),
        '9' => new Vector4(0.33f, 0.33f, 1.00f, alpha),
        'a' => new Vector4(0.33f, 1.00f, 0.33f, alpha),
        'b' => new Vector4(0.33f, 1.00f, 1.00f, alpha),
        'c' => new Vector4(1.00f, 0.33f, 0.33f, alpha),
        'd' => new Vector4(1.00f, 0.33f, 1.00f, alpha),
        'e' => new Vector4(1.00f, 1.00f, 0.33f, alpha),
        _ => new Vector4(1.00f, 1.00f, 1.00f, alpha),
    };

    // ImGui writes a NUL-terminated UTF-8 buffer; take everything before the first NUL.
    private static string ReadBuffer(byte[] buffer)
    {
        int length = Array.IndexOf(buffer, (byte)0);
        if (length < 0) length = buffer.Length;
        return Encoding.UTF8.GetString(buffer, 0, length);
    }

    public void OnClose() => Close();
}
