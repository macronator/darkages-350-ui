using System.Text.Json;
using System.Text.Json.Serialization;

namespace DarkAges350;

public sealed class Rect
{
    [JsonPropertyName("left")] public int Left { get; set; }
    [JsonPropertyName("top")] public int Top { get; set; }
    [JsonPropertyName("right")] public int Right { get; set; }
    [JsonPropertyName("bottom")] public int Bottom { get; set; }
    public int W => Right - Left;
    public int H => Bottom - Top;
    public override string ToString() => $"({Left},{Top},{Right},{Bottom}) {W}x{H}";
}

public sealed class SpaceInfo
{
    [JsonPropertyName("width")] public int Width { get; set; } = 640;
    [JsonPropertyName("height")] public int Height { get; set; } = 480;
}

public sealed class Constants
{
    [JsonPropertyName("background_asset")] public string BackgroundAsset { get; set; } = "Backgrnd.epf";
    [JsonPropertyName("orb_hp_asset")] public string OrbHpAsset { get; set; } = "orb001.epf";
    [JsonPropertyName("orb_mp_asset")] public string OrbMpAsset { get; set; } = "orb002.epf";
    [JsonPropertyName("orb_hp_rect")] public Rect? OrbHpRect { get; set; }
    [JsonPropertyName("orb_mp_rect")] public Rect? OrbMpRect { get; set; }
    [JsonPropertyName("minimap_rect")] public Rect? MinimapRect { get; set; }
    [JsonPropertyName("chat_panel_rect")] public Rect? ChatPanelRect { get; set; }
}

public sealed class UiSpec
{
    [JsonPropertyName("coordinate_space")] public SpaceInfo Space { get; set; } = new();
    [JsonPropertyName("constants")] public Constants Constants { get; set; } = new();
    [JsonPropertyName("palettes")] public Dictionary<string, string> Palettes { get; set; } = new();

    public string DefaultUiPalette =>
        Palettes.TryGetValue("_default_ui", out var p) ? p : "legend.pal";

    public static UiSpec Load(string path)
    {
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<UiSpec>(File.ReadAllText(path), opts)
               ?? throw new InvalidDataException("could not parse UI spec");
    }
}
