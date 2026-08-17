using ImGuiNET;
using VoxelEngine.GameEntity;

namespace VoxelEngine.UI;

public static class NameTagRenderer
{
    private const float MAX_DISTANCE = 64;
    private const float HEAD_HEIGHT = 2.1f;

    public static void Render(IEnumerable<RemotePlayerEntity> players, Camera camera, Vector2 screenSize)
    {
        var viewProj = camera.GetViewMatrix() * camera.GetProjectionMatrix();
        var drawList = ImGui.GetBackgroundDrawList();

        foreach (var p in players)
        {
            float distance = (p.Position - camera.Position).Length();
            if (distance > MAX_DISTANCE)
                continue;

            var worldPos = new Vector4(p.Position.X, p.Position.Y + HEAD_HEIGHT, p.Position.Z, 1f);
            var clip = Vector4.Transform(worldPos, viewProj);

            if (clip.W <= .01f)
                continue;

            var ndc = new Vector2(clip.X / clip.W, clip.Y / clip.W);
            var screen = new Vector2((ndc.X * .5f + .5f) * screenSize.X,
                (1f - (ndc.Y * .5f + .5f)) * screenSize.Y);

            var textSize = ImGui.CalcTextSize(p.Name);
            var topleft = new Vector2(screen.X - textSize.X * .5f, screen.Y - textSize.Y * .5f);

            float alpha = Math.Clamp(1f - distance / MAX_DISTANCE, .25f, 1f);

            drawList.AddRectFilled(topleft - new Vector2(3f, 2f),
                topleft + textSize + new Vector2(3f, 2f),
                ImGui.GetColorU32(new Vector4(0f, 0f, 0f, .35f * alpha)), 2f);
            drawList.AddText(topleft, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alpha)), p.Name);
        }
    }
}