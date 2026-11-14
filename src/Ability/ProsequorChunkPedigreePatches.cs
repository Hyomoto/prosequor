using HarmonyLib;
using Vintagestory.API.Datastructures;
using Vintagestory.Server;

namespace Prosequor.Ability;

/// <summary>
/// Flush live pedigree entries into chunk moddata before the engine copies that
/// dictionary for save or client send.
/// </summary>
public static class ProsequorChunkPedigreePatches
{
    [HarmonyPatch(typeof(ServerChunk), nameof(ServerChunk.ToBytes), typeof(FastMemoryStream))]
    public static class ServerChunkToBytesPedigreeFlushPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ServerChunk __instance) =>
            ProsequorChunkPedigree.FlushIfDirty(__instance);
    }

    [HarmonyPatch(typeof(ServerChunk), "ToPacket")]
    public static class ServerChunkToPacketPedigreeFlushPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ServerChunk __instance) =>
            ProsequorChunkPedigree.FlushIfDirty(__instance);
    }
}
