using Prosequor.Client;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>
/// Template-based hover line stripping: values are wildcards, static infixes matter.
/// </summary>
public static class HoverStatLineStripperFixtures
{
    public static void VerifyAll()
    {
        VerifyCultureDecimalAttackPower();
        VerifyDurabilityInfixRequired();
        VerifyInterleavedMiningSpeedKept();
        VerifyBowPiercingSuffix();
        VerifyVtmlWrappedDurability();
        VerifyDurabilityOnlyDoesNotStealAttack();
        VerifyArmorCultureFlatReduction();
        VerifyArmorKeepsHealingBetweenStolenLines();
        VerifyClothingCategoryUnknownExact();
        VerifyMeleeTemplatesDoNotStealProtectionTier();
        VerifyClothingConditionVtmlAndWarmth();
        VerifyClothingMaxWarmthTemplate();
        VerifyClothingStripKeepsFlavorAndHealing();
        VerifyMeleeTemplatesDoNotStealMaxWarmth();
        MatchesTemplateHelpers();
    }

    static void VerifyCultureDecimalAttackPower()
    {
        string desc =
            "Прочность: 400 / 400\n" +
            "Уровень инструмента: 3\n" +
            "Скорость добычи: Почва 8,9x\n" +
            "Сила атаки: 1,8 урона\n" +
            "Уровень атаки: 3";

        string[] meleeTemplates =
        [
            "Прочность: {0} / {1}",
            "Уровень инструмента: {0}",
            "Сила атаки: {0} урона",
            "Уровень атаки: {0}",
            "Дальность атаки: {0} м"
        ];

        string got = HoverStatLineStripper.Strip(desc, meleeTemplates);
        if (got != "Скорость добычи: Почва 8,9x")
        {
            Assert.Fail(
                "[prosequor] Culture decimal attack power should strip; mining speed kept. got=" + got);
        }
    }

    static void VerifyDurabilityInfixRequired()
    {
        if (!HoverStatLineStripper.MatchesTemplate("Durability: 12 / 40", "Durability: {0} / {1}"))
        {
            Assert.Fail("[prosequor] Durability with slash infix should match.");
        }

        if (HoverStatLineStripper.MatchesTemplate("Durability: infinite", "Durability: {0} / {1}"))
        {
            Assert.Fail("[prosequor] Durability without slash infix must not match.");
        }
    }

    static void VerifyInterleavedMiningSpeedKept()
    {
        string desc =
            "Durability: 100 / 100\n" +
            "Tool Tier: 2\n" +
            "Mining Speed: Soil 5x, Sand 4x\n" +
            "Attack power: 1.5 damage\n" +
            "Attack tier: 2\n" +
            "Attack range: 2.5 m";

        string[] meleeTemplates =
        [
            "Durability: {0} / {1}",
            "Tool Tier: {0}",
            "Attack power: {0} damage",
            "Attack power: -{0} hp",
            "Attack tier: {0}",
            "Attack range: {0} m"
        ];

        string got = HoverStatLineStripper.Strip(desc, meleeTemplates);
        if (got != "Mining Speed: Soil 5x, Sand 4x")
        {
            Assert.Fail(
                "[prosequor] Mining speed between tier and attack must be kept. got=" + got);
        }
    }

    static void VerifyBowPiercingSuffix()
    {
        if (!HoverStatLineStripper.MatchesTemplate("12 piercing damage", "{0} piercing damage"))
        {
            Assert.Fail("[prosequor] Placeholder-first bow piercing template should match.");
        }

        string desc =
            "Durability: 80 / 80\n" +
            "12 piercing damage\n" +
            "+20% accuracy\n" +
            "Some lore line";

        string[] bowTemplates =
        [
            "Durability: {0} / {1}",
            "{0} piercing damage",
            "{0}{1}% accuracy"
        ];

        string got = HoverStatLineStripper.Strip(desc, bowTemplates);
        if (got != "Some lore line")
        {
            Assert.Fail("[prosequor] Bow templates should strip piercing/accuracy. got=" + got);
        }
    }

    static void VerifyVtmlWrappedDurability()
    {
        string desc = "<font color=\"#bbbbbb\">Durability: 12 / 40</font>\nMining Speed: Soil 5x";
        string[] templates = ["Durability: {0} / {1}"];
        string got = HoverStatLineStripper.Strip(desc, templates);
        if (got != "Mining Speed: Soil 5x")
        {
            Assert.Fail("[prosequor] VTML-wrapped durability should still match. got=" + got);
        }
    }

    static void VerifyDurabilityOnlyDoesNotStealAttack()
    {
        string desc =
            "Durability: 50 / 50\n" +
            "Attack power: 1.8 damage\n" +
            "Flavor text";

        // Durability-only layout only steals the durability template.
        string[] durabilityOnly = ["Durability: {0} / {1}"];
        string got = HoverStatLineStripper.Strip(desc, durabilityOnly);
        if (got != "Attack power: 1.8 damage\nFlavor text")
        {
            Assert.Fail(
                "[prosequor] Durability-only must not steal attack power. got=" + got);
        }
    }

    static void VerifyArmorCultureFlatReduction()
    {
        if (!HoverStatLineStripper.MatchesTemplate(
                "Flat damage reduction: 1,7 hp",
                "Flat damage reduction: {0} hp"))
        {
            Assert.Fail("[prosequor] Culture decimal flat reduction should match template.");
        }

        if (!HoverStatLineStripper.MatchesTemplate("Percent protection: 96%", "Percent protection: {0}%"))
        {
            Assert.Fail("[prosequor] Percent protection should match.");
        }

        if (!HoverStatLineStripper.MatchesTemplate("Protection tier: 3", "Protection tier: {0}"))
        {
            Assert.Fail("[prosequor] Protection tier should match.");
        }

        if (!HoverStatLineStripper.MatchesTemplate(
                "Clothing Category: Head Armor",
                "Clothing Category: {0}"))
        {
            Assert.Fail("[prosequor] Clothing Category with value should match.");
        }
    }

    static void VerifyArmorKeepsHealingBetweenStolenLines()
    {
        string desc =
            "Durability: 1600 / 1600\n" +
            "Clothing Category: Head Armor\n" +
            "Flat damage reduction: 1.7 hp\n" +
            "Percent protection: 96%\n" +
            "Protection tier: 3\n" +
            "\n" +
            "Healing effectiveness: -17%\n" +
            "Hunger rate: +16%\n" +
            "Walk speed: -8%";

        string[] armorTemplates =
        [
            "Durability: {0} / {1}",
            "Clothing Category: {0}",
            "Clothing Category: Unknown",
            "Flat damage reduction: {0} hp",
            "Percent protection: {0}%",
            "Protection tier: {0}"
        ];

        string got = HoverStatLineStripper.Strip(desc, armorTemplates);
        string expected =
            "Healing effectiveness: -17%\n" +
            "Hunger rate: +16%\n" +
            "Walk speed: -8%";
        if (got != expected)
        {
            Assert.Fail(
                "[prosequor] Armor strip should keep healing/hunger/walk. got=" + got);
        }
    }

    static void VerifyClothingCategoryUnknownExact()
    {
        if (!HoverStatLineStripper.MatchesTemplate(
                "Clothing Category: Unknown",
                "Clothing Category: Unknown"))
        {
            Assert.Fail("[prosequor] Exact Clothing Category: Unknown should match.");
        }

        // Value form must not swallow an unrelated "Unknown" only when using the exact key —
        // the placeholder form still matches "Clothing Category: Unknown" because {0}=Unknown.
        if (!HoverStatLineStripper.MatchesTemplate(
                "Clothing Category: Unknown",
                "Clothing Category: {0}"))
        {
            Assert.Fail("[prosequor] Placeholder form should also match Unknown.");
        }
    }

    static void VerifyMeleeTemplatesDoNotStealProtectionTier()
    {
        string desc =
            "Durability: 100 / 100\n" +
            "Protection tier: 3\n" +
            "Attack power: 1.5 damage";

        string[] meleeTemplates =
        [
            "Durability: {0} / {1}",
            "Tool Tier: {0}",
            "Attack power: {0} damage",
            "Attack power: -{0} hp",
            "Attack tier: {0}",
            "Attack range: {0} m"
        ];

        string got = HoverStatLineStripper.Strip(desc, meleeTemplates);
        if (got != "Protection tier: 3")
        {
            Assert.Fail(
                "[prosequor] Melee templates must not steal Protection tier. got=" + got);
        }
    }

    static void VerifyClothingConditionVtmlAndWarmth()
    {
        // Vanilla: Condition: <font>…</font>, <font>+0.7°C</font>
        string line =
            "Condition: <font color=\"#ff4040\">16%</font>, <font color=\"#ff8484\">+0.7°C</font>";
        if (!HoverStatLineStripper.MatchesTemplate(line, "Condition: {0}"))
        {
            Assert.Fail("[prosequor] Condition line with VTML + warmth should match prefix template.");
        }

        string desc =
            "Clothing Category: Upper Body\n" +
            line + "\n" +
            "Max warmth: 2°C\n" +
            "A soft linen shirt.";

        string[] clothingTemplates =
        [
            "Clothing Category: {0}",
            "Clothing Category: Unknown",
            "Condition: {0}",
            "Max warmth: {0}°C"
        ];

        string got = HoverStatLineStripper.Strip(desc, clothingTemplates);
        if (got != "A soft linen shirt.")
        {
            Assert.Fail(
                "[prosequor] Clothing strip should drop condition/category/max warmth. got=" + got);
        }
    }

    static void VerifyClothingMaxWarmthTemplate()
    {
        // clothing-maxwarmth unformatted collapses {0:0.#} → {0} for matching.
        if (!HoverStatLineStripper.MatchesTemplate("Max warmth: 2°C", "Max warmth: {0}°C"))
        {
            Assert.Fail("[prosequor] Max warmth: 2°C should match clothing-maxwarmth shape.");
        }

        if (!HoverStatLineStripper.MatchesTemplate("Max warmth: 1.5°C", "Max warmth: {0}°C"))
        {
            Assert.Fail("[prosequor] Max warmth with decimal should match.");
        }
    }

    static void VerifyClothingStripKeepsFlavorAndHealing()
    {
        string desc =
            "Clothing Category: Upper Body\n" +
            "Condition: 100%, +2°C\n" +
            "Max warmth: 2°C\n" +
            "\n" +
            "Healing effectiveness: -5%\n" +
            "A traveler's favorite.";

        string[] clothingTemplates =
        [
            "Clothing Category: {0}",
            "Clothing Category: Unknown",
            "Condition: {0}",
            "Max warmth: {0}°C"
        ];

        string got = HoverStatLineStripper.Strip(desc, clothingTemplates);
        string expected =
            "Healing effectiveness: -5%\n" +
            "A traveler's favorite.";
        if (got != expected)
        {
            Assert.Fail(
                "[prosequor] Clothing strip should keep healing and flavor. got=" + got);
        }

        if (!HoverStatLineStripper.MatchesTemplate(
                "Clothing Category: Upper Body",
                "Clothing Category: {0}"))
        {
            Assert.Fail("[prosequor] Clothing Category: Upper Body should match.");
        }
    }

    static void VerifyMeleeTemplatesDoNotStealMaxWarmth()
    {
        string desc =
            "Durability: 50 / 50\n" +
            "Max warmth: 2°C\n" +
            "Attack power: 1.8 damage";

        string[] meleeTemplates =
        [
            "Durability: {0} / {1}",
            "Tool Tier: {0}",
            "Attack power: {0} damage",
            "Attack power: -{0} hp",
            "Attack tier: {0}",
            "Attack range: {0} m"
        ];

        string got = HoverStatLineStripper.Strip(desc, meleeTemplates);
        if (got != "Max warmth: 2°C")
        {
            Assert.Fail(
                "[prosequor] Melee templates must not steal Max warmth. got=" + got);
        }
    }

    static void MatchesTemplateHelpers()
    {
        if (HoverStatLineStripper.MatchesTemplate(null, "Durability: {0} / {1}"))
        {
            Assert.Fail("[prosequor] Null line must not match.");
        }

        if (HoverStatLineStripper.MatchesTemplate("Durability: 1 / 1", null))
        {
            Assert.Fail("[prosequor] Null template must not match.");
        }

        string empty = HoverStatLineStripper.Strip(null, ["Durability: {0} / {1}"]);
        if (empty != "")
        {
            Assert.Fail("[prosequor] Strip(null) should return empty string.");
        }
    }
}
