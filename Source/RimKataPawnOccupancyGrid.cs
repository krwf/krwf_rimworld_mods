using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace KRWF.RimKata
{
    // Shared for the map's lifetime after its first search. The value retains
    // only cell data, so the weak key does not keep a discarded map alive.
    internal sealed class RimKataPawnOccupancyGrid
    {
        private static readonly ConditionalWeakTable<Map, RimKataPawnOccupancyGrid> ByMap =
            new ConditionalWeakTable<Map, RimKataPawnOccupancyGrid>();
        private static readonly ConditionalWeakTable<Map, RimKataPawnOccupancyGrid>
            .CreateValueCallback CreateGrid = map => new RimKataPawnOccupancyGrid(map);

        private readonly int width;
        private readonly int height;
        private readonly int[] counts;
        private readonly ulong[] words;

        private RimKataPawnOccupancyGrid(Map map)
        {
            width = map.Size.x;
            height = map.Size.z;
            counts = new int[map.cellIndices.NumGridCells];
            words = new ulong[(counts.Length + 63) >> 6];

            // Seed once from existing Pawns, not by walking every map cell.
            // Reading each occupied cell also preserves duplicate native grid
            // registrations and supports more than one Pawn sharing a cell.
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn.def.size.x == 1 && pawn.def.size.z == 1)
                {
                    SeedCell(map.thingGrid, pawn.Position);
                    continue;
                }

                CellRect rect = pawn.OccupiedRect();
                for (int z = rect.minZ; z <= rect.maxZ; z++)
                {
                    for (int x = rect.minX; x <= rect.maxX; x++)
                    {
                        SeedCell(map.thingGrid, new IntVec3(x, 0, z));
                    }
                }
            }
        }

        internal static RimKataPawnOccupancyGrid For(Map map)
            => map == null ? null : ByMap.GetValue(map, CreateGrid);

        internal bool HasPawn(int cellIndex)
            => (uint)cellIndex < (uint)counts.Length
                && (words[cellIndex >> 6] & (1UL << (cellIndex & 63))) != 0;

        private bool TryCellIndex(IntVec3 cell, out int cellIndex)
        {
            if ((uint)cell.x >= (uint)width || (uint)cell.z >= (uint)height)
            {
                cellIndex = -1;
                return false;
            }

            cellIndex = cell.z * width + cell.x;
            return true;
        }

        private void SeedCell(ThingGrid thingGrid, IntVec3 cell)
        {
            if (!TryCellIndex(cell, out int cellIndex) || counts[cellIndex] != 0)
            {
                return;
            }

            List<Thing> things = thingGrid.ThingsListAtFast(cellIndex);
            int count = 0;
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Pawn)
                {
                    count++;
                }
            }

            if (count != 0)
            {
                counts[cellIndex] = count;
                words[cellIndex >> 6] |= 1UL << (cellIndex & 63);
            }
        }

        private void Register(int cellIndex)
        {
            if (counts[cellIndex]++ == 0)
            {
                words[cellIndex >> 6] |= 1UL << (cellIndex & 63);
            }
        }

        private void Deregister(int cellIndex)
        {
            int count = counts[cellIndex];
            if (count <= 0)
            {
                return;
            }

            counts[cellIndex] = count - 1;
            if (count == 1)
            {
                words[cellIndex >> 6] &= ~(1UL << (cellIndex & 63));
            }
        }

        // Per-call value state avoids retaining any Thing/Pawn in the cache.
        // Comparing the existing list's count needs no additional Contains
        // scan and distinguishes native no-op removals or skipped originals.
        internal readonly struct CellMutation
        {
            private readonly RimKataPawnOccupancyGrid grid;
            private readonly List<Thing> things;
            private readonly int cellIndex;
            private readonly int beforeCount;

            internal CellMutation(RimKataPawnOccupancyGrid grid,
                List<Thing> things, int cellIndex)
            {
                this.grid = grid;
                this.things = things;
                this.cellIndex = cellIndex;
                beforeCount = things.Count;
            }

            internal void FinishRegistration()
            {
                if (grid != null && things.Count > beforeCount)
                {
                    grid.Register(cellIndex);
                }
            }

            internal void FinishDeregistration()
            {
                if (grid != null && things.Count < beforeCount)
                {
                    grid.Deregister(cellIndex);
                }
            }
        }

        internal static CellMutation CaptureMutation(Thing thing, IntVec3 cell,
            Map map, ThingGrid thingGrid)
        {
            // Ordinary grid mutations never allocate or initialize a cache.
            if (!(thing is Pawn) || map == null
                || !ByMap.TryGetValue(map, out RimKataPawnOccupancyGrid grid)
                || !grid.TryCellIndex(cell, out int cellIndex))
            {
                return default;
            }

            return new CellMutation(grid, thingGrid.ThingsListAtFast(cellIndex), cellIndex);
        }
    }

    [HarmonyPatch(typeof(ThingGrid), "RegisterInCell", new[] { typeof(Thing), typeof(IntVec3) })]
    internal static class Patch_ThingGrid_RegisterInCell_RimKataPawnOccupancy
    {
        private static void Prefix(ThingGrid __instance, Thing __0, IntVec3 __1,
            Map ___map, out RimKataPawnOccupancyGrid.CellMutation __state)
            => __state = RimKataPawnOccupancyGrid.CaptureMutation(__0, __1, ___map, __instance);

        private static void Postfix(RimKataPawnOccupancyGrid.CellMutation __state)
            => __state.FinishRegistration();
    }

    [HarmonyPatch(typeof(ThingGrid), "DeregisterInCell", new[] { typeof(Thing), typeof(IntVec3) })]
    internal static class Patch_ThingGrid_DeregisterInCell_RimKataPawnOccupancy
    {
        private static void Prefix(ThingGrid __instance, Thing __0, IntVec3 __1,
            Map ___map, out RimKataPawnOccupancyGrid.CellMutation __state)
            => __state = RimKataPawnOccupancyGrid.CaptureMutation(__0, __1, ___map, __instance);

        private static void Postfix(RimKataPawnOccupancyGrid.CellMutation __state)
            => __state.FinishDeregistration();
    }
}
