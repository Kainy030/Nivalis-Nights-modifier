using BepInEx;
using NightsHack.HookRuntime;

namespace NightsHack.PlayerHook;

[BepInPlugin(PluginId, "PlayerHook", "0.1.0")]
[BepInProcess("Nivalis Nights.exe")]
public sealed class PlayerHook : ObservationPlugin
{
    public const string PluginId = "nightshack.playerhook";
    public static PlayerHook? Active => FindActive(PluginId) as PlayerHook;
    protected override string Identifier => PluginId;
    protected override string CatalogResource => "PlayerHook.Catalog.json";
    protected override bool CapturePlayerDetails => true;
}
