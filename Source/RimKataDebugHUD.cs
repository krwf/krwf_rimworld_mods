using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace KRWF.RimKata
{
    [StaticConstructorOnStartup]
    public class RimKataDebugHUD : GameComponent
    {
        public static bool Enabled = false;
        public static bool SearchRangeEnabled = false;

        private const int EntryPopupTicks = 45;
        private static readonly Material SearchCellMaterial =
            SolidColorMaterials.SimpleSolidColorMaterial(
                new Color(0f, 0f, 0f, 0.3f));
        private static readonly List<Pawn> DebugPawns = new List<Pawn>();
        private static readonly List<IntVec3> SearchCells = new List<IntVec3>();
        private static readonly HashSet<IntVec3> UniqueSearchCells =
            new HashSet<IntVec3>();
        private static readonly List<Vector3> SearchMeshVertices =
            new List<Vector3>();
        private static readonly List<int> SearchMeshTriangles =
            new List<int>();
        private static readonly Mesh SearchMesh = CreateSearchMesh();
        private static Map searchMeshMap;
        private static int searchMeshTick = -1;

        private struct ActualSearchRing
        {
            public Map map;
            public IntVec3 origin;
            public float innerRadius;
            public float outerRadius;
            public int tick;
        }

        private static readonly List<ActualSearchRing> ActualSearchRings =
            new List<ActualSearchRing>();

        private struct RPopup
        {
            public int startTick;
            public bool state;
        }
        private static readonly Dictionary<Pawn, bool> previousCombatState = new Dictionary<Pawn, bool>();

        private static readonly Dictionary<Pawn, List<RPopup>> combatPopups = new Dictionary<Pawn, List<RPopup>>();

        private struct UPopup
        {
            public int startTick;
            public bool state;
        }

        private static readonly Dictionary<Pawn, bool> previousUsingState = new Dictionary<Pawn, bool>();

        private static readonly Dictionary<Pawn, UPopup> usingPopups = new Dictionary<Pawn, UPopup>();

        private static void UpdateCombatPopup(
            Pawn pawn,
            bool combatActive)
        {
            if (!previousCombatState.TryGetValue(pawn, out bool previous))
            {
                previousCombatState[pawn] = combatActive;
                return;
            }

            if (previous == combatActive)
            {
                return;
            }

            if (!combatPopups.TryGetValue(pawn, out List<RPopup> popups))
            {
                popups = new List<RPopup>();
                combatPopups[pawn] = popups;
            }

            popups.Add(new RPopup
            {
                startTick = Find.TickManager.TicksGame,
                state = combatActive
            });

            previousCombatState[pawn] = combatActive;
        }

        private static void UpdateUsingPopup(
            Pawn pawn,
            bool usingRimKata)
        {
            if (!previousUsingState.TryGetValue(pawn, out bool previous))
            {
                previousUsingState[pawn] = usingRimKata;
                return;
            }

            if (previous == usingRimKata)
            {
                return;
            }

            usingPopups[pawn] = new UPopup
            {
                startTick = Find.TickManager.TicksGame,
                state = usingRimKata
            };

            previousUsingState[pawn] = usingRimKata;
        }

        public RimKataDebugHUD(Game game)
        {
        }

        public static void SetHudEnabled(bool enabled)
        {
            Enabled = enabled && Prefs.DevMode;
            if (!Enabled)
            {
                previousCombatState.Clear();
                combatPopups.Clear();
                previousUsingState.Clear();
                usingPopups.Clear();
            }
        }

        public static void SetSearchRangeEnabled(bool enabled)
        {
            SearchRangeEnabled = enabled && Prefs.DevMode;
            if (!SearchRangeEnabled)
            {
                ActualSearchRings.Clear();
                SearchCells.Clear();
                UniqueSearchCells.Clear();
                SearchMeshVertices.Clear();
                SearchMeshTriangles.Clear();
                SearchMesh.Clear();
                searchMeshMap = null;
                searchMeshTick = -1;
            }
        }

        public static void RecordSearchPulse(
            Map map,
            IntVec3 origin,
            int maximumRadius)
        {
        }

        public static void RecordActualSearchRing(
            Pawn owner,
            Map map,
            IntVec3 origin,
            float innerRadius,
            float outerRadius)
        {
            if (!Prefs.DevMode
                || !SearchRangeEnabled
                || RimKataEligibility.IsHostileToPlayerFaction(owner)
                || map == null
                || map.Disposed
                || !origin.IsValid
                || outerRadius <= 0f
                || innerRadius >= outerRadius)
            {
                return;
            }

            int currentTick = Find.TickManager?.TicksGame ?? -1;
            if (currentTick < 0)
            {
                return;
            }

            PruneActualSearchRings(currentTick);
            ActualSearchRings.Add(new ActualSearchRing
            {
                map = map,
                origin = origin,
                innerRadius = innerRadius,
                outerRadius = outerRadius,
                tick = currentTick
            });
            searchMeshTick = -1;
        }

        private static void DisableForDeveloperMode()
        {
            SetHudEnabled(false);
            SetSearchRangeEnabled(false);
            DebugPawns.Clear();
        }

        public override void GameComponentOnGUI()
        {
            base.GameComponentOnGUI();

            if (!Prefs.DevMode)
            {
                if (Enabled || SearchRangeEnabled)
                {
                    DisableForDeveloperMode();
                }

                return;
            }

            if (!Enabled)
            {
                return;
            }

            Map map = Find.CurrentMap;
            if (map == null || map.Disposed)
            {
                return;
            }

            var pawns = map.mapPawns.AllPawnsSpawned;
            DebugPawns.Clear();

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];

                if (pawn == null || !pawn.Spawned)
                {
                    continue;
                }

                if (!RimKataEligibilityCache.DebugHasRawAccessSource(pawn))
                {
                    continue;
                }

                DebugPawns.Add(pawn);
            }

            for (int i = 0; i < DebugPawns.Count; i++)
            {
                DrawPawnDebug(DebugPawns[i]);
            }
        }

        private static void DrawPawnDebug(Pawn pawn)
        {
            if (RimKataEligibility.IsHostileToPlayerFaction(pawn))
            {
                RimKataDualWeaponController.DebugTryGetExistingUsingState(
                    pawn,
                    out bool hostileUsingRimKata);
                DrawMinimalUsingState(pawn, hostileUsingRimKata);
                return;
            }

            bool swapPending = RimKataDualWeaponController.DebugWeaponSwapPending(pawn);

            bool cacheKnown = RimKataEligibilityCache.TryGetCachedAccess(pawn, out bool cachedAccess);

            bool progressiveSearch = RimKataDualWeaponController.DebugProgressiveSearchActive(pawn);

            RimKataDualWeaponController.TryGetDebugState(pawn, out char primaryState, out char secondaryState, out bool usingRimKata, out bool combatActive);

            string combatReasons = RimKataDraftedFireController.DebugCombatDemandReasons(pawn);

            UpdateUsingPopup(pawn, usingRimKata);
            UpdateCombatPopup(pawn, combatActive);

            Vector2 screenPos = pawn.DrawPos.MapToUIPosition();

            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;

            string cacheState;

            if (!cacheKnown)
            {
                cacheState = "F";
            }
            else if (cachedAccess)
            {
                cacheState = "T";
            }
            else
            {
                cacheState = "X";
            }

            ThingWithComps primaryWeapon = RimKataWeaponSlotUtility.PrimaryWeapon(pawn);

            ThingWithComps secondaryWeapon =
                RimKataWeaponSlotUtility.SecondaryWeapon(pawn);

            Verb primaryVerb = RimKataWeaponSlotUtility.CombatVerb(pawn, primaryWeapon);

            Verb secondaryVerb = RimKataWeaponSlotUtility.CombatVerb(pawn, secondaryWeapon);

            char primaryType =
                primaryVerb == null
                    ? '-'
                    : primaryVerb.IsMeleeAttack ? 'S' : 'L';

            char secondaryType =
                secondaryVerb == null
                    ? '-'
                    : secondaryVerb.IsMeleeAttack ? 'S' : 'L';

            RimKataDualWeaponController.GetDebugWeaponState(
                pawn,
                primaryWeapon,
                out primaryState,
                out bool primaryVanillaState);
            RimKataDualWeaponController.GetDebugWeaponState(
                pawn,
                secondaryWeapon,
                out secondaryState,
                out bool secondaryVanillaState);

            string primaryStateText = primaryVanillaState
                ? "B" + primaryState
                : primaryState.ToString();
            string secondaryStateText = secondaryVanillaState
                ? "B" + secondaryState
                : secondaryState.ToString();

            string status = $"RK-{cacheState}"
                + $"_A{primaryType}-{primaryStateText}"
                + $"_B{secondaryType}-{secondaryStateText}";

            Rect statusRect = new Rect(screenPos.x - 90f, screenPos.y - 37f, 180f, 24f);

            if (progressiveSearch)
            {
                Rect searchRect = new Rect(screenPos.x - 100f, screenPos.y - 57f, 30f, 24f);

                Widgets.Label(searchRect, "S");
            }

            Widgets.Label(statusRect, status);
            Rect usingRect = new Rect(screenPos.x - 70f, screenPos.y - 57f, 40f, 24f);

            Widgets.Label(usingRect, usingRimKata ? "UT" : "UF");

            if (swapPending)
            {
                Rect swapRect = new Rect(screenPos.x - 15f, screenPos.y - 57f, 30f, 24f);

                Widgets.Label(swapRect, "S");
            }

            string combatText =
                combatActive
                    ? $"RT-{combatReasons}"
                    : $"RF-{combatReasons}";

            float combatWidth = Mathf.Max(40f, Text.CalcSize(combatText).x + 8f);

            Rect combatRect = new Rect(screenPos.x + 30f, screenPos.y - 57f, combatWidth, 24f);

            Widgets.Label(combatRect, combatText);

            DrawUsingPopup(pawn, screenPos);
            DrawCombatPopups(pawn, screenPos);

            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
        }

        private static void DrawMinimalUsingState(
            Pawn pawn,
            bool usingRimKata)
        {
            Vector2 screenPos = pawn.DrawPos.MapToUIPosition();
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Rect usingRect = new Rect(
                screenPos.x - 20f,
                screenPos.y - 57f,
                40f,
                24f);
            Widgets.Label(usingRect, usingRimKata ? "UT" : "UF");
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
        }

        private static Mesh CreateSearchMesh()
        {
            Mesh mesh = new Mesh
            {
                name = "RimKata progressive search cells",
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };
            mesh.MarkDynamic();
            return mesh;
        }

        internal static void DrawSearchPulses(Map map)
        {
            if (!Prefs.DevMode
                || !SearchRangeEnabled
                || map == null
                || map.Disposed)
            {
                return;
            }

            int currentTick = Find.TickManager?.TicksGame ?? -1;
            if (searchMeshMap != map || searchMeshTick != currentTick)
            {
                searchMeshMap = map;
                searchMeshTick = currentTick;
                SearchCells.Clear();
                UniqueSearchCells.Clear();
                PruneActualSearchRings(currentTick);
                for (int i = 0; i < ActualSearchRings.Count; i++)
                {
                    ActualSearchRing ring = ActualSearchRings[i];
                    if (ring.map != map)
                    {
                        continue;
                    }

                    AppendSearchRingCells(
                        map,
                        ring.origin,
                        ring.innerRadius,
                        ring.outerRadius);
                }

                BuildSearchMesh(SearchMesh, SearchCells);
            }

            if (SearchCells.Count == 0)
            {
                return;
            }

            Graphics.DrawMesh(
                SearchMesh,
                Matrix4x4.identity,
                SearchCellMaterial,
                0);
        }

        private static void PruneActualSearchRings(int currentTick)
        {
            for (int i = ActualSearchRings.Count - 1; i >= 0; i--)
            {
                if (ActualSearchRings[i].tick != currentTick)
                {
                    ActualSearchRings.RemoveAt(i);
                }
            }
        }

        private static void AppendSearchRingCells(
            Map map,
            IntVec3 center,
            float innerRadius,
            float outerRadius)
        {
            float innerSquared = innerRadius < 0f
                ? -1f
                : innerRadius * innerRadius;
            float outerSquared = outerRadius * outerRadius;
            if (outerRadius <= GenRadial.MaxRadialPatternRadius)
            {
                int startIndex = innerRadius < 0f
                    ? 0
                    : GenRadial.NumCellsInRadius(innerRadius);
                int endIndex = GenRadial.NumCellsInRadius(outerRadius);
                for (int i = startIndex; i < endIndex; i++)
                {
                    IntVec3 offset = GenRadial.RadialPattern[i];
                    int distanceSquared = offset.LengthHorizontalSquared;
                    IntVec3 cell = center + offset;
                    if (distanceSquared > innerSquared
                        && distanceSquared <= outerSquared
                        && cell.InBounds(map)
                        && UniqueSearchCells.Add(cell))
                    {
                        SearchCells.Add(cell);
                    }
                }

                return;
            }

            int extent = Mathf.CeilToInt(outerRadius);
            int minX = Mathf.Max(0, center.x - extent);
            int maxX = Mathf.Min(map.Size.x - 1, center.x + extent);
            int minZ = Mathf.Max(0, center.z - extent);
            int maxZ = Mathf.Min(map.Size.z - 1, center.z + extent);
            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    IntVec3 cell = new IntVec3(x, 0, z);
                    int distanceSquared = center.DistanceToSquared(cell);
                    if (distanceSquared > innerSquared
                        && distanceSquared <= outerSquared
                        && UniqueSearchCells.Add(cell))
                    {
                        SearchCells.Add(cell);
                    }
                }
            }
        }

        private static void BuildSearchMesh(
            Mesh mesh,
            List<IntVec3> cells)
        {
            SearchMeshVertices.Clear();
            SearchMeshTriangles.Clear();
            const float halfSize = 0.48f;
            for (int i = 0; i < cells.Count; i++)
            {
                Vector3 center = cells[i].ToVector3ShiftedWithAltitude(
                    AltitudeLayer.MetaOverlays);
                int vertex = SearchMeshVertices.Count;
                SearchMeshVertices.Add(center + new Vector3(-halfSize, 0f, -halfSize));
                SearchMeshVertices.Add(center + new Vector3(-halfSize, 0f, halfSize));
                SearchMeshVertices.Add(center + new Vector3(halfSize, 0f, halfSize));
                SearchMeshVertices.Add(center + new Vector3(halfSize, 0f, -halfSize));
                SearchMeshTriangles.Add(vertex);
                SearchMeshTriangles.Add(vertex + 1);
                SearchMeshTriangles.Add(vertex + 2);
                SearchMeshTriangles.Add(vertex);
                SearchMeshTriangles.Add(vertex + 2);
                SearchMeshTriangles.Add(vertex + 3);
            }

            mesh.Clear();
            mesh.SetVertices(SearchMeshVertices);
            mesh.SetTriangles(SearchMeshTriangles, 0);
            mesh.RecalculateBounds();
        }

        private static void DrawUsingPopup(
            Pawn pawn,
            Vector2 basePosition)
        {
            if (!usingPopups.TryGetValue(pawn, out UPopup popup))
            {
                return;
            }

            int elapsed = Find.TickManager.TicksGame - popup.startTick;

            if (elapsed < 0 || elapsed >= EntryPopupTicks)
            {
                usingPopups.Remove(pawn);
                return;
            }

            float progress = elapsed / (float)EntryPopupTicks;

            float offsetX = -55f - 30f * progress;
            float offsetY = -57f - 32f * progress;

            Rect popupRect = new Rect(basePosition.x + offsetX, basePosition.y + offsetY, 40f, 24f);

            Widgets.Label(popupRect, popup.state ? "UT" : "UF");
        }

        private static void DrawCombatPopups(
            Pawn pawn,
            Vector2 basePosition)
        {
            if (!combatPopups.TryGetValue(pawn, out List<RPopup> popups))
            {
                return;
            }

            int currentTick = Find.TickManager.TicksGame;

            for (int i = popups.Count - 1; i >= 0; i--)
            {
                RPopup popup = popups[i];
                int elapsed = currentTick - popup.startTick;

                if (elapsed < 0 || elapsed >= EntryPopupTicks)
                {
                    popups.RemoveAt(i);
                    continue;
                }

                float progress = elapsed / (float)EntryPopupTicks;

                float offsetX = 35f + 35f * progress;
                float offsetY = -57f - 32f * progress;

                Rect popupRect = new Rect(basePosition.x + offsetX, basePosition.y + offsetY, 40f, 24f);

                Widgets.Label(popupRect, popup.state ? "RT" : "RF");
            }

            if (popups.Count == 0)
            {
                combatPopups.Remove(pawn);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_RimKataDebugHUDGizmo
    {
        public static IEnumerable<Gizmo> Postfix(
            IEnumerable<Gizmo> __result,
            Pawn __instance)
        {
            foreach (Gizmo gizmo in __result)
            {
                yield return gizmo;
            }

            if (!Prefs.DevMode
                || __instance == null
                || !__instance.Spawned
                || !RimKataEligibilityCache.DebugHasRawAccessSource(__instance))
            {
                yield break;
            }

            Pawn firstRimKataPawn =
                Find.Selector.SelectedObjects
                    .OfType<Pawn>()
                    .FirstOrDefault(
                        p => p != null
                            && p.Spawned
                            && RimKataEligibilityCache.DebugHasRawAccessSource(p));

            if (firstRimKataPawn != __instance)
            {
                yield break;
            }

            yield return new Command_Action
            {
                defaultLabel = "KRWF_RimKata_DebugMenuLabel".Translate(),
                defaultDesc = "KRWF_RimKata_DebugMenuDesc".Translate(),
                action = OpenDebugMenu
            };
        }

        private static void OpenDebugMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption(
                    ToggleOptionLabel(
                        "KRWF_RimKata_DebugHUDOption",
                        RimKataDebugHUD.Enabled),
                    delegate
                    {
                        RimKataDebugHUD.SetHudEnabled(
                            !RimKataDebugHUD.Enabled);
                    }),
                new FloatMenuOption(
                    ToggleOptionLabel(
                        "KRWF_RimKata_DebugSearchRangeOption",
                        RimKataDebugHUD.SearchRangeEnabled),
                    delegate
                    {
                        RimKataDebugHUD.SetSearchRangeEnabled(
                            !RimKataDebugHUD.SearchRangeEnabled);
                    })
            };

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static string ToggleOptionLabel(
            string translationKey,
            bool enabled)
        {
            return (enabled ? "[x] " : "[ ] ")
                + translationKey.Translate();
        }
    }
}
