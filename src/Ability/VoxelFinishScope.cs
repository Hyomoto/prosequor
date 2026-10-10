using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Holds the selected voxel recipe across <c>CheckIfFinished</c>, which clears it
/// before the output stack is given. Clay reads the key at its stamp sites.
/// Smithing and knapping stamp the stack on the way out of the inventory or the world.
/// </summary>
public static class VoxelFinishScope
{
    [ThreadStatic]
    static State? current;

    sealed class State
    {
        public IPlayer Player = null!;
        public string RecipeKey = "";
        public string Caller = "";
        public bool InterceptGive;
        public ItemStack? OutputTemplate;
    }

    public static string? RecipeKey =>
        string.IsNullOrWhiteSpace(current?.RecipeKey) ? null : current.RecipeKey;

    /// <summary>Station block code captured with the recipe, when a finish is in progress.</summary>
    public static string? Caller =>
        string.IsNullOrWhiteSpace(current?.Caller) ? null : current.Caller;

    public static void BeginClay(IPlayer? player, BlockEntityClayForm? form)
    {
        string? key = ClayFormXpStation.RecipeKeyOf(form?.SelectedRecipe);
        if (player == null || form == null || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        current = new State
        {
            Player = player,
            RecipeKey = key.Trim(),
            Caller = EventFactBuilder.CodeOf(form.Block) ?? "",
            InterceptGive = false,
            OutputTemplate = form.SelectedRecipe?.Output?.ResolvedItemstack?.Clone()
        };
    }

    public static void BeginGive(IPlayer? player, string? recipeKey, Block? station)
    {
        string? caller = EventFactBuilder.CodeOf(station);
        if (player == null || string.IsNullOrWhiteSpace(recipeKey) || string.IsNullOrWhiteSpace(caller))
        {
            return;
        }

        current = new State
        {
            Player = player,
            RecipeKey = recipeKey.Trim(),
            Caller = caller,
            InterceptGive = true
        };
    }

    public static void End() => current = null;

    public static void OnTryGivePrefix(ItemStack? itemstack)
    {
        if (current is not { InterceptGive: true })
        {
            return;
        }

        Stamp(itemstack);
    }

    public static void OnTryGivePostfix(ItemStack? itemstack, bool given)
    {
        if (current is not { InterceptGive: true } || !given)
        {
            return;
        }

        Emit(itemstack);
    }

    public static void OnSpawn(ItemStack? itemstack)
    {
        if (current is not { InterceptGive: true } || itemstack == null)
        {
            return;
        }

        Stamp(itemstack);
        Emit(itemstack);
    }

    /// <summary>
    /// Clay's single-block exit places the output and returns no item.
    /// Emit with a clone of the recipe output when that block is what got placed.
    /// </summary>
    public static void TryEmitPlacedOutput(BlockEntityClayForm? form)
    {
        State? state = current;
        if (state == null
            || state.InterceptGive
            || form?.Api?.World == null
            || form.Pos == null)
        {
            return;
        }

        ItemStack? template = state.OutputTemplate;
        if (template == null || template.Class != EnumItemClass.Block || template.Block == null)
        {
            return;
        }

        Block block = form.Api.World.BlockAccessor.GetBlock(form.Pos);
        if (block == null || block.Id == 0 || block.Id != template.Block.Id)
        {
            return;
        }

        ItemStack subject = template.Clone();
        Stamp(subject);
        Emit(subject);
    }

    public static void Stamp(ItemStack? stack)
    {
        if (current == null || stack == null)
        {
            return;
        }

        CraftAttribution.StampMaker(stack, current.Player);
        CraftAttribution.StampRecipe(stack, current.RecipeKey);
    }

    public static void Emit(ItemStack? stack)
    {
        if (current == null || stack == null)
        {
            return;
        }

        ICoreAPI? api = current.Player.Entity?.World?.Api ?? current.Player.Entity?.Api;
        ProsequorModSystem.For(api)?.VoxelWorkXp?.NotifyFinished(current.Player, stack, current.Caller);
    }

    /// <summary>
    /// Patch concrete <c>SpawnItemEntity</c> implementations. The server world type
    /// is not a compile-time reference.
    /// </summary>
    public static void TryPatchSpawn(Harmony harmony)
    {
        if (harmony == null)
        {
            return;
        }

        Type[][] signatures =
        [
            [typeof(ItemStack), typeof(Vec3d), typeof(Vec3d)],
            [typeof(ItemStack), typeof(BlockPos), typeof(Vec3d)]
        ];
        HarmonyMethod prefix = new(typeof(VoxelFinishScope), nameof(SpawnPrefix));
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (Type type in SafeTypes(assembly))
            {
                if (type.IsInterface || type.IsAbstract)
                {
                    continue;
                }

                foreach (Type[] args in signatures)
                {
                    MethodInfo? method = AccessTools.DeclaredMethod(type, "SpawnItemEntity", args);
                    if (method == null || method.IsAbstract)
                    {
                        continue;
                    }

                    harmony.Patch(method, prefix: prefix);
                }
            }
        }
    }

    static void SpawnPrefix(ItemStack itemstack) => OnSpawn(itemstack);

    static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type != null)!;
        }
        catch (Exception)
        {
            return Array.Empty<Type>();
        }
    }

    [HarmonyPatch(typeof(PlayerInventoryManager), nameof(PlayerInventoryManager.TryGiveItemstack))]
    public static class VoxelFinishGivePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static void Prefix(ItemStack itemstack) => OnTryGivePrefix(itemstack);

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(ItemStack itemstack, bool __result) =>
            OnTryGivePostfix(itemstack, __result);
    }
}
