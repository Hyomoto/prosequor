using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Prosequor.Ability;
using Prosequor.Data;
using Prosequor.Player;
using Prosequor.Progress;
using Prosequor.Xp;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Prosequor.Commands;

public static class ProgressCommands
{
    static readonly MethodInfo? BarrelTick = AccessTools.Method(typeof(BlockEntityBarrel), "OnEvery3Second");

    public static void Register(ICoreServerAPI api)
    {
        api.ChatCommands
            .Create("prosequor")
            .WithDescription("Prosequor progress debug (show / clear / emptybuckets / setlevel / addxp / points / addbucket / setattr / unlock / revoke / setfriendliness / addfriendliness / satiety / finishbarrel / pedigree / skillhint / fingerprint)")
            .RequiresPrivilege(Privilege.controlserver)
            .BeginSubCommand("show")
                .WithDescription("Show progress for a player (default: you)")
                .WithArgs(api.ChatCommands.Parsers.OptionalWord("player"))
                .HandleWith(args => OnShow(api, args))
            .EndSubCommand()
            .BeginSubCommand("skillhint")
                .WithDescription("Dump client skill-waiting HUD status (armed / draw / textures / rect)")
                .HandleWith(args => OnSkillHint(api, args))
            .EndSubCommand()
            .BeginSubCommand("fingerprint")
                .WithDescription("Show the server compiled-content fingerprint")
                .HandleWith(_ => OnFingerprint(api))
            .EndSubCommand()
            .BeginSubCommand("setlevel")
                .WithDescription("Set player or skill level")
                .WithArgs(
                    api.ChatCommands.Parsers.Word("player"),
                    api.ChatCommands.Parsers.All("skillOrPlayer and level"))
                .HandleWith(args => OnSetLevel(api, args))
            .EndSubCommand()
            .BeginSubCommand("addxp")
                .WithDescription("Add XP to player or a skill (optional trailing nb=Grant, fb=GrantAndFill)")
                .WithArgs(
                    api.ChatCommands.Parsers.Word("player"),
                    api.ChatCommands.Parsers.All("skillOrPlayer amount [nb|fb]"))
                .HandleWith(args => OnAddXp(api, args))
            .EndSubCommand()
            .BeginSubCommand("points")
                .WithDescription("Add or subtract unspent unlock points")
                .WithArgs(
                    api.ChatCommands.Parsers.Word("player"),
                    api.ChatCommands.Parsers.Int("amount"))
                .HandleWith(args => OnPoints(api, args))
            .EndSubCommand()
            .BeginSubCommand("addbucket")
                .WithDescription("Add growth credit to an attribute bucket")
                .WithArgs(
                    api.ChatCommands.Parsers.Word("player"),
                    api.ChatCommands.Parsers.Word("attribute"),
                    api.ChatCommands.Parsers.Float("amount"))
                .HandleWith(args => OnAddBucket(api, args))
            .EndSubCommand()
            .BeginSubCommand("setattr")
                .WithDescription("Set a body attribute score (0–18) and refresh effects")
                .WithArgs(
                    api.ChatCommands.Parsers.Word("player"),
                    api.ChatCommands.Parsers.Word("attribute"),
                    api.ChatCommands.Parsers.Int("value"))
                .HandleWith(args => OnSetAttribute(api, args))
            .EndSubCommand()
            .BeginSubCommand("unlock")
                .WithDescription("Grant an unlock code on a skill (costs 1 point)")
                .WithArgs(
                    api.ChatCommands.Parsers.Word("player"),
                    api.ChatCommands.Parsers.All("skill and code"))
                .HandleWith(args => OnUnlock(api, args, grant: true))
            .EndSubCommand()
            .BeginSubCommand("revoke")
                .WithDescription("Revoke an unlock code (refunds 1 point)")
                .WithArgs(
                    api.ChatCommands.Parsers.Word("player"),
                    api.ChatCommands.Parsers.All("skill and code"))
                .HandleWith(args => OnUnlock(api, args, grant: false))
            .EndSubCommand()
            .BeginSubCommand("clear")
                .WithDescription("Reset all progress (levels, XP, unlocks, buckets)")
                .WithArgs(api.ChatCommands.Parsers.OptionalWord("player"))
                .HandleWith(args => OnClear(api, args))
            .EndSubCommand()
            .BeginSubCommand("emptybuckets")
                .WithDescription("Zero saturation buckets and pending accrued XP")
                .WithArgs(api.ChatCommands.Parsers.OptionalWord("player"))
                .HandleWith(args => OnEmptyBuckets(api, args))
            .EndSubCommand()
            .BeginSubCommand("satiety")
                .WithDescription("Set hunger satiety to 0 so food can be eaten immediately (default: you)")
                .WithArgs(api.ChatCommands.Parsers.OptionalWord("player"))
                .HandleWith(args => OnSatiety(api, args))
            .EndSubCommand()
            .BeginSubCommand("setfriendliness")
                .WithDescription("Set animal friendliness (pedigree contributor total) on the looked-at entity (or entity id)")
                .WithArgs(
                    api.ChatCommands.Parsers.Int("value"),
                    api.ChatCommands.Parsers.OptionalLong("entityId"))
                .HandleWith(args => OnSetFriendliness(api, args))
            .EndSubCommand()
            .BeginSubCommand("addfriendliness")
                .WithDescription("Earn friendliness on the looked-at entity (or entity id) via the real gain path (contributor, cooldown, favorite roll). Skips the wait so you can fire it again immediately.")
                .WithArgs(
                    api.ChatCommands.Parsers.OptionalInt("amount", 1),
                    api.ChatCommands.Parsers.OptionalLong("entityId"))
                .HandleWith(args => OnAddFriendliness(api, args))
            .EndSubCommand()
            .BeginSubCommand("finishbarrel")
                .WithDescription("Finish processing on the looked-at sealed barrel")
                .HandleWith(args => OnFinishBarrel(api, args))
            .EndSubCommand()
            .BeginSubCommand("pedigree")
                .WithDescription("Dump pedigree on the held item, or the looked-at entity/block when the hand is empty")
                .HandleWith(args => OnPedigree(api, args))
            .EndSubCommand();
    }

    static TextCommandResult OnFingerprint(ICoreServerAPI api)
    {
        ContentFingerprint? fingerprint = ProsequorModSystem.For(api)?.Fingerprint;
        if (fingerprint == null)
        {
            return TextCommandResult.Error("Content fingerprint is not ready.");
        }

        return TextCommandResult.Success($"v{fingerprint.Version} {fingerprint.Hash}");
    }

    static TextCommandResult OnSkillHint(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        if (args.Caller.Player is not IServerPlayer player)
        {
            return TextCommandResult.Error("skillhint must be run by a player.");
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(api);
        if (mod?.Network == null)
        {
            return TextCommandResult.Error("Prosequor network is not ready.");
        }

        mod.Network.RequestSkillWaitingDump(player);
        return TextCommandResult.Success("Requested skill-waiting HUD dump (see client chat).");
    }

    static TextCommandResult OnShow(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        if (!TryResolvePlayer(api, args, optionalIndex: 0, out IServerPlayer player, out string? err))
        {
            return TextCommandResult.Error(err ?? "Player not found.");
        }

        EntityBehaviorProgress? progress = GetProgress(api, player);
        if (progress == null)
        {
            return TextCommandResult.Error("No prosequor progress behavior on that player.");
        }

        StringBuilder sb = new();
        sb.AppendLine($"Player {player.PlayerName}: level {progress.PlayerLevel}, xp {progress.PlayerXp:0.##}, until next {progress.PlayerXpUntilNext:0.##}, points {progress.UnlockPoints}");
        sb.Append("  Attributes:");
        foreach (string id in AttributeIds.All)
        {
            sb.Append($" {id}={progress.GetAttribute(id)} (bucket {progress.GetAttributeBucket(id):0.##})");
        }

        sb.AppendLine();
        ISkillRegistry? registry = ProsequorModSystem.For(api)?.Registry;
        if (registry != null)
        {
            foreach (SkillDef def in registry.All)
            {
                progress.GetSkillBar(def.Id, out float into, out int need, out int level);
                string unlocks = string.Join(", ", progress.GetUnlocks(def.Id)
                    .Select(id => $"{id} {progress.GetUnlockTier(def.Id, id)}/{TierCount(def, id)}"));
                sb.AppendLine($"  {DisplayNameForCaller(args, def)}: Lv {level}, into {into:0.##}/{need}, unlocks [{unlocks}]");
            }
        }

        foreach (KeyValuePair<string, SkillProgressState> kv in progress.State.Skills)
        {
            if (registry?.TryGet(kv.Key, out _) == true)
            {
                continue;
            }

            sb.AppendLine($"  {kv.Key} (unknown): Lv {kv.Value.Level}, xp {kv.Value.Xp:0.##}");
        }

        return TextCommandResult.Success(sb.ToString().TrimEnd());
    }

    static TextCommandResult OnSetLevel(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        if (!TryResolvePlayer(api, args[0] as string, out IServerPlayer player, out string? err))
        {
            return TextCommandResult.Error(err ?? "Player not found.");
        }

        EntityBehaviorProgress? progress = GetProgress(api, player);
        if (progress == null)
        {
            return TextCommandResult.Error("No prosequor progress behavior.");
        }

        if (!TrySplitTrailingInt(args[1] as string, out string target, out int level))
        {
            return TextCommandResult.Error("Expected <skill name|player> <level>.");
        }

        if (IsPlayerTrack(target))
        {
            progress.SetPlayerLevel(level);
            return TextCommandResult.Success($"Set {player.PlayerName} player level to {progress.PlayerLevel} (points {progress.UnlockPoints}).");
        }

        if (!TryResolveSkill(api, args, target, out SkillDef skill, out string skillError))
        {
            return TextCommandResult.Error(skillError);
        }

        progress.SetSkillLevel(skill.Id, level);
        return TextCommandResult.Success(
            $"Set {player.PlayerName} skill '{DisplayNameForCaller(args, skill)}' to level {progress.GetSkillLevel(skill.Id)}.");
    }

    static TextCommandResult OnAddXp(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        if (!TryResolvePlayer(api, args[0] as string, out IServerPlayer player, out string? err))
        {
            return TextCommandResult.Error(err ?? "Player not found.");
        }

        EntityBehaviorProgress? progress = GetProgress(api, player);
        if (progress == null)
        {
            return TextCommandResult.Error("No prosequor progress behavior.");
        }

        if (!TryParseAddXpTail(args[1] as string, out string target, out float amount, out XpAwardMode mode, out string? parseError))
        {
            return TextCommandResult.Error(parseError ?? "Expected <skill name|player> <amount> [nb|fb].");
        }

        string modeNote = mode switch
        {
            XpAwardMode.Grant => " (nb/Grant)",
            XpAwardMode.GrantAndFill => " (fb/GrantAndFill)",
            _ => ""
        };

        if (IsPlayerTrack(target))
        {
            progress.AddPlayerXp(amount, mode);
            return TextCommandResult.Success(
                $"Added {amount} player XP{modeNote} to {player.PlayerName}: Lv {progress.PlayerLevel}, xp {progress.PlayerXp:0.##}, points {progress.UnlockPoints}.");
        }

        if (!TryResolveSkill(api, args, target, out SkillDef skill, out string skillError))
        {
            return TextCommandResult.Error(skillError);
        }

        progress.AddSkillXp(skill.Id, amount, fact: null, mode);
        return TextCommandResult.Success(
            $"Added {amount} XP{modeNote} to {player.PlayerName}/{DisplayNameForCaller(args, skill)}: " +
            $"Lv {progress.GetSkillLevel(skill.Id)}, xp {progress.GetSkillXp(skill.Id):0.##}.");
    }

    static bool TryParseAddXpTail(
        string? text,
        out string target,
        out float amount,
        out XpAwardMode mode,
        out string? error)
    {
        target = "";
        amount = 0f;
        mode = XpAwardMode.Earn;
        error = null;

        string value = text?.Trim() ?? "";
        if (value.Length == 0)
        {
            error = "Expected <skill name|player> <amount> [nb|fb].";
            return false;
        }

        List<string> parts = value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count >= 1 && IsAwardModeFlag(parts[^1], out XpAwardMode flagMode))
        {
            mode = flagMode;
            parts.RemoveAt(parts.Count - 1);
            if (parts.Count >= 1 && IsAwardModeFlag(parts[^1], out _))
            {
                error = "Use only one of nb or fb.";
                return false;
            }
        }

        if (parts.Count < 2)
        {
            error = "Expected <skill name|player> <amount> [nb|fb].";
            return false;
        }

        string amountRaw = parts[^1];
        if (!float.TryParse(amountRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out amount)
            && !float.TryParse(amountRaw, NumberStyles.Float, CultureInfo.CurrentCulture, out amount))
        {
            error = "Expected <skill name|player> <amount> [nb|fb].";
            return false;
        }

        parts.RemoveAt(parts.Count - 1);
        target = string.Join(' ', parts);
        return target.Length > 0;
    }

    static bool IsAwardModeFlag(string token, out XpAwardMode mode)
    {
        if (string.Equals(token, "nb", StringComparison.OrdinalIgnoreCase))
        {
            mode = XpAwardMode.Grant;
            return true;
        }

        if (string.Equals(token, "fb", StringComparison.OrdinalIgnoreCase))
        {
            mode = XpAwardMode.GrantAndFill;
            return true;
        }

        mode = XpAwardMode.Earn;
        return false;
    }

    static TextCommandResult OnPoints(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        if (!TryResolvePlayer(api, args[0] as string, out IServerPlayer player, out string? err))
        {
            return TextCommandResult.Error(err ?? "Player not found.");
        }

        EntityBehaviorProgress? progress = GetProgress(api, player);
        if (progress == null)
        {
            return TextCommandResult.Error("No prosequor progress behavior.");
        }

        int amount = args[1] is int i ? i : Convert.ToInt32(args[1]);
        progress.AddUnlockPoints(amount);
        return TextCommandResult.Success($"{player.PlayerName} unlock points now {progress.UnlockPoints}.");
    }

    static TextCommandResult OnAddBucket(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        if (!TryResolvePlayer(api, args[0] as string, out IServerPlayer player, out string? err))
        {
            return TextCommandResult.Error(err ?? "Player not found.");
        }

        EntityBehaviorProgress? progress = GetProgress(api, player);
        if (progress == null)
        {
            return TextCommandResult.Error("No prosequor progress behavior.");
        }

        string? attribute = args[1] as string;
        string? canonical = AttributeIds.Canonicalize(attribute ?? "");
        if (canonical == null)
        {
            return TextCommandResult.Error(
                $"Unknown attribute '{attribute}'. Expected: {string.Join(", ", AttributeIds.All)}.");
        }

        float amount = args[2] is float f ? f : Convert.ToSingle(args[2], CultureInfo.InvariantCulture);
        progress.AddAttributeBucket(canonical, amount);
        return TextCommandResult.Success(
            $"{player.PlayerName} {canonical} bucket now {progress.GetAttributeBucket(canonical):0.##} " +
            $"(score {progress.GetAttribute(canonical)}).");
    }

    static TextCommandResult OnSetAttribute(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        if (!TryResolvePlayer(api, args[0] as string, out IServerPlayer player, out string? err))
        {
            return TextCommandResult.Error(err ?? "Player not found.");
        }

        EntityBehaviorProgress? progress = GetProgress(api, player);
        if (progress == null)
        {
            return TextCommandResult.Error("No prosequor progress behavior.");
        }

        string? attribute = args[1] as string;
        string? canonical = AttributeIds.Canonicalize(attribute ?? "");
        if (canonical == null)
        {
            return TextCommandResult.Error(
                $"Unknown attribute '{attribute}'. Expected: {string.Join(", ", AttributeIds.All)}.");
        }

        int value = args[2] is int i ? i : Convert.ToInt32(args[2]);
        progress.SetAttribute(canonical, value);
        return TextCommandResult.Success(
            $"{player.PlayerName} {canonical} score now {progress.GetAttribute(canonical)}.");
    }

    static TextCommandResult OnClear(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        if (!TryResolvePlayer(api, args, optionalIndex: 0, out IServerPlayer player, out string? err))
        {
            return TextCommandResult.Error(err ?? "Player not found.");
        }

        EntityBehaviorProgress? progress = GetProgress(api, player);
        if (progress == null)
        {
            return TextCommandResult.Error("No prosequor progress behavior.");
        }

        ISkillRegistry? registry = ProsequorModSystem.For(api)?.Registry;
        if (registry == null)
        {
            return TextCommandResult.Error("Prosequor skill registry is not loaded.");
        }

        progress.ClearProgress(registry);
        return TextCommandResult.Success($"Cleared all progress for {player.PlayerName}.");
    }

    static TextCommandResult OnEmptyBuckets(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        if (!TryResolvePlayer(api, args, optionalIndex: 0, out IServerPlayer player, out string? err))
        {
            return TextCommandResult.Error(err ?? "Player not found.");
        }

        EntityBehaviorProgress? progress = GetProgress(api, player);
        if (progress == null)
        {
            return TextCommandResult.Error("No prosequor progress behavior.");
        }

        progress.EmptyBuckets();
        return TextCommandResult.Success($"Emptied XP buckets for {player.PlayerName}.");
    }

    static TextCommandResult OnSatiety(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        if (!TryResolvePlayer(api, args, optionalIndex: 0, out IServerPlayer player, out string? err))
        {
            return TextCommandResult.Error(err ?? "Player not found.");
        }

        EntityBehaviorHunger? hunger = player.Entity?.GetBehavior<EntityBehaviorHunger>();
        if (hunger == null)
        {
            return TextCommandResult.Error($"{player.PlayerName} has no hunger behavior.");
        }

        float before = hunger.Saturation;
        hunger.Saturation = 0f;
        return TextCommandResult.Success(
            $"Set satiety to 0 for {player.PlayerName} (was {before.ToString("0.#", CultureInfo.InvariantCulture)} / {hunger.MaxSaturation.ToString("0.#", CultureInfo.InvariantCulture)}).");
    }

    static TextCommandResult OnSetFriendliness(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        int value = (int)args[0];
        if (!TryResolveLookedAtEntity(api, args, entityIdIndex: 1, out Entity entity, out string? err))
        {
            return TextCommandResult.Error(err ?? "Look at an entity, or pass entityId.");
        }

        HusbandryFriendliness.Set(entity, value);
        return TextCommandResult.Success(
            $"Set friendliness={HusbandryFriendliness.Get(entity)} on {entity.Code} ({entity.EntityId}).");
    }

    static TextCommandResult OnAddFriendliness(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        int amount = args[0] is int parsed ? parsed : 1;
        if (amount <= 0)
        {
            return TextCommandResult.Error("Amount must be greater than 0.");
        }

        if (!TryResolveLookedAtEntity(api, args, entityIdIndex: 1, out Entity entity, out string? err))
        {
            return TextCommandResult.Error(err ?? "Look at an entity, or pass entityId.");
        }

        string? contributorUid = args.Caller.Player?.PlayerUID;
        if (string.IsNullOrWhiteSpace(contributorUid))
        {
            contributorUid = HusbandryFriendliness.AnonContributorUid;
        }

        int before = HusbandryFriendliness.Get(entity);
        HusbandryFriendliness.TryGetFavoriteSeraph(entity, out string? favoriteBefore);
        if (!HusbandryFriendliness.CanGain(entity))
        {
            HusbandryFriendliness.StampFriendlinessReadyAt(entity, 0);
        }

        HusbandryFriendliness.Add(entity, contributorUid, amount);
        int after = HusbandryFriendliness.Get(entity);
        HusbandryFriendliness.TryGetFavoriteSeraph(entity, out string? favoriteAfter);
        string favorite = favoriteAfter == null
            ? "none"
            : PedigreeInspect.ResolvePlayerName(api, favoriteAfter);
        string ready = HusbandryFriendliness.TryGetFriendlinessReadyAt(entity, out double readyAt)
            ? $", readyAt={FormatReadyAt(api, readyAt)}"
            : "";
        string favoriteNote = string.Equals(favoriteBefore, favoriteAfter, StringComparison.Ordinal)
            ? $"favorite={favorite}"
            : $"favorite {FormatFavorite(api, favoriteBefore)} → {favorite}";

        return TextCommandResult.Success(
            $"Added {amount} friendliness on {entity.Code} ({entity.EntityId}): {before} → {after} ({favoriteNote}{ready}).");
    }

    static string FormatFavorite(ICoreServerAPI api, string? uid) =>
        uid == null ? "none" : PedigreeInspect.ResolvePlayerName(api, uid);

    static string FormatReadyAt(ICoreServerAPI api, double readyAt)
    {
        double now = api.World.Calendar?.TotalHours ?? 0;
        double hours = readyAt - now;
        return hours.ToString("+0.#h;-0.#h;0h", CultureInfo.InvariantCulture);
    }

    static bool TryResolveLookedAtEntity(
        ICoreServerAPI api,
        TextCommandCallingArgs args,
        int entityIdIndex,
        out Entity entity,
        out string? error)
    {
        entity = null!;
        error = null;

        if (args.Parsers.Count > entityIdIndex && args[entityIdIndex] is long entityId && entityId > 0)
        {
            Entity? found = api.World.GetEntityById(entityId);
            if (found == null)
            {
                error = $"Entity {entityId} not found.";
                return false;
            }

            entity = found;
            return true;
        }

        if (args.Caller.Player?.Entity is EntityPlayer ep)
        {
            entity = ep.EntitySelection?.Entity!;
        }

        if (entity == null)
        {
            error = "Look at an entity, or pass entityId.";
            return false;
        }

        return true;
    }

    static TextCommandResult OnFinishBarrel(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        if (args.Caller.Player is not IServerPlayer)
        {
            return TextCommandResult.Error("finishbarrel must be run by a player.");
        }

        BlockSelection? sel = args.Caller.Player.CurrentBlockSelection;
        if (sel == null)
        {
            return TextCommandResult.Error("Look at a barrel.");
        }

        if (api.World.BlockAccessor.GetBlockEntity(sel.Position) is not BlockEntityBarrel barrel)
        {
            return TextCommandResult.Error("Look at a barrel.");
        }

        if (!TryFinishBarrel(barrel, out string error))
        {
            return TextCommandResult.Error(error);
        }

        return TextCommandResult.Success("Finished barrel processing.");
    }

    static TextCommandResult OnPedigree(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        if (args.Caller.Player is not IServerPlayer player)
        {
            return TextCommandResult.Error("pedigree must be run by a player.");
        }

        return TextCommandResult.Success(PedigreeInspect.Run(api, player));
    }

    /// <summary>
    /// Backdates seal time past the recipe duration and runs the vanilla 3s tick
    /// so craft completion, mutate-process, XP, and pedigree stamps still fire.
    /// </summary>
    internal static bool TryFinishBarrel(BlockEntityBarrel barrel, out string error)
    {
        error = "";
        if (barrel == null)
        {
            error = "Look at a barrel.";
            return false;
        }

        if (!barrel.Sealed)
        {
            error = "That barrel has not started processing.";
            return false;
        }

        BarrelRecipe? recipe = barrel.CurrentRecipe;
        if (recipe == null || recipe.SealHours <= 0.0)
        {
            error = "That barrel has no recipe in progress.";
            return false;
        }

        if (BarrelTick == null)
        {
            error = "Could not finish barrel processing.";
            return false;
        }

        ICoreAPI? api = barrel.Api;
        if (api?.World?.Calendar == null)
        {
            error = "Could not finish barrel processing.";
            return false;
        }

        barrel.SealedSinceTotalHours = api.World.Calendar.TotalHours - recipe.SealHours - 1.0;
        BarrelTick.Invoke(barrel, [3f]);
        if (barrel.Sealed)
        {
            error = "Barrel is still sealed (recipe did not complete).";
            return false;
        }

        return true;
    }

    static TextCommandResult OnUnlock(ICoreServerAPI api, TextCommandCallingArgs args, bool grant)
    {
        if (!TryResolvePlayer(api, args[0] as string, out IServerPlayer player, out string? err))
        {
            return TextCommandResult.Error(err ?? "Player not found.");
        }

        EntityBehaviorProgress? progress = GetProgress(api, player);
        if (progress == null)
        {
            return TextCommandResult.Error("No prosequor progress behavior.");
        }

        if (!TrySplitTrailingWord(args[1] as string, out string target, out string code))
        {
            return TextCommandResult.Error("Expected <skill name> <code>.");
        }

        if (!TryResolveSkill(api, args, target, out SkillDef skill, out string skillError))
        {
            return TextCommandResult.Error(skillError);
        }

        bool ok;
        if (grant && skill.Tree != null)
        {
            UnlockPurchaseStatus status = progress.TryPurchaseNode(skill.Id, code);
            ok = status == UnlockPurchaseStatus.Ok;
            if (!ok)
            {
                return TextCommandResult.Error($"Grant failed ({status}).");
            }
        }
        else
        {
            ok = grant ? progress.GrantUnlock(skill.Id, code) : progress.RevokeUnlock(skill.Id, code);
            if (!ok)
            {
                return TextCommandResult.Error(grant
                    ? "Grant failed (already unlocked, unknown node, or not enough points)."
                    : "Revoke failed (unlock not present).");
            }
        }

        return TextCommandResult.Success(
            $"{(grant ? "Granted" : "Revoked")} {DisplayNameForCaller(args, skill)}:{code} " +
            $"for {player.PlayerName}. Points {progress.UnlockPoints}.");
    }

    static bool TryResolveSkill(
        ICoreServerAPI api,
        TextCommandCallingArgs args,
        string query,
        out SkillDef skill,
        out string error)
    {
        ISkillRegistry? registry = ProsequorModSystem.For(api)?.Registry;
        string? languageCode = (args.Caller.Player as IServerPlayer)?.LanguageCode;
        if (registry?.TryResolve(query, languageCode, out skill) == true)
        {
            error = "";
            return true;
        }

        skill = null!;
        string available = registry == null
            ? ""
            : string.Join(", ", registry.All.Select(def => SkillRegistry.DisplayName(def, languageCode)));
        error = string.IsNullOrEmpty(available)
            ? $"Unknown skill '{query}'."
            : $"Unknown skill '{query}'. Available skills: {available}.";
        return false;
    }

    static int TierCount(SkillDef skill, string nodeId) =>
        skill.Tree?.TryGet(nodeId, out SkillTreeNodeDef node) == true ? node.MaxTier : 1;

    static string DisplayNameForCaller(TextCommandCallingArgs args, SkillDef skill)
    {
        string? languageCode = (args.Caller.Player as IServerPlayer)?.LanguageCode;
        return SkillRegistry.DisplayName(skill, languageCode);
    }

    static bool TrySplitTrailingInt(string? text, out string target, out int value)
    {
        if (TrySplitTrailingWord(text, out target, out string raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        target = "";
        value = 0;
        return false;
    }

    static bool TrySplitTrailingFloat(string? text, out string target, out float value)
    {
        if (TrySplitTrailingWord(text, out target, out string raw)
            && (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || float.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out value)))
        {
            return true;
        }

        target = "";
        value = 0;
        return false;
    }

    static bool TrySplitTrailingWord(string? text, out string leading, out string trailing)
    {
        string value = text?.Trim() ?? "";
        int split = value.LastIndexOfAny([' ', '\t']);
        if (split <= 0 || split >= value.Length - 1)
        {
            leading = "";
            trailing = "";
            return false;
        }

        leading = value[..split].Trim();
        trailing = value[(split + 1)..].Trim();
        return leading.Length > 0 && trailing.Length > 0;
    }

    static bool IsPlayerTrack(string target) =>
        string.Equals(target, "player", StringComparison.OrdinalIgnoreCase)
        || string.Equals(target, "self", StringComparison.OrdinalIgnoreCase);

    static EntityBehaviorProgress? GetProgress(ICoreServerAPI api, IServerPlayer player)
    {
        EntityBehaviorProgress? behavior = player.Entity?.GetBehavior<EntityBehaviorProgress>();
        if (behavior != null && ProsequorModSystem.For(api)?.Registry is ISkillRegistry registry)
        {
            behavior.EnsureLoaded(player, registry);
        }

        return behavior;
    }

    static bool TryResolvePlayer(ICoreServerAPI api, TextCommandCallingArgs args, int optionalIndex, out IServerPlayer player, out string? error)
    {
        player = null!;
        error = null;
        string? name = null;
        if (args.ArgCount > optionalIndex)
        {
            name = args[optionalIndex] as string;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            if (args.Caller.Player is IServerPlayer self)
            {
                player = self;
                return true;
            }

            error = "Specify a player name.";
            return false;
        }

        return TryResolvePlayer(api, name, out player, out error);
    }

    static bool TryResolvePlayer(ICoreServerAPI api, string? name, out IServerPlayer player, out string? error)
    {
        player = null!;
        error = null;
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Specify a player name.";
            return false;
        }

        IPlayer? found = api.World.AllOnlinePlayers.FirstOrDefault(p =>
            string.Equals(p.PlayerName, name, StringComparison.OrdinalIgnoreCase));
        if (found is IServerPlayer sp)
        {
            player = sp;
            return true;
        }

        error = $"Player '{name}' not online.";
        return false;
    }
}
