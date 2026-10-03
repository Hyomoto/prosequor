using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Prosequor.Client;
using Prosequor.Data;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>
/// Options files OR <c>traitsTab</c>. A false or an unmet <c>dependsOn</c> does not cancel a true.
/// The vanilla traits tab stays hidden unless the player or a contribution enables it.
/// </summary>
public static class OptionsFixtures
{
    public static void VerifyAll()
    {
        HashSet<string> loaded = new(StringComparer.Ordinal) { "seraphleveling" };
        OptionsRegistry registry = new();

        if (!registry.Consider(new OptionsFileJson { traitsTab = false }, loaded)
            || registry.TraitsTab)
        {
            Assert.Fail("[prosequor] Options fixture failed (false is not a request).");
        }

        OptionsFileJson? unmet = JsonConvert.DeserializeObject<OptionsFileJson>(
            """
            { "traitsTab": true, "dependsOn": [{ "modid": "seraphleveling", "invert": true }] }
            """);
        if (unmet == null
            || registry.Consider(unmet, loaded)
            || registry.TraitsTab)
        {
            Assert.Fail("[prosequor] Options fixture failed (unmet dependsOn must skip).");
        }

        if (!registry.Consider(new OptionsFileJson { traitsTab = true }, loaded)
            || !registry.TraitsTab)
        {
            Assert.Fail("[prosequor] Options fixture failed (true must request the tab).");
        }

        if (!registry.Consider(new OptionsFileJson { traitsTab = false }, loaded)
            || !registry.TraitsTab)
        {
            Assert.Fail("[prosequor] Options fixture failed (later false must not cancel true).");
        }

        if (!OptionsRegistry.SuppressVanillaTraitsTab(userEnabled: false, anyContributionEnabled: false))
        {
            Assert.Fail("[prosequor] Options fixture failed (default hides the tab).");
        }

        if (OptionsRegistry.SuppressVanillaTraitsTab(userEnabled: true, anyContributionEnabled: false)
            || OptionsRegistry.SuppressVanillaTraitsTab(userEnabled: false, anyContributionEnabled: true)
            || OptionsRegistry.SuppressVanillaTraitsTab(userEnabled: true, anyContributionEnabled: true))
        {
            Assert.Fail("[prosequor] Options fixture failed (user or contribution enable shows the tab).");
        }

        JObject client = new() { [LevelUpAudio.VolumeProperty] = 40 };
        if (!VanillaTraitsTab.Normalize(client, out bool show)
            || show
            || client.Value<bool>(VanillaTraitsTab.ShowProperty)
            || client.Value<int>(LevelUpAudio.VolumeProperty) != 40)
        {
            Assert.Fail("[prosequor] Options fixture failed (missing player flag defaults false and keeps volume).");
        }

        JObject enabled = new()
        {
            [VanillaTraitsTab.ShowProperty] = true,
            [LevelUpAudio.VolumeProperty] = 40
        };
        if (VanillaTraitsTab.Normalize(enabled, out bool shown)
            || !shown
            || enabled.Value<int>(LevelUpAudio.VolumeProperty) != 40)
        {
            Assert.Fail("[prosequor] Options fixture failed (player enable stays and keeps volume).");
        }

        if (!CharacterStatusTab.IncludeTraits(vanillaTabSuppressed: true)
            || CharacterStatusTab.IncludeTraits(vanillaTabSuppressed: false))
        {
            Assert.Fail("[prosequor] Options fixture failed (Status lists traits only while the vanilla tab is hidden).");
        }
    }
}
