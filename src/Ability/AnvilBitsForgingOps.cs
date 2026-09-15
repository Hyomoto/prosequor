using Prosequor.Player;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>Thread-local aim voxel for aim-aware bit <c>TryPut</c> → <c>TryPlaceOn</c>.</summary>
public static class AnvilBitAimScope
{
    [ThreadStatic]
    static Vec3i? aim;

    public static Vec3i? Current => aim;

    public static void Begin(Vec3i? voxel) => aim = voxel;

    public static void End() => aim = null;
}

/// <summary>
/// Bits Forging: place heated metal bits onto existing anvil work with bottom-on-metal
/// support (never Y=0 / never floating). Aim is the solid metal voxel the player selects.
/// </summary>
public static class AnvilBitsForgingOps
{
    public const string SkillId = "metalworking";
    public const string UnlockId = "bits-forging";
    public const int BitsForIngot = 21;

    public static int VoxelsPerBit =>
        Math.Max(1, (int)Math.Round(ItemIngot.VoxelCount / (float)BitsForIngot));

    public static bool IsMetalBit(CollectibleObject? obj) =>
        obj?.Code != null
        && obj.Code.Path.StartsWith("metalbit", StringComparison.OrdinalIgnoreCase);

    public static bool HasBitsForging(IPlayer? player)
    {
        if (player == null)
        {
            return false;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        return progress != null && progress.HasUnlock(SkillId, UnlockId);
    }

    /// <summary>
    /// Decode <paramref name="blockSel"/>'s fine selection into a voxel on the work item.
    /// Returns false when the player aimed the anvil body (index 0) or an empty miss.
    /// </summary>
    public static bool TryDecodeAimVoxel(
        BlockEntityAnvil anvil,
        BlockSelection? blockSel,
        out Vec3i voxel)
    {
        voxel = new Vec3i();
        if (anvil?.Api?.World == null || blockSel == null || blockSel.SelectionBoxIndex <= 0)
        {
            return false;
        }

        Block? block = anvil.Api.World.BlockAccessor.GetBlock(anvil.Pos);
        if (block == null)
        {
            return false;
        }

        Cuboidf[] boxes = block.GetSelectionBoxes(anvil.Api.World.BlockAccessor, anvil.Pos);
        int idx = blockSel.SelectionBoxIndex;
        if (boxes == null || idx <= 0 || idx >= boxes.Length || boxes[idx] == null)
        {
            return false;
        }

        Cuboidf box = boxes[idx];
        int x = (int)(16f * box.X1);
        int y = (int)(16f * box.Y1) - 10;
        int z = (int)(16f * box.Z1);
        if (!AnvilVoxelGrid.InBounds(x, y, z))
        {
            return false;
        }

        voxel = new Vec3i(x, y, z);
        return true;
    }

    public static string ResolveBitMetal(ItemStack stack)
    {
        string? metal = null;
        if (stack?.Collectible?.Variant != null
            && stack.Collectible.Variant.TryGetValue("metal", out string? fromVariant))
        {
            metal = fromVariant;
        }

        if (string.IsNullOrEmpty(metal) && stack?.Collectible?.Code != null)
        {
            string path = stack.Collectible.Code.Path;
            metal = path.StartsWith("metalbit-", StringComparison.OrdinalIgnoreCase)
                ? path.Substring("metalbit-".Length)
                : stack.Collectible.LastCodePart();
        }

        if (string.Equals(metal, "blistersteel", StringComparison.OrdinalIgnoreCase))
        {
            return "steel";
        }

        return metal ?? "";
    }

    public static void ClientError(ICoreAPI? api, object source, string code, string message)
    {
        if (api?.Side != EnumAppSide.Client)
        {
            return;
        }

        ((ICoreClientAPI)api).TriggerIngameError(source, code, message);
    }
}
