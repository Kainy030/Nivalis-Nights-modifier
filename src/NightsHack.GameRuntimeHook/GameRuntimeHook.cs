using BepInEx;
using NightsHack.HookRuntime;

namespace NightsHack.GameRuntimeHook;

[BepInPlugin(PluginId, "GameRuntimeHook", "0.1.0")]
[BepInProcess("Nivalis Nights.exe")]
public sealed class GameRuntimeHook : ObservationPlugin
{
    public const string PluginId = "nightshack.gameruntimehook";
    public static GameRuntimeHook? Active => FindActive(PluginId) as GameRuntimeHook;
    protected override string Identifier => PluginId;
    protected override string CatalogResource => "GameRuntimeHook.Catalog.json";
}
