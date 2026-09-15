using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using Prosequor.Data;
using Prosequor.Player;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace Prosequor.Client;

/// <summary>
/// Replaces the Character dialog Traits tab with a scrollable inset of leftover traits,
/// qualitative attribute effects, and blended entity-stat lines.
/// </summary>
[HarmonyPatch(typeof(CharacterSystem), "composeTraitsTab")]
public static class CharacterTraitsTabPatches
{
    const string BodyKey = "prosequorTraitsBody";
    const string ScrollKey = "prosequorTraitsScroll";
    const double ScrollbarWidth = 16;
    const double InsetPad = 6;
    const double TopY = 25;
    const double FallbackWidth = 385;
    const double FallbackHeight = 440;

    static GuiComposer? activeComposer;
    static ElementBounds? clipBounds;
    static ElementBounds? contentBounds;
    static float scrollY;
    static EntityBehaviorProgress? observedProgress;
    static Entity? observedEntity;
    static CharacterSystem? activeModSys;
    static ICoreClientAPI? activeCapi;
    static string? lastHtml;

    public static void Dispose()
    {
        DetachProgress();
        DetachStats();
        activeComposer = null;
        activeModSys = null;
        activeCapi = null;
        clipBounds = null;
        contentBounds = null;
        lastHtml = null;
    }

    [HarmonyPrefix]
    public static bool Prefix(CharacterSystem __instance, GuiComposer compo)
    {
        ICoreClientAPI? capi = Traverse.Create(__instance).Field<ICoreClientAPI>("capi").Value;
        if (capi == null)
        {
            return true;
        }

        DetachProgress();
        DetachStats();
        lastHtml = null;
        activeComposer = compo;
        activeModSys = __instance;
        activeCapi = capi;
        scrollY = 0f;

        double width = FallbackWidth;
        double height = FallbackHeight;
        ElementBounds parent = compo.CurParentBounds;
        if (parent != null)
        {
            if (parent.fixedWidth > 50)
            {
                width = parent.fixedWidth;
            }

            if (parent.fixedHeight > 50)
            {
                height = parent.fixedHeight;
            }
        }

        double boxH = Math.Max(120, height - TopY - 8);
        ElementBounds box = ElementBounds.Fixed(0, TopY, width, boxH);
        compo.AddInset(box, 2, 0.85f);

        double listW = width - ScrollbarWidth - InsetPad * 2 - 4;
        clipBounds = ElementBounds.Fixed(InsetPad, TopY + InsetPad, listW, boxH - InsetPad * 2);
        contentBounds = ElementBounds
            .Fixed(0, 0, listW, Math.Max(boxH - InsetPad * 2, 40))
            .WithParent(clipBounds);
        ElementBounds scrollBounds = ElementBounds.Fixed(
            width - ScrollbarWidth - InsetPad,
            TopY + InsetPad,
            ScrollbarWidth,
            boxH - InsetPad * 2);

        compo.BeginClip(clipBounds);
        compo.AddRichtext(
            "",
            CairoFont.WhiteDetailText().WithLineHeightMultiplier(1.15),
            contentBounds,
            BodyKey);
        compo.EndClip();
        compo.AddVerticalScrollbar(OnScroll, scrollBounds, ScrollKey);

        EntityPlayer entity = capi.World.Player.Entity;
        EntityBehaviorProgress? progress = entity.GetBehavior<EntityBehaviorProgress>();
        AttachProgress(progress);
        AttachStats(entity);

        Refresh();
        return false;
    }

    static void OnScroll(float value)
    {
        scrollY = Math.Max(0f, value);
        if (contentBounds == null)
        {
            return;
        }

        contentBounds.fixedY = -scrollY;
        contentBounds.CalcWorldBounds();
    }

    static void AttachProgress(EntityBehaviorProgress? progress)
    {
        DetachProgress();
        observedProgress = progress;
        if (observedProgress != null)
        {
            observedProgress.Changed += OnProgressChanged;
        }
    }

    static void DetachProgress()
    {
        if (observedProgress != null)
        {
            observedProgress.Changed -= OnProgressChanged;
            observedProgress = null;
        }
    }

    static void AttachStats(Entity entity)
    {
        DetachStats();
        observedEntity = entity;
        observedEntity.WatchedAttributes.RegisterModifiedListener("stats", OnStatsChanged);
    }

    static void DetachStats()
    {
        if (observedEntity == null)
        {
            return;
        }

        observedEntity.WatchedAttributes.UnregisterListener(OnStatsChanged);
        observedEntity = null;
    }

    static void OnProgressChanged() => Refresh();

    static void OnStatsChanged() => Refresh();

    static void Refresh()
    {
        GuiComposer? compo = activeComposer;
        CharacterSystem? modSys = activeModSys;
        ICoreClientAPI? capi = activeCapi;
        if (compo == null || modSys == null || capi == null)
        {
            return;
        }

        string html = BuildHtml(modSys, capi);
        GuiElementRichtext? body = compo.GetRichtext(BodyKey);
        if (body == null)
        {
            // Traits tab composer was torn down (switched tabs / closed dialog).
            Dispose();
            return;
        }

        if (html == lastHtml)
        {
            return;
        }

        lastHtml = html;
        body.SetNewText(html, CairoFont.WhiteDetailText().WithLineHeightMultiplier(1.15));

        double contentH = body.Bounds.fixedHeight;
        if (contentH < 40)
        {
            contentH = 40;
        }

        scrollY = 0f;
        if (contentBounds != null && clipBounds != null)
        {
            contentBounds.fixedHeight = Math.Max(clipBounds.fixedHeight, contentH);
            contentBounds.fixedY = 0;
            contentBounds.CalcWorldBounds();
        }

        GuiElementScrollbar? scrollbar = compo.GetScrollbar(ScrollKey);
        if (scrollbar != null && clipBounds != null)
        {
            scrollbar.SetHeights(
                (float)clipBounds.fixedHeight,
                (float)Math.Max(clipBounds.fixedHeight, contentH));
            // SetHeights can leave / fire a stale offset — pin to top like create-char traits.
            scrollbar.CurrentYPosition = 0;
        }
    }

    public static string BuildHtml(CharacterSystem modSys, ICoreClientAPI capi)
    {
        EntityPlayer entity = capi.World.Player.Entity;
        StringBuilder sb = new();
        AppendTraits(sb, modSys, entity);

        HashSet<string> emittedBlended = new(StringComparer.OrdinalIgnoreCase);
        List<(string Text, bool Positive)> blended = [];
        foreach ((string text, bool positive) in BlendedStatDescription.EnumerateActiveLines(
                     entity,
                     emittedBlended))
        {
            blended.Add((text, positive));
        }

        IAttributeStatRegistry? registry = ProsequorModSystem.For(capi)?.AttributeStats;
        IPlayerProgress? progress = entity.GetBehavior<EntityBehaviorProgress>();
        if (registry != null)
        {
            foreach ((string text, bool positive) in AttributeEffectDescription.EnumerateActiveLines(
                         registry,
                         progress,
                         emittedBlended))
            {
                sb.AppendLine(AttributeEffectDescription.ColorWrap(text, positive));
            }
        }

        foreach ((string text, bool positive) in blended)
        {
            sb.AppendLine(AttributeEffectDescription.ColorWrap(text, positive));
        }

        if (sb.Length == 0)
        {
            return Lang.Get("prosequor:traits-tab-empty");
        }

        return sb.ToString();
    }

    static void AppendTraits(StringBuilder sb, CharacterSystem modSys, EntityPlayer entity)
    {
        List<string> codes = new();
        string? charClass = entity.WatchedAttributes.GetString("characterClass");
        if (!string.IsNullOrEmpty(charClass)
            && modSys.characterClassesByCode.TryGetValue(charClass, out CharacterClass? cls)
            && cls?.Traits != null)
        {
            codes.AddRange(cls.Traits);
        }

        string[]? extra = entity.WatchedAttributes.GetStringArray("extraTraits");
        if (extra != null)
        {
            foreach (string code in extra)
            {
                if (!string.IsNullOrWhiteSpace(code)
                    && !codes.Contains(code, StringComparer.OrdinalIgnoreCase))
                {
                    codes.Add(code);
                }
            }
        }

        if (codes.Count == 0)
        {
            return;
        }

        StringBuilder attrs = new();
        foreach (Trait item in codes
                     .Select(code => modSys.TraitsByCode.TryGetValue(code, out Trait? t) ? t : null)
                     .Where(t => t != null)
                     .Cast<Trait>()
                     .OrderBy(t => (int)t.Type))
        {
            attrs.Clear();
            foreach (KeyValuePair<string, double> attribute in item.Attributes)
            {
                if (attrs.Length > 0)
                {
                    attrs.Append(", ");
                }

                attrs.Append(
                    Lang.Get(
                        string.Format(
                            GlobalConstants.DefaultCultureInfo,
                            "charattribute-{0}-{1}",
                            attribute.Key,
                            attribute.Value)));
            }

            if (attrs.Length > 0)
            {
                sb.AppendLine(
                    StripTraitBullet(
                        Lang.Get(
                            "traitwithattributes",
                            Lang.Get("trait-" + item.Code),
                            attrs.ToString())));
                continue;
            }

            string? desc = Lang.GetIfExists("traitdesc-" + item.Code);
            if (desc != null)
            {
                sb.AppendLine(
                    StripTraitBullet(
                        Lang.Get(
                            "traitwithattributes",
                            Lang.Get("trait-" + item.Code),
                            desc)));
            }
            else
            {
                sb.AppendLine(StripTraitBullet(Lang.Get("trait-" + item.Code)));
            }
        }
    }

    /// <summary>
    /// Vanilla <c>trait-*</c> keys bake in a leading bullet (often inside font markup).
    /// Attribute effect lines have none — strip so the list reads consistently.
    /// </summary>
    static string StripTraitBullet(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // "• Name" or "<font …>• Name" / "<font …> • Name"
        string stripped = Regex.Replace(
            text,
            @"(^|>)\s*[•\u2022\u25CF\u25E6\u00B7]\s*",
            "$1");
        return stripped.TrimStart();
    }
}
