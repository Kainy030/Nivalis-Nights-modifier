using BepInEx;
using NightsHack.HookRuntime;

namespace NightsHack.WorldHook;

[BepInPlugin(PluginId, "WorldHook", "0.1.0")]
[BepInProcess("Nivalis Nights.exe")]
public sealed class WorldHook : ObservationPlugin
{
    public const string PluginId = "nightshack.worldhook";
    public static WorldHook? Active => FindActive(PluginId) as WorldHook;
    protected override string Identifier => PluginId;
    protected override string CatalogResource => "WorldHook.Catalog.json";
}
