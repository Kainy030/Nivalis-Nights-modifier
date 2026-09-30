using BepInEx;
using NightsHack.HookRuntime;

namespace NightsHack.ItemHook;

[BepInPlugin(PluginId, "ItemHook", "0.1.0")]
[BepInProcess("Nivalis Nights.exe")]
public sealed class ItemHook : ObservationPlugin
{
    public const string PluginId = "nightshack.itemhook";
    public static ItemHook? Active => FindActive(PluginId) as ItemHook;
    protected override string Identifier => PluginId;
    protected override string CatalogResource => "ItemHook.Catalog.json";
    public static IBackpackItemAddition BackpackItems { get; } = new ReservedBackpackItemAddition();
}
