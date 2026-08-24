using HarmonyLib;
using UnityEngine;
using Verse;

namespace KRWF.RimKata
{
    public abstract class Stance_RimKataLeaningAim : Stance_Busy
    {
        private bool leanCacheInitialized;
        private IntVec3 lastLeanRoot = IntVec3.Invalid;
        private IntVec3 lastLeanTargetCell = IntVec3.Invalid;
        private Thing lastLeanTargetThing;
        private Verb lastLeanVerb;
        private bool lastLeanTargetUsable;

        public override bool StanceBusy => false;

        protected Stance_RimKataLeaningAim()
        {
        }

        protected Stance_RimKataLeaningAim(
            int ticks,
            LocalTargetInfo focusTarget,
            Verb verb)
            : base(ticks, focusTarget, verb)
        {
        }

        public override void StanceTick()
        {
            RefreshLeanNow();
            base.StanceTick();
        }

        public void RefreshLeanNow()
        {
            Pawn pawn = Pawn;
            IntVec3 root = pawn?.Spawned == true ? pawn.Position : IntVec3.Invalid;
            IntVec3 targetCell = focusTarg.IsValid ? focusTarg.Cell : IntVec3.Invalid;
            Thing targetThing = focusTarg.HasThing ? focusTarg.Thing : null;
            bool targetUsable = targetThing == null || (targetThing.Spawned && targetThing.Map == pawn?.Map);
            bool targetCellChanged = targetThing == null
                && targetCell != lastLeanTargetCell;
            if (leanCacheInitialized
                && root == lastLeanRoot
                && !targetCellChanged
                && targetThing == lastLeanTargetThing
                && verb == lastLeanVerb
                && targetUsable == lastLeanTargetUsable)
            {
                return;
            }

            leanCacheInitialized = true;
            lastLeanRoot = root;
            lastLeanTargetCell = targetCell;
            lastLeanTargetThing = targetThing;
            lastLeanVerb = verb;
            lastLeanTargetUsable = targetUsable;

            if (pawn?.Spawned != true || pawn.Drawer == null)
            {
                return;
            }

            if (!targetUsable
                || verb == null
                || !focusTarg.IsValid
                || !verb.TryFindShootLineFromTo(root, focusTarg, out ShootLine line))
            {
                line = new ShootLine(root, root);
            }

            pawn.Drawer.Notify_WarmingCastAlongLine(line, root);
        }
    }

    [HarmonyPatch(typeof(PawnLeaner), nameof(PawnLeaner.LeanOffset), MethodType.Getter)]
    public static class Patch_PawnLeaner_RimKataMovingAim
    {
        public static void Postfix(Pawn ___pawn, ref Vector3 __result)
        {
            if (!(___pawn?.stances?.curStance is Stance_RimKataLeaningAim)
                || !RimKataDodgeMovementUtility.TryGetCurrentMovementDirection(___pawn, out IntVec3 movementDirection))
            {
                return;
            }

            Vector3 forward = movementDirection.ToVector3();
            if (forward.sqrMagnitude <= 0.001f)
            {
                return;
            }

            forward.Normalize();
            __result -= forward * Vector3.Dot(__result, forward);
        }
    }
}
