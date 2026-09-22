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
