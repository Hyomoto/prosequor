using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Engine transitions that would otherwise drop Live pedigree: grow identity replace,
/// ItemCreature place (stack → entity), right-click pickup (entity → stack).
/// Crate catch/release needs no patch — <c>ToBytes</c> carries WatchedAttributes.
/// </summary>
public static class ProsequorEntityPedigreePatches
{
    /// <summary>
    /// Grow creates a new adult entity and despawns the child. Copy Live before vanilla
    /// copies generation / despawns.
    /// </summary>
    [HarmonyPatch(typeof(EntityBehaviorGrow), "BecomeAdult")]
    public static class GrowBecomeAdultPedigreePatch
    {
        [HarmonyPrefix]
        public static void Prefix(EntityBehaviorGrow __instance, Entity adult)
        {
            if (__instance?.entity == null || adult == null)
            {
                return;
            }

            ProsequorEntityPedigreeStation.Copy(__instance.entity, adult);
        }
    }

    /// <summary>
    /// ItemCreature place TakeOuts before SpawnEntity — snapshot primary blob in prefix,
    /// apply onto the spawned entity in postfix.
    /// </summary>
    [HarmonyPatch(typeof(ItemCreature), nameof(ItemCreature.OnHeldInteractStart))]
    public static class ItemCreaturePlacePedigreePatch
    {
        [ThreadStatic]
        static ProsequorBlob? pendingBlob;

        [ThreadStatic]
        static Vec3d? pendingPos;

        [HarmonyPrefix]
        public static void Prefix(ItemSlot slot, BlockSelection blockSel)
        {
            pendingBlob = null;
            pendingPos = null;
            if (blockSel?.Position == null || slot?.Itemstack == null)
            {
                return;
            }

            if (!ProsequorStackPedigree.TryGetPrimaryBlob(slot.Itemstack, out ProsequorBlob blob)
                || blob.IsAnonymous)
            {
                return;
            }

            pendingBlob = blob;
            pendingPos = SpawnPos(blockSel);
        }

        [HarmonyPostfix]
        public static void Postfix(EntityAgent byEntity, BlockSelection blockSel, EnumHandHandling handHandling)
        {
            ProsequorBlob? blob = pendingBlob;
            Vec3d? pos = pendingPos;
            pendingBlob = null;
            pendingPos = null;

            // ItemCreature sets PreventDefaultAction (3) on successful place.
            if (blob == null
                || blob.IsAnonymous
                || pos == null
                || byEntity?.World == null
                || handHandling == EnumHandHandling.NotHandled)
            {
                return;
            }

            Entity? spawned = byEntity.World.GetNearestEntity(
                pos,
                horRange: 1.5f,
                vertRange: 1.5f,
                (Entity e) =>
                    e != null
                    && e.Alive
                    && e.Attributes?.GetString("origin") == "playerplaced");

            if (spawned != null)
            {
                ProsequorEntityPedigreeStation.ApplyBlob(spawned, blob);
            }
        }

        static Vec3d SpawnPos(BlockSelection blockSel)
        {
            BlockPos p = blockSel.Position;
            int ox = blockSel.DidOffset ? 0 : blockSel.Face.Normali.X;
            int oy = blockSel.DidOffset ? 0 : blockSel.Face.Normali.Y;
            int oz = blockSel.DidOffset ? 0 : blockSel.Face.Normali.Z;
            return new Vec3d(p.X + ox + 0.5, p.Y + oy, p.Z + oz + 0.5);
        }
    }

    /// <summary>
    /// Right-click pickup gives a JSON template stack. Clone + stamp before give so a
    /// shared ResolvedItemstack is not mutated for later entities of the same type.
    /// </summary>
    [HarmonyPatch(typeof(EntityBehaviorRightClickPickup), nameof(EntityBehaviorRightClickPickup.OnInteract))]
    public static class RightClickPickupPedigreePatch
    {
        static readonly FieldInfo CollectedStackField =
            AccessTools.Field(typeof(EntityBehaviorRightClickPickup), "collectedStack")
            ?? throw new InvalidOperationException(
                "[prosequor] EntityBehaviorRightClickPickup.collectedStack missing.");

        [HarmonyPrefix]
        public static void Prefix(EntityBehaviorRightClickPickup __instance, EnumInteractMode mode)
        {
            if (mode != EnumInteractMode.Interact || __instance?.entity == null)
            {
                return;
            }

            if (CollectedStackField.GetValue(__instance) is not ItemStack template
                || template.StackSize <= 0)
            {
                return;
            }

            if (!ProsequorEntityPedigreeStation.TryGetBlob(__instance.entity, out _))
            {
                return;
            }

            ItemStack stamped = template.Clone();
            ProsequorEntityPedigreeStation.ApplyToStack(__instance.entity, stamped);
            CollectedStackField.SetValue(__instance, stamped);
        }
    }
}
