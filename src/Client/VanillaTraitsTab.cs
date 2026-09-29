using Newtonsoft.Json.Linq;
using Prosequor.Data;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace Prosequor.Client;

/// <summary>
/// Player opt-in for the vanilla character-dialog traits tab, and the hide that
/// leaves <c>composeTraitsTab</c> in <c>RenderTabHandlers</c> for other mods.
/// </summary>
public static class VanillaTraitsTab
{
    public const string ShowProperty = "ShowVanillaTraitsTab";

    /// <summary>
    /// Missing or unreadable values become false. Returns true when <paramref name="root"/> was changed.
    /// Other keys on <paramref name="root"/> are left in place.
    /// </summary>
    public static bool Normalize(JObject root, out bool show)
    {
        JToken? token = root[ShowProperty];
        if (token == null || token.Type == JTokenType.Null)
        {
            show = false;
            root[ShowProperty] = show;
            return true;
        }

        if (token.Type == JTokenType.Boolean)
        {
            show = token.Value<bool>();
            return false;
        }

        show = false;
        root[ShowProperty] = show;
        return true;
    }

    /// <summary>
    /// Reads <see cref="ShowProperty"/> from <c>ModConfig/prosequor/client.json</c>.
    /// Writes the key back without dropping sibling keys such as level-up volume.
    /// </summary>
    public static bool ReadShow(ICoreAPI api)
    {
        try
        {
            JsonObject? loaded = api.LoadModConfig(LevelUpAudio.ConfigFileName);
            if (loaded != null && loaded.Token is not JObject)
            {
                api.Logger.Warning(
                    "[prosequor] {0} is not a JSON object. Hiding the vanilla traits tab.",
                    LevelUpAudio.ConfigFileName);
                return false;
            }

            JObject root = loaded?.Token as JObject ?? new JObject();
            bool dirty = Normalize(root, out bool show);
            if (loaded == null || dirty)
            {
                api.StoreModConfig(new JsonObject(root), LevelUpAudio.ConfigFileName);
            }

            return show;
        }
        catch (Exception ex)
        {
            api.Logger.Warning(
                "[prosequor] Could not read {0}: {1}",
                LevelUpAudio.ConfigFileName,
                ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Removes the vanilla traits tab button when <paramref name="suppress"/> is true.
    /// The dialog's render handler for that tab stays, so another mod can still patch it.
    /// </summary>
    public static void HideIfSuppressed(ICoreClientAPI capi, bool suppress)
    {
        if (!suppress)
        {
            return;
        }

        GuiDialogCharacterBase? dlg =
            capi.Gui.LoadedGuis.Find(g => g is GuiDialogCharacterBase) as GuiDialogCharacterBase;
        if (dlg?.Tabs == null)
        {
            capi.Logger.Warning(
                "[prosequor] GuiDialogCharacterBase not found; vanilla traits tab left visible.");
            return;
        }

        string name = Lang.Get("charactertab-traits");
        int removed = dlg.Tabs.RemoveAll(tab =>
            tab != null && string.Equals(tab.Name, name, StringComparison.Ordinal));
        if (removed == 0)
        {
            capi.Logger.Debug("[prosequor] Vanilla traits tab was not on the character dialog.");
        }
    }

    public static bool ShouldSuppress(ICoreAPI api, OptionsRegistry options) =>
        OptionsRegistry.SuppressVanillaTraitsTab(ReadShow(api), options.TraitsTab);
}
