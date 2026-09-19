using System.Reflection;
using System.Text;
using HarmonyLib;
using Prosequor.Client;
using Prosequor.Data;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Optional hooks for Player Model Lib: its <c>GuiDialogCreateCustomCharacter</c> hides
/// vanilla class compose, and model extra-traits land after the class-selection packet.
/// </summary>
public static class PlayerModelLibCompat
{
    public const string ModId = "playermodellib";
    const string ExtraSkinnableBehavior = "PlayerModelLib:ExtraSkinnable";
    const string CustomModelsSystemTypeName = "PlayerModelLib.CustomModelsSystem";
    const string CreateDialogTypeName = "PlayerModelLib.GuiDialogCreateCustomCharacter";

    public static bool IsLoaded(ICoreAPI? api) =>
        api?.ModLoader?.IsModEnabled(ModId) == true;

    public static void TryPatch(Harmony harmony)
    {
        if (harmony == null)
        {
            return;
        }

        Type? modelsSystem = AccessTools.TypeByName(CustomModelsSystemTypeName);
        MethodInfo? onModelPacket = modelsSystem == null
            ? null
            : AccessTools.Method(modelsSystem, "HandleChangePlayerModelPacket");
        if (onModelPacket != null)
        {
            harmony.Patch(
                onModelPacket,
                postfix: new HarmonyMethod(
                    typeof(PlayerModelLibCompat),
                    nameof(HandleChangePlayerModelPacketPostfix)));
        }

        Type? dialog = AccessTools.TypeByName(CreateDialogTypeName);
        MethodInfo? changeClass = dialog == null
            ? null
            : AccessTools.Method(dialog, "ChangeClass", [typeof(int)]);
        if (changeClass != null)
        {
            harmony.Patch(
                changeClass,
                postfix: new HarmonyMethod(
                    typeof(PlayerModelLibCompat),
                    nameof(ChangeClassPostfix)));
        }
    }

    public static void HandleChangePlayerModelPacketPostfix(IPlayer player)
    {
        if (player is not IServerPlayer server || player.Entity?.Api == null)
        {
            return;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(player.Entity.Api);
        if (mod?.TraitAttributes == null)
        {
            return;
        }

        CharacterSystem? characters = player.Entity.Api.ModLoader.GetModSystem<CharacterSystem>();
        if (characters == null)
        {
            return;
        }

        TraitAttributeConverter.TryApplyOnSelection(
            server,
            characters,
            mod.TraitAttributes,
            mod.Registry);
        TraitAttributeConverter.FoldNewExtraTraits(server, mod.TraitAttributes, mod.Registry);
    }

    public static void ChangeClassPostfix(object __instance)
    {
        if (__instance is not GuiDialog dialog)
        {
            return;
        }

        ICoreClientAPI? capi = Traverse.Create((GuiDialog)dialog).Field<ICoreClientAPI>("capi").Value;
        if (capi == null || !dialog.Composers.ContainsKey("createcharacter"))
        {
            return;
        }

        GuiComposer composer = dialog.Composers["createcharacter"];
        GuiElementRichtext? body = composer.GetRichtext("characterDesc");
        if (body == null)
        {
            return;
        }

        CharacterSystem? characters = capi.ModLoader.GetModSystem<CharacterSystem>();
        EntityPlayer entity = capi.World.Player.Entity;
        string? classCode = entity.WatchedAttributes.GetString("characterClass");
        if (characters == null
            || string.IsNullOrWhiteSpace(classCode)
            || !characters.characterClassesByCode.TryGetValue(classCode, out CharacterClass? cls)
            || cls == null)
        {
            return;
        }

        string[] extra = ReadModelExtraTraits(capi, entity);
        body.SetNewText(
            BuildClassPreviewHtml(cls, characters, capi, extra),
            CairoFont.WhiteDetailText());

        GuiElementScrollbar? scrollbar = composer.GetScrollbar("scrollbar");
        if (scrollbar == null)
        {
            return;
        }

        float clipH = 0f;
        Traverse clipProp = Traverse.Create(__instance).Property("ClipHeight");
        if (clipProp.PropertyExists())
        {
            clipH = clipProp.GetValue<float>();
        }
        if (clipH < 1f)
        {
            scrollbar.Bounds.CalcWorldBounds();
            clipH = (float)scrollbar.Bounds.fixedHeight;
        }

        double contentH = body.TotalHeight / RuntimeEnv.GUIScale;
        scrollbar.Bounds.CalcWorldBounds();
        scrollbar.SetHeights(clipH, (float)Math.Max(clipH, contentH));
        scrollbar.CurrentYPosition = 0;
    }

    public static string[] ReadModelExtraTraits(ICoreAPI api, Entity entity)
    {
        EntityBehavior? skin = FindSkinBehavior(entity);
        if (skin == null)
        {
            return [];
        }

        string? modelCode = Traverse.Create(skin).Property("CurrentModelCode").GetValue<string>();
        if (string.IsNullOrWhiteSpace(modelCode))
        {
            return [];
        }

        Type? systemType = AccessTools.TypeByName(CustomModelsSystemTypeName);
        if (systemType == null)
        {
            return [];
        }

        object? system = null;
        foreach (ModSystem candidate in api.ModLoader.Systems)
        {
            if (systemType.IsInstanceOfType(candidate))
            {
                system = candidate;
                break;
            }
        }

        if (system == null)
        {
            return [];
        }

        object? models = Traverse.Create(system).Property("CustomModels").GetValue();
        if (models is not System.Collections.IDictionary map || !map.Contains(modelCode))
        {
            return [];
        }

        object? data = map[modelCode];
        object? extras = data == null ? null : Traverse.Create(data).Property("ExtraTraits").GetValue();
        return extras as string[] ?? [];
    }

    static EntityBehavior? FindSkinBehavior(Entity entity)
    {
        EntityBehavior? named = entity.GetBehavior("skinnableplayercustommodel")
            ?? entity.GetBehavior(ExtraSkinnableBehavior);
        if (named != null)
        {
            return named;
        }

        if (entity.SidedProperties?.Behaviors == null)
        {
            return null;
        }

        foreach (EntityBehavior behavior in entity.SidedProperties.Behaviors)
        {
            if (behavior.GetType().FullName == "PlayerModelLib.PlayerSkinBehavior")
            {
                return behavior;
            }
        }

        return null;
    }

    static string BuildClassPreviewHtml(
        CharacterClass characterClass,
        CharacterSystem characters,
        ICoreClientAPI capi,
        string[] modelExtraTraits)
    {
        StringBuilder sb = new();
        CreateCharacterClassTab.SplitCharacterDesc(characterClass.Code, out string flavor, out string body);
        if (!string.IsNullOrWhiteSpace(flavor))
        {
            sb.AppendLine(flavor.Trim());
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            sb.AppendLine(body.Trim());
            sb.AppendLine();
        }

        Dictionary<string, int> scores = ResolvePreviewScores(capi, characterClass.Code, modelExtraTraits);
        sb.AppendLine(Lang.Get("prosequor:class-starting-attributes"));
        foreach (string id in AttributeIds.All)
        {
            int score = scores.TryGetValue(id, out int value) ? value : AttributeGrowth.DefaultScore;
            sb.AppendLine($"{Lang.Get("prosequor:attribute-" + id)}: {score}");
        }

        sb.AppendLine();
        sb.AppendLine(Lang.Get("prosequor:class-traits"));
        sb.Append(CreateCharacterClassTab.BuildTraitsHtml(characterClass, characters));

        List<string> leftoverExtras = [];
        ITraitAttributeRegistry? registry = ProsequorModSystem.For(capi)?.TraitAttributes;
        foreach (string code in modelExtraTraits)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

            if (registry != null && !TraitAttributeConverter.ShouldKeepOnClass(registry, code))
            {
                continue;
            }

            leftoverExtras.Add(code);
        }

        if (leftoverExtras.Count > 0 && characters.TraitsByCode != null)
        {
            sb.AppendLine();
            sb.AppendLine(Lang.Get("model-traits-title"));

            CharacterClass extrasAsClass = new() { Code = characterClass.Code, Traits = leftoverExtras.ToArray() };
            sb.Append(CreateCharacterClassTab.BuildTraitsHtml(extrasAsClass, characters));
        }

        return sb.ToString();
    }

    static Dictionary<string, int> ResolvePreviewScores(
        ICoreClientAPI capi,
        string classCode,
        string[] extraTraits)
    {
        ITraitAttributeRegistry? registry = ProsequorModSystem.For(capi)?.TraitAttributes;
        Dictionary<string, int> scores = new(StringComparer.OrdinalIgnoreCase);
        foreach (string id in AttributeIds.All)
        {
            scores[id] = AttributeGrowth.DefaultScore;
        }

        if (registry == null)
        {
            return scores;
        }

        if (registry.ClassStartingScores.TryGetValue(classCode, out Dictionary<string, int>? cached)
            && cached != null)
        {
            return TraitAttributeConverter.WithExtraTraits(cached, registry, extraTraits);
        }

        return TraitAttributeConverter.WithExtraTraits(scores, registry, extraTraits);
    }
}
