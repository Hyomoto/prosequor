using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Prosequor.Ability;

/// <summary>
/// Host-agnostic pedigree verbs. Stacks, block entities, and entities all expose
/// maker / contributors via the same <see cref="ProsequorBlob"/> shape.
/// </summary>
public static class ProsequorPedigree
{
    public static bool TryGetBlob(ItemStack? stack, out ProsequorBlob blob) =>
        ProsequorStackPedigree.TryGetPrimaryBlob(stack, out blob);

    public static bool TryGetBlob(BlockEntity? be, out ProsequorBlob blob) =>
        ProsequorBlockPedigreeStation.TryGetBlob(be, out blob);

    public static bool TryGetBlob(Entity? entity, out ProsequorBlob blob) =>
        ProsequorEntityPedigreeStation.TryGetBlob(entity, out blob);

    public static void StampMaker(ItemStack? stack, string? makerUid) =>
        ProsequorStackPedigree.StampMaker(stack, makerUid);

    public static void StampMaker(BlockEntity? be, string? makerUid) =>
        ProsequorBlockPedigreeStation.StampPlanter(be, makerUid);

    public static void StampMaker(Entity? entity, string? makerUid) =>
        ProsequorEntityPedigreeStation.StampMaker(entity, makerUid);

    public static void AddContributor(ItemStack? stack, string? contributorUid, int amount = 1) =>
        ProsequorStackPedigree.AddContributor(stack, contributorUid, amount);

    public static void AddContributor(BlockEntity? be, string? contributorUid, int amount = 1) =>
        ProsequorBlockPedigreeStation.AddContributor(be, contributorUid, amount);

    public static void AddContributor(Entity? entity, string? contributorUid, int amount = 1) =>
        ProsequorEntityPedigreeStation.AddContributor(entity, contributorUid, amount);

    public static void StampSoleContributor(BlockEntity? be, string? contributorUid) =>
        ProsequorBlockPedigreeStation.StampSoleContributor(be, contributorUid);

    public static bool TryGetSoleContributor(BlockEntity? be, out string? uid) =>
        ProsequorBlockPedigreeStation.TryGetSoleContributor(be, out uid);

    public static void ClearContributors(BlockEntity? be) =>
        ProsequorBlockPedigreeStation.ClearContributors(be);

    public static void ClearContributors(Entity? entity) =>
        ProsequorEntityPedigreeStation.ClearContributors(entity);

    public static void Clear(ItemStack? stack) =>
        ProsequorStackPedigree.ClearAll(stack);

    public static void Clear(BlockEntity? be) =>
        ProsequorBlockPedigreeStation.Clear(be);

    public static void Clear(Entity? entity) =>
        ProsequorEntityPedigreeStation.Clear(entity);

    public static void CaptureFromStack(BlockEntity? be, ItemStack? stack) =>
        ProsequorBlockPedigreeStation.CaptureFromPlacedStack(be, stack);

    public static void CaptureFromStack(Entity? entity, ItemStack? stack) =>
        ProsequorEntityPedigreeStation.CaptureFromStack(entity, stack);

    public static void ApplyToStack(BlockEntity? be, Block? block, ItemStack? stack) =>
        ProsequorBlockPedigreeStation.ApplyToStack(be, block, stack);

    public static void ApplyBlob(BlockEntity? be, ProsequorBlob blob) =>
        ProsequorBlockPedigreeStation.ApplyBlob(be, blob);

    public static void ApplyToStack(Entity? entity, ItemStack? stack) =>
        ProsequorEntityPedigreeStation.ApplyToStack(entity, stack);

    public static void Copy(Entity? from, Entity? to) =>
        ProsequorEntityPedigreeStation.Copy(from, to);
}
