using System.Reflection;
using System.Runtime.CompilerServices;
using Cairo;
using HarmonyLib;
using Prosequor.Client;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Replaces the Class tab layout of <see cref="GuiDialogCreateCharacter"/> while leaving Skin &amp; Voice vanilla.
/// </summary>
[HarmonyPatch(typeof(GuiDialogCreateCharacter))]
public static class CreateCharacterClassPatches
{
    static readonly ConditionalWeakTable<GuiDialogCreateCharacter, CreateCharacterClassTab> Tabs = new();

    static readonly MethodInfo ChangeClassMethod =
        AccessTools.Method(typeof(GuiDialogCreateCharacter), "changeClass", [typeof(int)])
        ?? throw new MissingMethodException(nameof(GuiDialogCreateCharacter), "changeClass(int)");

    static readonly MethodInfo OnTabClickedMethod =
        AccessTools.Method(typeof(GuiDialogCreateCharacter), "onTabClicked", [typeof(int)])
        ?? throw new MissingMethodException(nameof(GuiDialogCreateCharacter), "onTabClicked(int)");

    static readonly MethodInfo OnConfirmMethod =
        AccessTools.Method(typeof(GuiDialogCreateCharacter), "OnConfirm")
        ?? throw new MissingMethodException(nameof(GuiDialogCreateCharacter), "OnConfirm");

    static readonly MethodInfo OnTitleBarCloseMethod =
        AccessTools.Method(typeof(GuiDialogCreateCharacter), "OnTitleBarClose")
        ?? throw new MissingMethodException(nameof(GuiDialogCreateCharacter), "OnTitleBarClose");

    static readonly MethodInfo ReTesselateMethod =
        AccessTools.Method(typeof(GuiDialogCreateCharacter), "reTesselate")
        ?? throw new MissingMethodException(nameof(GuiDialogCreateCharacter), "reTesselate");

    [HarmonyPrefix]
    [HarmonyPatch("ComposeGuis")]
    public static bool ComposeGuisPrefix(GuiDialogCreateCharacter __instance)
    {
        Traverse t = Traverse.Create(__instance);
        int curTab = t.Field<int>("curTab").Value;
        if (curTab != 1)
        {
            return true;
        }

        ComposeClassTab(__instance, t);
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch("changeClass")]
    public static bool ChangeClassPrefix(GuiDialogCreateCharacter __instance, int dir)
    {
        if (!Tabs.TryGetValue(__instance, out CreateCharacterClassTab? tab) || tab == null)
        {
            return true;
        }

        if (!__instance.Composers.ContainsKey("createcharacter"))
        {
            return true;
        }

        GuiComposer composer = __instance.Composers["createcharacter"];
        // Skin tab recomposes without our keys — leave vanilla changeClass alone.
        if (composer.GetDynamicText(CreateCharacterClassTab.ClassNameKey) == null)
        {
            return true;
        }

        Traverse t = Traverse.Create(__instance);
        CharacterSystem? modSys = t.Field<CharacterSystem>("modSys").Value;
        if (modSys?.characterClasses == null || modSys.characterClasses.Count == 0)
        {
            return true;
        }

        int index = GameMath.Mod(
            t.Field<int>("currentClassIndex").Value + dir,
            modSys.characterClasses.Count);
        t.Field<int>("currentClassIndex").Value = index;

        CharacterClass characterClass = modSys.characterClasses[index];
        tab.Refresh(composer, characterClass, modSys);
        modSys.setCharacterClass(PlayerEntity(__instance), characterClass.Code);
        ReTesselateMethod.Invoke(__instance, null);
        return false;
    }

    static EntityPlayer PlayerEntity(GuiDialogCreateCharacter dialog)
    {
        ICoreClientAPI capi = Traverse.Create((GuiDialog)dialog).Field<ICoreClientAPI>("capi").Value;
        return capi.World.Player.Entity;
    }

    static void ComposeClassTab(GuiDialogCreateCharacter dialog, Traverse t)
    {
        ICoreClientAPI capi = Traverse.Create((GuiDialog)dialog).Field<ICoreClientAPI>("capi").Value;
        int dlgHeight = t.Field<int>("dlgHeight").Value;
        bool allowClassSelection = t.Property("AllowClassSelection").GetValue<bool>();

        EntityBehaviorPlayerInventory? invBehavior =
            capi.World.Player.Entity.GetBehavior<EntityBehaviorPlayerInventory>();
        if (invBehavior != null)
        {
            invBehavior.hideClothing = false;
        }

        if (capi.World.Player.Entity.Properties.Client.Renderer is EntityShapeRenderer esr)
        {
            esr.TesselateShape();
        }

        double unscaledSlotPadding = GuiElementItemSlotGridBase.unscaledSlotPadding;
        ElementBounds tabBounds = ElementBounds.Fixed(0, -25, 450, 25);
        ElementBounds bgBounds = ElementBounds
            .FixedSize(717, dlgHeight)
            .WithFixedPadding(GuiStyle.ElementToDialogPadding);
        ElementBounds dialogBounds = ElementBounds
            .FixedSize(757, dlgHeight + 40)
            .WithAlignment(EnumDialogArea.LeftMiddle)
            .WithFixedAlignmentOffset(GuiStyle.DialogToScreenPadding, 0);

        GuiTab[] tabs =
        [
            new GuiTab
            {
                Name = Lang.Get("tab-skinandvoice"),
                DataInt = 0
            },
            new GuiTab
            {
                Name = Lang.Get("tab-charclass"),
                DataInt = 1
            }
        ];

        GuiComposer composer = capi.Gui
            .CreateCompo("createcharacter", dialogBounds)
            .AddShadedDialogBG(bgBounds, true, 5, 0.75f)
            .AddDialogTitleBar(
                Lang.Get("Select character class"),
                () => OnTitleBarCloseMethod.Invoke(dialog, null))
            .AddIf(allowClassSelection)
            .AddHorizontalTabs(
                tabs,
                tabBounds,
                id => OnTabClickedMethod.Invoke(dialog, [id]),
                CairoFont.WhiteSmallText().WithWeight(FontWeight.Bold),
                CairoFont.WhiteSmallText().WithWeight(FontWeight.Bold),
                "tabs")
            .EndIf()
            .BeginChildElements(bgBounds);

        dialog.Composers["createcharacter"] = composer;

        double top = 20 + unscaledSlotPadding - 10;
        ElementBounds leftPad = ElementBounds
            .Fixed(0, top, 0, dlgHeight - 47)
            .FixedGrow(2 * unscaledSlotPadding, 2 * unscaledSlotPadding);
        ElementBounds insetSlotBounds = ElementBounds
            .Fixed(0, top + 25, 190, leftPad.fixedHeight - 2 * unscaledSlotPadding + 10)
            .FixedRightOf(leftPad, 10);
        t.Field<ElementBounds>("insetSlotBounds").Value = insetSlotBounds;

        composer.AddInset(insetSlotBounds, 2, 0.85f);

        double rightX = insetSlotBounds.fixedX + insetSlotBounds.fixedWidth + 20;
        double rightW = 480;
        double availableH = dlgHeight - 47 - 40;

        CreateCharacterClassTab tab = Tabs.GetValue(dialog, _ => new CreateCharacterClassTab(capi));

        tab.Compose(
            composer,
            rightX,
            top + 25,
            rightW,
            availableH,
            () => ChangeClassMethod.Invoke(dialog, [-1]),
            () => ChangeClassMethod.Invoke(dialog, [1]));

        composer.AddSmallButton(
            Lang.Get("Confirm Class"),
            () => (bool)(OnConfirmMethod.Invoke(dialog, null) ?? false),
            ElementBounds.Fixed(0, dlgHeight - 25)
                .WithAlignment(EnumDialogArea.RightFixed)
                .WithFixedPadding(12, 6),
            EnumButtonStyle.Normal);

        GuiElementHorizontalTabs? horizontalTabs = composer.GetHorizontalTabs("tabs");
        if (horizontalTabs != null)
        {
            horizontalTabs.unscaledTabSpacing = 20;
            horizontalTabs.unscaledTabPadding = 10;
            horizontalTabs.activeElement = 1;
        }

        composer.EndChildElements();
        composer.Compose();

        // Populate texts / apply class after composer keys exist (goes through our Prefix).
        ChangeClassMethod.Invoke(dialog, [0]);
    }
}
