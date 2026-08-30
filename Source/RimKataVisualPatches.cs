using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace KRWF.RimKata
{
    public static class RimKataVisualUtility
    {
        public static bool IsCachedWorldVisualUser(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            if (RimKataMod.Settings?.accessRestrictionsDisabled == true)
            {
                return RimKataEligibility.FactionEffectsEnabled(pawn);
            }

            return RimKataEligibilityCache.IsRegisteredUser(pawn)
                && RimKataEligibility.FactionEffectsEnabled(pawn);
        }

        public static bool TryGetCachedWorldLoadout(
            Pawn pawn,
            out ThingWithComps primary,
            out ThingWithComps secondary)
        {
            return TryGetVisualLoadout(
                pawn,
                false,
                out primary,
                out secondary);
        }

        public static bool TryGetUiLoadout(
            Pawn pawn,
            out ThingWithComps primary,
            out ThingWithComps secondary)
        {
            return TryGetVisualLoadout(
                pawn,
                true,
                out primary,
                out secondary);
        }

        public static bool IsSecondaryUsable(
            Pawn pawn,
            ThingWithComps primary,
            ThingWithComps secondary)
        {
            return pawn != null
                && secondary != null
                && RimKataMod.Settings?.secondaryWeaponEnabled != false
                && RimKataEquipmentUtility.IsWeaponEnabled(primary?.def)
                && RimKataGripUtility.GripTypeFor(primary?.def)
                    == RimKataGripType.OneHand;
        }

        public static bool TryGetResponseParticipantLoadout(
            Pawn pawn,
            out ThingWithComps primary,
            out ThingWithComps secondary)
        {
            primary = null;
            secondary = null;
            if (!RimKataResponseVisualParticipantCache
                .TryGetParticipantWeapons(
                    pawn,
                    out ThingWithComps deflectionWeapon,
                    out ThingWithComps responsePoseWeapon))
            {
                return false;
            }

            primary = pawn?.equipment?.Primary;
            if (IsHeldNonPrimary(
                pawn,
                primary,
                responsePoseWeapon))
            {
                secondary = responsePoseWeapon;
            }
            else if (IsHeldNonPrimary(
                pawn,
                primary,
                deflectionWeapon))
            {
                secondary = deflectionWeapon;
            }

            return true;
        }

        public static bool TryGetActiveSnapshot(
            Pawn pawn,
            out RimKataVisualSnapshot snapshot)
        {
            snapshot = default(RimKataVisualSnapshot);
            RimKataMapComponent component = pawn?.Map
                ?.GetComponent<RimKataMapComponent>();
            return component?.TryGetActiveVisualSnapshot(
                pawn,
                out snapshot) == true;
        }

        public static bool TryGetCachedActiveSnapshot(
            Pawn pawn,
            out RimKataVisualSnapshot snapshot)
        {
            snapshot = default(RimKataVisualSnapshot);
            if (!RimKataResponseVisualParticipantCache.IsParticipant(pawn)
                && !RimKataResponseVisualParticipantCache
                    .IsBodyVisualParticipant(pawn))
            {
                return false;
            }

            return TryGetActiveSnapshot(pawn, out snapshot);
        }

        public static RimKataVisualSnapshot SnapshotFor(Pawn pawn)
        {
            return TryGetActiveSnapshot(
                    pawn,
                    out RimKataVisualSnapshot snapshot)
                ? snapshot
                : default(RimKataVisualSnapshot);
        }

        public static Vector3 DrawOffset(RimKataVisualSnapshot snapshot)
        {
            if (!snapshot.visualActive)
            {
                return Vector3.zero;
            }

            float progress = Mathf.Clamp01(snapshot.visualProgress);
            Vector3 rawDirection = snapshot.dodgeDirection.ToVector3();
            Vector3 direction = rawDirection;
            if (direction.sqrMagnitude > 0.01f)
            {
                direction.Normalize();
            }

            if (snapshot.dodgeMovementActive)
            {
                float hop = Mathf.Sin(progress * Mathf.PI);
                return new Vector3(0f, 0f, hop * 0.2f);
            }

            switch (snapshot.visualState)
            {
                case RimKataVisualState.StandardDodge:
                    return direction * (Mathf.Sin(progress * Mathf.PI) * 0.45f);
                case RimKataVisualState.Tumble:
                    return new Vector3(0f, 0.02f, Mathf.Sin(progress * Mathf.PI) * 0.65f);
                default:
                    return Vector3.zero;
            }
        }

        public static bool RequiresDynamicBodyRotation(RimKataVisualSnapshot snapshot)
        {
            return (snapshot.visualActive
                    && (snapshot.visualState == RimKataVisualState.AdditionalDodge
                        || snapshot.visualState == RimKataVisualState.Tumble))
                        || snapshot.closeDodgeActive
                        || (snapshot.responsePoseActive
                            && snapshot.responsePoseLookAtFocus);
        }

        public static bool TryGetResponseFacing(
            Pawn pawn,
            RimKataVisualSnapshot snapshot,
            out Rot4 facing)
        {
            facing = Rot4.Invalid;
            if (!snapshot.responsePoseLookAtFocus
                || !TryGetLiveResponseFocus(
                    pawn,
                    snapshot,
                    out LocalTargetInfo focus))
            {
                return false;
            }

            Thing target = focus.Thing;
            Vector3 direction = target.DrawPos - pawn.DrawPos;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.001f)
            {
                return false;
            }

            facing = Rot4.FromAngleFlat(direction.AngleFlat());
            return facing.IsValid;
        }

        public static bool TryGetLiveResponseFocus(
            Pawn pawn,
            RimKataVisualSnapshot snapshot,
            out LocalTargetInfo focus)
        {
            focus = LocalTargetInfo.Invalid;
            if (pawn?.Map == null
                || !snapshot.responsePoseActive
                || !snapshot.responsePoseFocus.HasThing)
            {
                return false;
            }

            Thing target = snapshot.responsePoseFocus.Thing;
            if (target == null
                || target.Destroyed
                || !target.Spawned
                || target.Map != pawn.Map)
            {
                return false;
            }

            if (target is Pawn targetPawn
                && (targetPawn.Dead
                    || targetPawn.Downed
                    || targetPawn.Crawling
                    || targetPawn.IsPsychologicallyInvisible()))
            {
                return false;
            }

            focus = snapshot.responsePoseFocus;
            return true;
        }

        private static bool TryGetVisualLoadout(
            Pawn pawn,
            bool resolveAccess,
            out ThingWithComps primary,
            out ThingWithComps secondary)
        {
            primary = null;
            secondary = null;
            bool hasAccess = resolveAccess
                ? RimKataEligibility.HasRimKataAccess(pawn)
                : IsCachedWorldVisualUser(pawn);
            if (!hasAccess)
            {
                return false;
            }

            primary = pawn?.equipment?.Primary;
            bool cached = RimKataEligibilityCache
                .TryGetRegisteredSecondaryWeapon(
                    pawn,
                    out secondary);
            if (!cached)
            {
                secondary = RimKataSecondaryWeaponRegistry
                    .CurrentRegistry
                    ?.Get(pawn);
            }

            if (IsHeldSecondary(pawn, primary, secondary))
            {
                return true;
            }

            if (cached && secondary != null)
            {
                secondary = RimKataSecondaryWeaponRegistry
                    .CurrentRegistry
                    ?.Get(pawn);
            }

            if (!IsHeldSecondary(pawn, primary, secondary))
            {
                secondary = null;
            }

            return true;
        }

        private static bool IsHeldSecondary(
            Pawn pawn,
            ThingWithComps primary,
            ThingWithComps secondary)
        {
            return secondary == null
                || IsHeldNonPrimary(pawn, primary, secondary);
        }

        private static bool IsHeldNonPrimary(
            Pawn pawn,
            ThingWithComps primary,
            ThingWithComps weapon)
        {
            return weapon != null
                && !weapon.Destroyed
                && weapon != primary
                && pawn?.equipment?.AllEquipmentListForReading
                    ?.Contains(weapon) == true;
        }

        public static Pawn FindPawnOwner(Thing thing)
        {
            IThingHolder holder = thing?.ParentHolder;
            for (int i = 0; holder != null && i < 5; i++)
            {
                if (holder is Pawn pawn)
                {
                    return pawn;
                }

                holder = holder.ParentHolder;
            }

            return null;
        }
    }

    public struct RimKataCarryDrawContext
    {
        public bool active;
        public Pawn pawn;
        public ThingWithComps primary;
        public ThingWithComps secondary;
        public bool snapshotActive;
        public RimKataVisualSnapshot snapshot;
        public Vector3 drawPos;
    }

    internal static class RimKataCarryDrawUtility
    {
        [ThreadStatic] private static RimKataCarryDrawContext current;

        public static RimKataCarryDrawContext Current => current;

        public static RimKataCarryDrawContext Push(
            ThingWithComps weapon,
            Vector3 drawPos)
        {
            RimKataCarryDrawContext previous = current;
            current = default(RimKataCarryDrawContext);
            RimKataGunReadyDrawContext renderContext =
                RimKataGunReadyDrawUtility.Current;
            if (renderContext.scoped)
            {
                if (renderContext.active
                    && renderContext.primary == weapon)
                {
                    current = new RimKataCarryDrawContext
                    {
                        active = true,
                        pawn = renderContext.pawn,
                        primary = renderContext.primary,
                        secondary = renderContext.secondary,
                        snapshotActive = renderContext.snapshotActive,
                        snapshot = renderContext.snapshot,
                        drawPos = drawPos
                    };
                }

                return previous;
            }

            Pawn pawn = RimKataVisualUtility.FindPawnOwner(weapon);
            bool rimKataUser = RimKataVisualUtility
                .TryGetCachedWorldLoadout(
                    pawn,
                    out ThingWithComps primary,
                    out ThingWithComps rawSecondary);
            bool responseParticipant = RimKataVisualUtility
                .TryGetResponseParticipantLoadout(
                    pawn,
                    out ThingWithComps participantPrimary,
                    out ThingWithComps participantSecondary);
            if (!rimKataUser && responseParticipant)
            {
                primary = participantPrimary;
            }

            if ((!rimKataUser && !responseParticipant) || primary != weapon)
            {
                return previous;
            }

            ThingWithComps secondary = rimKataUser
                ? RimKataVisualUtility.IsSecondaryUsable(
                    pawn,
                    primary,
                    rawSecondary)
                        ? rawSecondary
                        : null
                : participantSecondary;
            RimKataVisualSnapshot snapshot =
                default(RimKataVisualSnapshot);
            bool snapshotActive = (secondary != null || responseParticipant)
                && RimKataVisualUtility.TryGetCachedActiveSnapshot(
                    pawn,
                    out snapshot);
            current = new RimKataCarryDrawContext
            {
                active = true,
                pawn = pawn,
                primary = primary,
                secondary = secondary,
                snapshotActive = snapshotActive,
                snapshot = snapshot,
                drawPos = drawPos
            };
            return previous;
        }

        public static void Pop(RimKataCarryDrawContext previous)
        {
            current = previous;
        }
    }

    [StaticConstructorOnStartup]
    public static class RimKataDualWeaponRenderUtility
    {
        [ThreadStatic] private static bool drawingPair;
        [ThreadStatic] private static bool drawingSecondary;
        [ThreadStatic] private static Vector3 currentEquipmentPivot;
        private static Mesh plane10VFlip;
        private static Mesh plane10UvFlip;

        private const float CombatIndicatorBaseAltitude = 0.2f;
        private const float CombatIndicatorTopAltitude = 0.201f;
        private const float FocusedTargetLineWidth = 0.2f;
        private const float FocusedTargetLineMinimumPixels = 2f;

        private static readonly Material BlackCombatIndicatorMaterial = SolidColorMaterials.SimpleSolidColorMaterial(new Color( 0f, 0f, 0f, 0.3f));
        private static readonly Material BlackTargetLineMaterial =
            MaterialPool.MatFrom(
                GenDraw.LineTexPath,
                ShaderDatabase.Transparent,
                Color.black);

        public static bool DrawingPair => drawingPair;

        public static Mesh Plane10ForContext()
        {
            return drawingSecondary
                ? plane10VFlip ??= CreateVFlippedMesh(MeshPool.plane10)
                : MeshPool.plane10;
        }

        public static Mesh Plane10FlipForContext()
        {
            return drawingSecondary
                ? plane10UvFlip ??= CreateVFlippedMesh(MeshPool.plane10Flip)
                : MeshPool.plane10Flip;
        }

        public static bool TryDrawPair(
            Thing equipment,
            Vector3 originalDrawLoc,
            float originalAimAngle)
        {
            if (drawingPair)
            {
                return false;
            }

            RimKataCarryDrawContext carryContext =
                RimKataCarryDrawUtility.Current;
            Pawn pawn;
            ThingWithComps primary;
            ThingWithComps secondary;
            bool snapshotActive;
            RimKataVisualSnapshot snapshot;
            if (carryContext.active)
            {
                pawn = carryContext.pawn;
                primary = carryContext.primary;
                secondary = carryContext.secondary;
                snapshotActive = carryContext.snapshotActive;
                snapshot = carryContext.snapshot;
            }
            else
            {
                RimKataGunReadyDrawContext renderContext =
                    RimKataGunReadyDrawUtility.Current;
                if (renderContext.scoped)
                {
                    if (!renderContext.active)
                    {
                        return false;
                    }

                    pawn = renderContext.pawn;
                    primary = renderContext.primary;
                    secondary = renderContext.secondary;
                    snapshotActive = renderContext.snapshotActive;
                    snapshot = renderContext.snapshot;
                }
                else
                {
                    pawn = RimKataVisualUtility.FindPawnOwner(equipment);
                    bool rimKataUser = RimKataVisualUtility
                        .TryGetCachedWorldLoadout(
                            pawn,
                            out primary,
                            out ThingWithComps rawSecondary);
                    bool responseParticipant = RimKataVisualUtility
                        .TryGetResponseParticipantLoadout(
                            pawn,
                            out ThingWithComps participantPrimary,
                            out ThingWithComps participantSecondary);
                    if (!rimKataUser && !responseParticipant)
                    {
                        return false;
                    }

                    if (!rimKataUser)
                    {
                        primary = participantPrimary;
                    }

                    secondary = rimKataUser
                        ? RimKataVisualUtility.IsSecondaryUsable(
                            pawn,
                            primary,
                            rawSecondary)
                                ? rawSecondary
                                : null
                        : participantSecondary;
                    if (equipment != primary || secondary == null)
                    {
                        return false;
                    }

                    snapshotActive = RimKataVisualUtility
                        .TryGetCachedActiveSnapshot(
                            pawn,
                            out snapshot);
                }
            }

            if (equipment != primary || secondary == null)
            {
                return false;
            }

            Vector3 equipmentPivot = ResolveEquipmentPivot(
                pawn,
                primary,
                originalDrawLoc,
                originalAimAngle);
            drawingPair = true;
            currentEquipmentPivot = equipmentPivot;
            try
            {
                DrawWeapon(
                    pawn,
                    primary,
                    primary,
                    originalDrawLoc,
                    equipmentPivot,
                    originalAimAngle,
                    false,
                    snapshotActive,
                    snapshot);
                DrawWeapon(
                    pawn,
                    primary,
                    secondary,
                    originalDrawLoc,
                    equipmentPivot,
                    originalAimAngle,
                    true,
                    snapshotActive,
                    snapshot);
            }
            finally
            {
                drawingSecondary = false;
                currentEquipmentPivot = default(Vector3);
                drawingPair = false;
            }

            return true;
        }

        public static void DrawCombatIndicators(Pawn pawn)
        {
            if (pawn?.Spawned != true
                || !Find.Selector.IsSelected(pawn)
                || !RimKataVisualUtility.TryGetUiLoadout(
                    pawn,
                    out ThingWithComps primary,
                    out ThingWithComps rawSecondary))
            {
                return;
            }

            ThingWithComps secondary =
                RimKataVisualUtility.IsSecondaryUsable(
                    pawn,
                    primary,
                    rawSecondary)
                        ? rawSecondary
                        : null;

            if (!DrawFocusedCloseTargetLine(pawn))
            {
                DrawFocusedTargetLine(
                    pawn,
                    primary);
                DrawFocusedTargetLine(
                    pawn,
                    secondary);
            }

            bool primaryVisible = TryGetCombatIndicatorData(pawn, primary, out RimKataWeaponVisualData primaryVisual, out Verb primaryVerb, out int primaryRemaining);
            bool secondaryVisible = TryGetCombatIndicatorData( pawn, secondary, out RimKataWeaponVisualData secondaryVisual, out Verb secondaryVerb, out int secondaryRemaining);
            if (!primaryVisible
                && !secondaryVisible)
            {
                return;
            }

            if (primaryVisible
                && secondaryVisible)
            {
                if (primaryRemaining <= secondaryRemaining)
                {
                    DrawCombatIndicatorForWeapon(pawn, secondaryVisual, secondaryVerb, CombatIndicatorBaseAltitude);
                    DrawCombatIndicatorForWeapon(pawn, primaryVisual, primaryVerb, CombatIndicatorTopAltitude);
                }
                else
                {
                    DrawCombatIndicatorForWeapon(pawn, primaryVisual, primaryVerb, CombatIndicatorBaseAltitude);
                    DrawCombatIndicatorForWeapon(pawn, secondaryVisual, secondaryVerb, CombatIndicatorTopAltitude);
                }

                return;
            }

            if (primaryVisible)
            {
                DrawCombatIndicatorForWeapon(pawn, primaryVisual, primaryVerb, CombatIndicatorBaseAltitude);

                return;
            }

            DrawCombatIndicatorForWeapon(pawn, secondaryVisual, secondaryVerb, CombatIndicatorBaseAltitude);
        }

        private static bool DrawFocusedCloseTargetLine(Pawn pawn)
        {
            if (!RimKataDualWeaponController.TryGetAttackGizmoCloseTarget(
                pawn,
                out Thing target))
            {
                return false;
            }

            Vector3 start = pawn.Position.ToVector3Shifted();
            Vector3 end = new LocalTargetInfo(target).CenterVector3;
            end.y = start.y;
            float altitude = Altitudes.AltitudeFor(
                AltitudeLayer.MetaOverlays);
            GenDraw.DrawLineBetween(
                start,
                end,
                altitude,
                BlackTargetLineMaterial,
                FocusedTargetLineWidthForCamera());
            return true;
        }

        private static void DrawFocusedTargetLine(
            Pawn pawn,
            ThingWithComps weapon)
        {
            if (weapon == null
                || !RimKataDualWeaponController.TryGetFocusedWeaponTarget(
                    pawn,
                    weapon,
                    out Thing target,
                    out bool fromAttackGizmo))
            {
                return;
            }

            Vector3 start = pawn.Position.ToVector3Shifted();
            Vector3 end = new LocalTargetInfo(target).CenterVector3;
            end.y = start.y;
            float altitude = Altitudes.AltitudeFor(
                AltitudeLayer.MetaOverlays);
            if (fromAttackGizmo)
            {
                GenDraw.DrawLineBetween(
                    start,
                    end,
                    altitude,
                    BlackTargetLineMaterial,
                    FocusedTargetLineWidthForCamera());
                return;
            }

            GenDraw.DrawLineBetween(start, end, altitude);
        }

        private static float FocusedTargetLineWidthForCamera()
        {
            Camera camera = Find.Camera;
            if (camera == null
                || !camera.orthographic
                || camera.pixelHeight <= 0
                || camera.orthographicSize <= 0f)
            {
                return FocusedTargetLineWidth;
            }

            float pixelsPerWorldUnit = camera.pixelHeight
                / (camera.orthographicSize * 2f);
            return Mathf.Max(
                FocusedTargetLineWidth,
                FocusedTargetLineMinimumPixels / pixelsPerWorldUnit);
        }

        private static void DrawWeapon(
            Pawn pawn,
            ThingWithComps primary,
            ThingWithComps weapon,
            Vector3 primaryDrawLoc,
            Vector3 equipmentPivot,
            float fallbackAngle,
            bool secondary,
            bool snapshotActive,
            RimKataVisualSnapshot snapshot)
        {
            float aimAngle = fallbackAngle;
            Vector3 drawLoc = primaryDrawLoc;
            LocalTargetInfo responseFocus = LocalTargetInfo.Invalid;
            bool responseTarget = snapshotActive
                && snapshot.responsePoseWeapon == weapon
                && RimKataVisualUtility.TryGetLiveResponseFocus(
                    pawn,
                    snapshot,
                    out responseFocus);
            RimKataWeaponVisualData visual = default(RimKataWeaponVisualData);
            bool cycleTarget = RimKataDualWeaponController.TryGetVisualData(
                    pawn,
                    weapon,
                    out visual)
                && visual.target.IsValid;
            bool hasOwnTarget = cycleTarget || responseTarget;
            if (hasOwnTarget)
            {
                LocalTargetInfo target = responseTarget
                    ? responseFocus
                    : visual.target;

                aimAngle = AngleToTarget(pawn, target, fallbackAngle);
                drawLoc = EquipmentCenter(
                    pawn,
                    weapon,
                    equipmentPivot,
                    aimAngle);
            }
            else if (!secondary
                && RimKataDualWeaponController.TryGetNextAim(pawn, out ThingWithComps activeWeapon, out LocalTargetInfo _)
                && activeWeapon != weapon)
            {
                aimAngle = pawn.Rotation.AsAngle;
                drawLoc = EquipmentCenter(
                    pawn,
                    weapon,
                    equipmentPivot,
                    aimAngle);
            }

            bool sharedFallbackAim = false;
            if (secondary)
            {
                if (!hasOwnTarget)
                {
                    RimKataCarryDrawContext carryContext =
                        RimKataCarryDrawUtility.Current;
                    if (!carryContext.active)
                    {
                        sharedFallbackAim = true;
                        aimAngle = fallbackAngle;
                        drawLoc = EquipmentCenter(
                            pawn,
                            weapon,
                            equipmentPivot,
                            aimAngle);
                    }
                    else
                    {
                        drawLoc = SymmetricIdleSecondaryLoc(
                            pawn,
                            primary,
                            weapon,
                            primaryDrawLoc,
                            fallbackAngle,
                            out aimAngle);
                    }
                }

                drawLoc.y -= 0.001f;
            }

            bool horizontalIdleFacing = pawn.Rotation == Rot4.East || pawn.Rotation == Rot4.West;
            bool secondaryIdle = secondary && !hasOwnTarget && !sharedFallbackAim;

            drawingSecondary = secondary && (!secondaryIdle || horizontalIdleFacing);
            PawnRenderUtility.DrawEquipmentAiming(weapon, drawLoc, aimAngle);

            drawingSecondary = false;
        }

        private static Vector3 SymmetricIdleSecondaryLoc(
            Pawn pawn,
            ThingWithComps primary,
            ThingWithComps secondary,
            Vector3 originalPrimaryLoc,
            float originalPrimaryAngle,
            out float secondaryAngle)
        {
            secondaryAngle = Mathf.Repeat(
                2f * pawn.Rotation.AsAngle - originalPrimaryAngle,
                360f);
            if (primary == null || secondary == null)
            {
                return originalPrimaryLoc;
            }

            RimKataCarryDrawContext carryContext =
                RimKataCarryDrawUtility.Current;
            if (carryContext.active
                && carryContext.pawn == pawn
                && carryContext.primary == primary)
            {
                Vector3 primaryOffset = originalPrimaryLoc
                    - carryContext.drawPos;
                return carryContext.drawPos
                    + MirrorOffsetAcrossFacing(
                        primaryOffset,
                        pawn.Rotation.AsAngle);
            }

            float factor = pawn.ageTracker?.CurLifeStage
                ?.equipmentDrawDistanceFactor ?? 1f;
            Vector3 carryPivot = originalPrimaryLoc
                - EquipmentRadial(primary, originalPrimaryAngle, factor);
            Vector3 secondaryLoc = carryPivot
                + EquipmentRadial(secondary, secondaryAngle, factor);
            secondaryLoc.y = originalPrimaryLoc.y;
            return secondaryLoc;
        }

        private static Vector3 MirrorOffsetAcrossFacing(
            Vector3 offset,
            float facingAxis)
        {
            Vector3 local = offset.RotatedBy(-facingAxis);
            local.x = -local.x;
            return local.RotatedBy(facingAxis);
        }

        private static Vector3 EquipmentRadial(
            ThingWithComps weapon,
            float angle,
            float distanceFactor)
        {
            return new Vector3(
                0f,
                0f,
                0.4f + weapon.def.equippedDistanceOffset)
                .RotatedBy(angle)
                * distanceFactor;
        }

        private static bool TryGetCombatIndicatorData(
            Pawn pawn,
            ThingWithComps weapon,
            out RimKataWeaponVisualData visual,
            out Verb verb,
            out int remainingTicks)
        {
            visual = default(RimKataWeaponVisualData);
            verb = null;
            remainingTicks = 0;
            if (weapon == null
                || !RimKataDualWeaponController.TryGetIndicatorVisualData(
                    pawn,
                    weapon,
                    out visual,
                    out bool _))
            {
                return false;
            }

            bool warming =
                visual.warming
                && visual.warmupTicksRemaining > 0
                && visual.warmupTotalTicks > 0;

            bool cooling = visual.cooldownTicksRemaining > 0;

            if (!warming
                && !cooling)
            {
                return false;
            }

            verb = RimKataWeaponSlotUtility.CombatVerb(pawn, weapon);

            if (verb == null)
            {
                return false;
            }

            if (warming)
            {
                remainingTicks = visual.warmupTicksRemaining;

                return true;
            }

            remainingTicks = visual.cooldownTicksRemaining;

            return true;
        }

        private static void DrawCombatIndicatorForWeapon(
            Pawn pawn,
            RimKataWeaponVisualData visual,
            Verb verb,
            float altitudeOffset)
        {
            if (pawn == null
                || verb == null)
            {
                return;
            }

            bool warming =
                visual.warming
                && visual.warmupTicksRemaining > 0
                && visual.warmupTotalTicks > 0;

            bool cooling = visual.cooldownTicksRemaining > 0;
            if (warming
                && RimKataDualWeaponController
                    .ShouldPauseFireForDodge(pawn))
            {
                return;
            }

            if (verb.IsMeleeAttack)
            {
                if (warming)
                {
                    float radius = Mathf.Min(0.5f, visual.warmupTicksRemaining * 0.002f);
                    DrawBlackCooldownCircle(pawn.Drawer.DrawPos + new Vector3( 0f, altitudeOffset, 0f), radius);
                }
                else if (cooling)
                {
                    float radius = Mathf.Min(0.5f, visual.cooldownTicksRemaining * 0.002f);
                    GenDraw.DrawCooldownCircle(pawn.Drawer.DrawPos + new Vector3( 0f, altitudeOffset, 0f), radius);
                }

                return;
            }

            if (warming
                && visual.target.IsValid
                && verb.verbProps?.drawAimPie == true)
            {
                int degrees = Mathf.Clamp(visual.warmupTicksRemaining, 1, 360);
                DrawAimPie(pawn, visual.target, degrees, altitudeOffset);

                return;
            }

            if (cooling
                && visual.target.IsValid
                && verb.verbProps?.drawAimPie == true)
            {
                int degrees = Mathf.Clamp(visual.cooldownTicksRemaining, 1, 360);
                DrawBlackAimPie(pawn, visual.target, degrees, altitudeOffset);
            }
        }

        private static void DrawBlackCooldownCircle(
            Vector3 center,
            float radius)
        {
            if (radius <= 0f)
            {
                return;
            }

            Vector3 scale = new Vector3(radius, 1f, radius);
            Matrix4x4 matrix = default(Matrix4x4);
            matrix.SetTRS(center, Quaternion.identity, scale);
            Graphics.DrawMesh(MeshPool.circle, matrix, BlackCombatIndicatorMaterial, 0);
        }

        private static void DrawAimPie(
            Pawn pawn,
            LocalTargetInfo target,
            int degreesWide,
            float altitudeOffset)
        {
            if (pawn == null
                || !target.IsValid
                || degreesWide <= 0)
            {
                return;
            }

            Vector3 center = pawn.DrawPos
                + new Vector3(0f, altitudeOffset, 0f);
            GenDraw.DrawAimPieRaw(
                center,
                AimPieFacing(pawn, target),
                Mathf.Min(360, degreesWide));
        }

        private static void DrawBlackAimPie(
            Pawn pawn,
            LocalTargetInfo target,
            int degreesWide,
            float altitudeOffset)
        {
            if (pawn == null
                || !target.IsValid
                || degreesWide <= 0)
            {
                return;
            }

            degreesWide = Mathf.Min(360, degreesWide);
            float facing = AimPieFacing(pawn, target);

            Vector3 center = pawn.DrawPos + new Vector3( 0f, altitudeOffset, 0f);
            center += Quaternion.AngleAxis( facing, Vector3.up) * Vector3.forward * 0.8f;
            Quaternion rotation = Quaternion.AngleAxis( facing + degreesWide / 2f - 90f, Vector3.up);
            Graphics.DrawMesh(MeshPool.pies[degreesWide], center, rotation, BlackCombatIndicatorMaterial, 0);
        }

        private static float AimPieFacing(
            Pawn pawn,
            LocalTargetInfo target)
        {
            Vector3 targetPosition = target.HasThing
                && target.Thing.Spawned
                    ? target.Thing.DrawPos
                    : target.Cell.ToVector3Shifted();
            Vector3 direction = targetPosition - pawn.DrawPos;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.001f
                ? direction.AngleFlat()
                : 0f;
        }

        public static bool ClaimsVanillaCombatCooldown(
            Stance_Cooldown cooldown)
        {
            Pawn pawn = cooldown?.stanceTracker?.pawn;
            Verb verb = cooldown?.verb;
            ThingWithComps weapon = verb?.EquipmentSource as ThingWithComps;
            if (pawn?.Spawned != true
                || !Find.Selector.IsSelected(pawn)
                || !RimKataAutomaticRangeVisualUtility
                    .CanDrawAutomaticSearchRange(pawn)
                || verb == null
                || cooldown.ticksLeft <= 0
                || weapon == null)
            {
                return false;
            }

            if (!RimKataVisualUtility.TryGetUiLoadout(
                    pawn,
                    out ThingWithComps primary,
                    out ThingWithComps rawSecondary))
            {
                return false;
            }

            ThingWithComps secondary =
                RimKataVisualUtility.IsSecondaryUsable(
                    pawn,
                    primary,
                    rawSecondary)
                        ? rawSecondary
                        : null;
            if (weapon != primary && weapon != secondary)
            {
                return false;
            }

            if (verb.IsMeleeAttack)
            {
                return RimKataDualWeaponController.TryGetVisualData(
                        pawn,
                        weapon,
                        out RimKataWeaponVisualData meleeVisual)
                    && meleeVisual.cooldownTicksRemaining > 0;
            }

            if (verb.verbProps?.drawAimPie != true
                || !cooldown.focusTarg.IsValid)
            {
                return false;
            }

            return RimKataDualWeaponController.TryGetIndicatorVisualData(
                    pawn,
                    weapon,
                    out RimKataWeaponVisualData _,
                    out bool claimed)
                && claimed;
        }

        internal static bool TryGetCurrentEquipmentPivot(
            out Vector3 equipmentPivot)
        {
            equipmentPivot = currentEquipmentPivot;
            return drawingPair;
        }

        internal static Vector3 ResolveEquipmentPivot(
            Pawn pawn,
            ThingWithComps primary,
            Vector3 originalDrawLoc,
            float originalAimAngle)
        {
            RimKataCarryDrawContext carryContext =
                RimKataCarryDrawUtility.Current;
            if (carryContext.active
                && carryContext.pawn == pawn
                && carryContext.primary == primary)
            {
                return carryContext.drawPos;
            }

            if (pawn == null || primary == null)
            {
                return originalDrawLoc;
            }

            float distanceFactor = pawn.ageTracker?.CurLifeStage
                ?.equipmentDrawDistanceFactor ?? 1f;
            return originalDrawLoc
                - EquipmentRadial(
                    primary,
                    originalAimAngle,
                    distanceFactor);
        }

        private static Vector3 EquipmentCenter(
            Pawn pawn,
            ThingWithComps weapon,
            Vector3 equipmentPivot,
            float aimAngle)
        {
            float distanceFactor = pawn.ageTracker?.CurLifeStage
                ?.equipmentDrawDistanceFactor ?? 1f;
            return equipmentPivot
                + EquipmentRadial(weapon, aimAngle, distanceFactor);
        }

        private static float AngleToTarget(Pawn pawn, LocalTargetInfo target, float fallback)
        {
            Vector3 targetPosition = target.HasThing && target.Thing.Spawned
                ? target.Thing.DrawPos
                : target.Cell.ToVector3Shifted();
            Vector3 aim = targetPosition - pawn.DrawPos;
            return aim.sqrMagnitude > 0.001f ? aim.AngleFlat() : fallback;
        }

        private static Mesh CreateVFlippedMesh(Mesh source)
        {
            Mesh mesh = UnityEngine.Object.Instantiate(source);
            Vector2[] uv = mesh.uv;
            for (int i = 0; i < uv.Length; i++)
            {
                uv[i].y = 1f - uv[i].y;
            }

            mesh.uv = uv;
            return mesh;
        }
    }

    public static class RimKataAutomaticRangeVisualUtility
    {
        private const float DualMeleeSearchRange = 1.42f;
        private const float RangeRingAltitudeOffset = -0.0001f;
        private const int RangeRingRenderQueue = 2899;
        private static readonly List<IntVec3> RingCells = new List<IntVec3>();

        public static bool CanDrawAutomaticSearchRange(Pawn pawn)
        {
            return RimKataEligibility.HasRimKataAccess(pawn);
        }

        public static void DrawAutomaticSearchRange(Pawn pawn)
        {
            if (pawn?.Map == null)
            {
                return;
            }

            bool hasAccess = RimKataVisualUtility.TryGetUiLoadout(
                pawn,
                out ThingWithComps primary,
                out ThingWithComps rawSecondary);
            if (!hasAccess)
            {
                primary = RimKataWeaponSlotUtility.PrimaryWeapon(pawn);
                rawSecondary = RimKataWeaponSlotUtility.SecondaryWeapon(pawn);
            }

            ThingWithComps usableSecondary = hasAccess
                && RimKataVisualUtility.IsSecondaryUsable(
                    pawn,
                    primary,
                    rawSecondary)
                        ? rawSecondary
                        : null;
            float radius = MaximumAutomaticSearchVisualRange(
                pawn,
                primary,
                usableSecondary);
            if (radius <= 0f
                && HasDualMeleeLoadout(primary, rawSecondary))
            {
                radius = DualMeleeSearchRange;
            }

            if (radius <= 0
                || radius > GenRadial.MaxRadialPatternRadius)
            {
                return;
            }

            RingCells.Clear();

            int cellCount = GenRadial.NumCellsInRadius(radius);

            for (int i = 0;
                 i < cellCount;
                 i++)
            {
                IntVec3 cell = pawn.Position + GenRadial.RadialPattern[i];

                RingCells.Add(cell);
            }

            GenDraw.DrawFieldEdges(RingCells, Color.black, RangeRingAltitudeOffset, null, RangeRingRenderQueue);
        }

        public static void DrawLongestRangedWeaponRange(Pawn pawn)
        {
            Verb verb = LongestRangedWeaponVerb(pawn);
            verb?.verbProps?.DrawRadiusRing(pawn.Position, verb);
        }

        private static Verb LongestRangedWeaponVerb(Pawn pawn)
        {
            if (!RimKataVisualUtility.TryGetUiLoadout(
                    pawn,
                    out ThingWithComps primary,
                    out ThingWithComps rawSecondary))
            {
                return FirstVanillaRangedCommandVerb(pawn);
            }

            ThingWithComps secondary =
                RimKataVisualUtility.IsSecondaryUsable(
                    pawn,
                    primary,
                    rawSecondary)
                        ? rawSecondary
                        : null;
            if (secondary == null)
            {
                return FirstVanillaRangedCommandVerb(pawn);
            }

            Verb longest = StandardCommandVerb(
                primary);
            Verb secondaryVerb = StandardCommandVerb(secondary);
            if (secondaryVerb?.IsMeleeAttack == false
                && (longest == null
                    || secondaryVerb.EffectiveRange > longest.EffectiveRange))
            {
                longest = secondaryVerb;
            }

            return longest;
        }

        private static Verb FirstVanillaRangedCommandVerb(Pawn pawn)
        {
            List<ThingWithComps> equipment =
                pawn?.equipment?.AllEquipmentListForReading;
            if (equipment == null)
            {
                return null;
            }

            for (int i = 0; i < equipment.Count; i++)
            {
                ThingWithComps weapon = equipment[i];
                if (weapon?.def?.IsRangedWeapon != true)
                {
                    continue;
                }

                return StandardCommandVerb(weapon);
            }

            return null;
        }

        private static Verb StandardCommandVerb(ThingWithComps weapon)
        {
            List<Verb> verbs = weapon?.TryGetComp<CompEquippable>()?.AllVerbs;
            if (verbs == null)
            {
                return null;
            }

            for (int i = 0; i < verbs.Count; i++)
            {
                Verb verb = verbs[i];
                if (verb?.verbProps?.hasStandardCommand == true
                    && !verb.IsMeleeAttack)
                {
                    return verb;
                }
            }

            return null;
        }

        private static float MaximumAutomaticSearchVisualRange(
            Pawn pawn,
            ThingWithComps primary,
            ThingWithComps secondary)
        {
            return Mathf.Max(
                AutomaticSearchVisualRange(
                    RimKataWeaponSlotUtility.CombatVerb(pawn, primary)),
                AutomaticSearchVisualRange(
                    RimKataWeaponSlotUtility.CombatVerb(pawn, secondary)));
        }

        private static float AutomaticSearchVisualRange(Verb verb)
        {
            if (verb == null || verb.IsMeleeAttack)
            {
                return 0f;
            }

            return Mathf.Max(
                0f,
                RimKataRangeUtility.ResolveCandidateRange(verb));
        }

        public static bool HasDualMeleeLoadout(Pawn pawn)
        {
            if (!RimKataVisualUtility.TryGetUiLoadout(
                    pawn,
                    out ThingWithComps primary,
                    out ThingWithComps secondary))
            {
                primary = RimKataWeaponSlotUtility.PrimaryWeapon(pawn);
                secondary = RimKataWeaponSlotUtility.SecondaryWeapon(pawn);
            }

            return HasDualMeleeLoadout(primary, secondary);
        }

        private static bool HasDualMeleeLoadout(
            ThingWithComps primary,
            ThingWithComps secondary)
        {
            return primary?.def?.IsMeleeWeapon == true
                && secondary?.def?.IsMeleeWeapon == true;
        }
    }

    [HarmonyPatch(typeof(PawnAttackGizmoUtility), "GetMeleeAttackGizmo")]
    public static class Patch_PawnAttackGizmoUtility_RimKataMeleeRange
    {
        private static readonly Action<LocalTargetInfo> DrawSelectedDualMeleeRangesAction = DrawSelectedDualMeleeRanges;

        public static void Postfix(Pawn pawn, ref Gizmo __result)
        {
            if (!(__result is Command_Target command))
            {
                return;
            }

            Action<LocalTargetInfo> originalAction = command.action;
            if (originalAction != null)
            {
                command.action = target =>
                    RimKataAttackGizmoTargetContext.Invoke(
                        originalAction,
                        target);
            }

            if (command.onUpdate == null)
            {
                command.onUpdate = DrawSelectedDualMeleeRangesAction;
            }
            else if (command.onUpdate != DrawSelectedDualMeleeRangesAction)
            {
                command.onUpdate += DrawSelectedDualMeleeRangesAction;
            }
        }

        private static void DrawSelectedDualMeleeRanges(LocalTargetInfo _)
        {
            List<Pawn> selectedPawns = Find.Selector?.SelectedPawns;
            if (selectedPawns == null)
            {
                return;
            }

            for (int i = 0; i < selectedPawns.Count; i++)
            {
                Pawn pawn = selectedPawns[i];
                if (pawn?.Spawned != true
                    || !RimKataAutomaticRangeVisualUtility.CanDrawAutomaticSearchRange(pawn))
                {
                    continue;
                }

                RimKataAutomaticRangeVisualUtility.DrawAutomaticSearchRange(pawn);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_StanceTracker), nameof(Pawn_StanceTracker.StanceTrackerDraw))]
    public static class Patch_PawnStanceTracker_RimKataCombatIndicators
    {
        public static void Postfix(Pawn ___pawn)
        {
            RimKataDualWeaponRenderUtility.DrawCombatIndicators(___pawn);
        }
    }

    [HarmonyPatch(
        typeof(Stance_Cooldown),
        nameof(Stance_Cooldown.StanceDraw))]
    public static class Patch_StanceCooldown_RimKataRangedIndicator
    {
        public static bool Prefix(Stance_Cooldown __instance)
        {
            return !RimKataDualWeaponRenderUtility
                .ClaimsVanillaCombatCooldown(__instance);
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.DynamicDrawPhaseAt))]
    public static class Patch_PawnRenderer_RimKataDodgeOffset
    {
        public static void Prefix(
            Pawn ___pawn,
            DrawPhase phase,
            ref Vector3 drawLoc,
            ref Rot4? rotOverride)
        {
            if (phase == DrawPhase.EnsureInitialized)
            {
                return;
            }

            if (!RimKataVisualUtility.TryGetCachedActiveSnapshot(
                    ___pawn,
                    out RimKataVisualSnapshot snapshot))
            {
                return;
            }

            drawLoc += RimKataVisualUtility.DrawOffset(snapshot);
            if (snapshot.dodgeMovementActive
                && !snapshot.dodgeMovementTumbling
                && snapshot.dodgeMovementDirection != IntVec3.Zero)
            {
                rotOverride = Rot4.FromIntVec3(
                    snapshot.dodgeMovementDirection);
            }
            else if (RimKataVisualUtility.TryGetResponseFacing(
                ___pawn,
                snapshot,
                out Rot4 responseFacing))
            {
                rotOverride = responseFacing;
            }
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), "ParallelGetPreRenderResults")]
    public static class Patch_PawnRenderer_RimKataDynamicRotationCache
    {
        public static void Prefix(Pawn ___pawn, ref bool disableCache)
        {
            if (!RimKataVisualUtility.TryGetCachedActiveSnapshot(
                    ___pawn,
                    out RimKataVisualSnapshot snapshot))
            {
                return;
            }

            if (RimKataVisualUtility.RequiresDynamicBodyRotation(snapshot))
            {
                disableCache = true;
            }
        }
    }

    [HarmonyPatch(typeof(PawnRenderTree), nameof(PawnRenderTree.ParallelPreDraw))]
    public static class Patch_PawnRenderTree_RimKataTumbleRotation
    {
        public static void Prefix(ref PawnDrawParms parms)
        {
            if (parms.Portrait
                || !RimKataVisualUtility.TryGetCachedActiveSnapshot(
                    parms.pawn,
                    out RimKataVisualSnapshot snapshot))
            {
                return;
            }

            bool additionalTumble = snapshot.visualActive && snapshot.visualState == RimKataVisualState.AdditionalDodge;
            bool stationaryTumble = snapshot.visualActive  && snapshot.visualState == RimKataVisualState.Tumble;
            if (!additionalTumble && !stationaryTumble && !snapshot.closeDodgeActive)
            {
                return;
            }

            float tumbleAngle = (additionalTumble || stationaryTumble)
                ? snapshot.visualProgress
                    * snapshot.visualTotalTicks
                    * RimKataCombatTuning.TumbleDegreesPerTick
                    * snapshot.tumbleSign
                : 0f;

            Matrix4x4 adjustedMatrix = parms.matrix;
            if (Mathf.Abs(tumbleAngle) > 0.001f)
            {
                adjustedMatrix *= Matrix4x4.Rotate(Quaternion.AngleAxis(tumbleAngle, Vector3.up));
            }

            if (snapshot.closeDodgeActive)
            {
                Vector3 footPivot = new Vector3(0f, 0f, -0.5f);
                adjustedMatrix = adjustedMatrix
                    * Matrix4x4.Translate(footPivot)
                    * Matrix4x4.Rotate(Quaternion.AngleAxis(snapshot.closeDodgeAngle, Vector3.up))
                    * Matrix4x4.Translate(-footPivot);
            }

            parms.matrix = adjustedMatrix;
        }
    }

    [HarmonyPatch(typeof(PawnRenderUtility), nameof(PawnRenderUtility.DrawEquipmentAiming))]
    public static class Patch_PawnRenderUtility_RimKataDualWeapons
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Thing eq, Vector3 drawLoc, float aimAngle)
        {
            return !RimKataDualWeaponRenderUtility.TryDrawPair(eq, drawLoc, aimAngle);
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo plane10 = AccessTools.Field(typeof(MeshPool), nameof(MeshPool.plane10));
            FieldInfo plane10Flip = AccessTools.Field(typeof(MeshPool), nameof(MeshPool.plane10Flip));
            MethodInfo choosePlane10 = AccessTools.Method(typeof(RimKataDualWeaponRenderUtility), nameof(RimKataDualWeaponRenderUtility.Plane10ForContext));
            MethodInfo choosePlane10Flip = AccessTools.Method(typeof(RimKataDualWeaponRenderUtility), nameof(RimKataDualWeaponRenderUtility.Plane10FlipForContext));

            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldsfld && Equals(instruction.operand, plane10))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = choosePlane10;
                }
                else if (instruction.opcode == OpCodes.Ldsfld && Equals(instruction.operand, plane10Flip))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = choosePlane10Flip;
                }

                yield return instruction;
            }
        }
    }

    [HarmonyPatch(typeof(PawnRenderUtility), nameof(PawnRenderUtility.DrawEquipmentAiming))]
    public static class Patch_PawnRenderUtility_RimKataDeflection
    {
        [ThreadStatic] private static float visualAngleOffset;

        [HarmonyPriority(Priority.Last)]
        public static void Prefix(
            Thing eq,
            ref Vector3 drawLoc,
            ref float aimAngle,
            out float __state)
        {
            __state = visualAngleOffset;
            visualAngleOffset = 0f;

            RimKataGunReadyDrawContext renderContext =
                RimKataGunReadyDrawUtility.Current;
            if (renderContext.portrait
                || !(eq is ThingWithComps weapon)
                || !RimKataResponseVisualParticipantCache
                    .TryGetWeaponOwner(weapon, out Pawn owner)
                || owner.equipment?.AllEquipmentListForReading
                    ?.Contains(weapon) != true)
            {
                return;
            }

            RimKataVisualSnapshot snapshot;
            if (renderContext.active
                && renderContext.pawn == owner
                && renderContext.snapshotActive)
            {
                snapshot = renderContext.snapshot;
            }
            else if (!RimKataVisualUtility.TryGetCachedActiveSnapshot(
                owner,
                out snapshot))
            {
                return;
            }

            bool deflectThisWeapon = snapshot.deflectionActive
                && snapshot.deflectionWeapon == weapon;
            bool poseThisWeapon = snapshot.responsePoseActive
                && snapshot.responsePoseWeapon == weapon;
            if (!deflectThisWeapon && !poseThisWeapon)
            {
                return;
            }

            float totalOffset = 0f;
            if (deflectThisWeapon)
            {
                float deflectionWave = 1f - Mathf.SmoothStep(0f, 1f, snapshot.deflectionProgress);
                float deflectionAngle = 30f * deflectionWave * snapshot.deflectionSign;
                totalOffset += deflectionAngle;
            }

            if (poseThisWeapon)
            {
                float responseWave = 1f - Mathf.SmoothStep(0f, 1f, snapshot.responsePoseProgress);
                totalOffset += snapshot.responsePoseMaxAngle * responseWave * snapshot.responsePoseSign;
            }

            if (owner != null && Mathf.Abs(totalOffset) > 0.001f)
            {
                Vector3 pivot;
                if (!RimKataDualWeaponRenderUtility
                    .TryGetCurrentEquipmentPivot(out pivot))
                {
                    pivot = RimKataDualWeaponRenderUtility
                        .ResolveEquipmentPivot(
                            owner,
                            weapon,
                            drawLoc,
                            aimAngle);
                }
                float renderHeight = drawLoc.y;
                Vector3 radial = drawLoc - pivot;
                radial.y = 0f;
                radial = radial.RotatedBy(totalOffset);
                drawLoc = new Vector3(pivot.x + radial.x, renderHeight, pivot.z + radial.z);
            }

            visualAngleOffset = totalOffset;
        }

        public static void Finalizer(float __state)
        {
            visualAngleOffset = __state;
        }

        public static float ApplyVisualAngleOffset(float angle)
        {
            return angle + visualAngleOffset;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            MethodInfo applyOffset = AccessTools.Method(typeof(Patch_PawnRenderUtility_RimKataDeflection), nameof(ApplyVisualAngleOffset));
            bool patched = false;
            for (int i = 0; i < codes.Count; i++)
            {
                CodeInstruction instruction = codes[i];
                yield return instruction;
                if (!patched && instruction.opcode == OpCodes.Stloc_1 && i > 0 && codes[i - 1].opcode == OpCodes.Rem)
                {
                    yield return new CodeInstruction(OpCodes.Ldloc_1);
                    yield return new CodeInstruction(OpCodes.Call, applyOffset);
                    yield return new CodeInstruction(OpCodes.Stloc_1);
                    patched = true;
                }
            }

            if (!patched)
            {
                Log.Error("[RimKata] Could not find the final equipment rotation anchor.");
            }
        }
    }

    public struct RimKataGunReadyDrawContext
    {
        public bool scoped;
        public bool portrait;
        public bool active;
        public bool gunReady;
        public Pawn pawn;
        public ThingWithComps primary;
        public ThingWithComps secondary;
        public bool snapshotActive;
        public RimKataVisualSnapshot snapshot;
        public float aimAngle;
    }

    internal static class RimKataGunReadyDrawUtility
    {
        [ThreadStatic] private static RimKataGunReadyDrawContext current;

        public static RimKataGunReadyDrawContext Current => current;

        public static RimKataGunReadyDrawContext Push(Pawn pawn, PawnRenderFlags flags)
        {
            RimKataGunReadyDrawContext previous = current;
            bool portrait = (flags & PawnRenderFlags.Portrait) != 0;
            current = new RimKataGunReadyDrawContext
            {
                scoped = true,
                portrait = portrait
            };
            if (portrait || pawn?.Spawned != true)
            {
                return previous;
            }

            bool rimKataUser = RimKataVisualUtility
                .TryGetCachedWorldLoadout(
                    pawn,
                    out ThingWithComps primary,
                    out ThingWithComps rawSecondary);
            bool responseParticipant = RimKataVisualUtility
                .TryGetResponseParticipantLoadout(
                    pawn,
                    out ThingWithComps participantPrimary,
                    out ThingWithComps participantSecondary);
            if (!rimKataUser && !responseParticipant)
            {
                return previous;
            }

            if (!rimKataUser)
            {
                primary = participantPrimary;
            }

            ThingWithComps secondary = rimKataUser
                ? RimKataVisualUtility.IsSecondaryUsable(
                    pawn,
                    primary,
                    rawSecondary)
                        ? rawSecondary
                        : null
                : participantSecondary;
            RimKataVisualSnapshot snapshot =
                default(RimKataVisualSnapshot);
            bool snapshotActive = (secondary != null || responseParticipant)
                && RimKataVisualUtility.TryGetCachedActiveSnapshot(
                    pawn,
                    out snapshot);
            RimKataGunReadyDrawContext next =
                new RimKataGunReadyDrawContext
                {
                    scoped = true,
                    active = true,
                    pawn = pawn,
                    primary = primary,
                    secondary = secondary,
                    snapshotActive = snapshotActive,
                    snapshot = snapshot
                };
            current = next;
            if (!rimKataUser
                || pawn.Dead
                || pawn.Downed
                || pawn.IsBurning()
                || primary == null
                || pawn.carryTracker?.CarriedThing != null
                || (flags & PawnRenderFlags.NeverAimWeapon) != 0
                || pawn.stances?.curStance is Stance_Busy)
            {
                return previous;
            }

            RimKataMapComponent component = pawn.Map.GetComponent<RimKataMapComponent>();
            if (component?.TryGetGunReadyTarget(pawn, out LocalTargetInfo target) != true)
            {
                return previous;
            }

            if (!RimKataEligibility.TryGetEnabledCombatVerb(pawn, out Verb _))
            {
                return previous;
            }

            float aimAngle = pawn.Rotation.AsAngle;
            Vector3 targetPosition;
            if (target.IsValid)
            {
                targetPosition = target.HasThing && target.Thing.Spawned
                    ? target.Thing.DrawPos
                    : target.Cell.ToVector3Shifted();
                Vector3 aimVector = targetPosition - pawn.DrawPos;
                if (aimVector.sqrMagnitude > 0.001f)
                {
                    aimAngle = aimVector.AngleFlat();
                }
            }
            else if (RimKataDodgeMovementUtility.TryGetCurrentMovementDirection(pawn, out IntVec3 direction))
            {
                aimAngle = direction.ToVector3().AngleFlat();
            }

            next.gunReady = true;
            next.aimAngle = aimAngle;
            current = next;
            return previous;
        }

        public static void Pop(RimKataGunReadyDrawContext previous)
        {
            current = previous;
        }
    }

    [HarmonyPatch(typeof(PawnRenderUtility), nameof(PawnRenderUtility.DrawEquipmentAndApparelExtras))]
    public static class Patch_PawnRenderUtility_RimKataGunReadyContext
    {
        public static void Prefix(
            Pawn pawn,
            PawnRenderFlags flags,
            out RimKataGunReadyDrawContext __state)
        {
            __state = RimKataGunReadyDrawUtility.Push(pawn, flags);
        }

        public static Exception Finalizer(
            Exception __exception,
            RimKataGunReadyDrawContext __state)
        {
            RimKataGunReadyDrawUtility.Pop(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(PawnRenderUtility), nameof(PawnRenderUtility.CarryWeaponOpenly))]
    public static class Patch_PawnRenderUtility_RimKataCarryGunReady
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            RimKataGunReadyDrawContext context = RimKataGunReadyDrawUtility.Current;
            if (context.gunReady && context.pawn == pawn)
            {
                __result = true;
            }

        }
    }

    [HarmonyPatch(typeof(PawnRenderUtility), nameof(PawnRenderUtility.DrawCarriedWeapon))]
    public static class Patch_PawnRenderUtility_RimKataCarryDrawContext
    {
        [HarmonyPriority(Priority.First)]
        public static void Prefix(
            ThingWithComps weapon,
            Vector3 drawPos,
            out RimKataCarryDrawContext __state)
        {
            __state = RimKataCarryDrawUtility.Push(weapon, drawPos);
        }

        public static Exception Finalizer(
            Exception __exception,
            RimKataCarryDrawContext __state)
        {
            RimKataCarryDrawUtility.Pop(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(PawnRenderUtility), nameof(PawnRenderUtility.DrawCarriedWeapon))]
    public static class Patch_PawnRenderUtility_RimKataDrawGunReady
    {
        public static bool Prefix(
            ThingWithComps weapon,
            Vector3 drawPos,
            float equipmentDrawDistanceFactor)
        {
            RimKataGunReadyDrawContext context = RimKataGunReadyDrawUtility.Current;
            if (!context.gunReady || context.primary != weapon)
            {
                return true;
            }

            float aimAngle = context.aimAngle;
            Vector3 aimedDrawLoc = drawPos + new Vector3(0f, 0f, 0.4f + weapon.def.equippedDistanceOffset).RotatedBy(aimAngle) * equipmentDrawDistanceFactor;
            PawnRenderUtility.DrawEquipmentAiming(weapon, aimedDrawLoc, aimAngle);
            return false;
        }
    }

    [HarmonyPatch(
    typeof(Command_VerbTarget),
    nameof(Command_VerbTarget.GizmoUpdateOnMouseover))]
    public static class Patch_CommandVerbTarget_RimKataAutomaticRange
    {
        public static void Postfix(
            Command_VerbTarget __instance,
            List<Verb> ___groupedVerbs)
        {
            if (__instance?.drawRadius != true)
            {
                return;
            }

            DrawAutomaticSearchRange(__instance.verb);

            if (___groupedVerbs == null)
            {
                return;
            }

            for (int i = 0; i < ___groupedVerbs.Count; i++)
            {
                DrawAutomaticSearchRange(___groupedVerbs[i]);
            }
        }

        private static void DrawAutomaticSearchRange(Verb verb)
        {
            Pawn pawn = verb?.CasterPawn;
            if (pawn == null
                || !pawn.Spawned
                || !RimKataAutomaticRangeVisualUtility.CanDrawAutomaticSearchRange(pawn))
            {
                return;
            }

            ThingWithComps commandWeapon = verb.EquipmentSource;
            if (commandWeapon == null)
            {
                return;
            }

            if (!RimKataVisualUtility.TryGetUiLoadout(
                    pawn,
                    out ThingWithComps primary,
                    out ThingWithComps rawSecondary))
            {
                return;
            }

            ThingWithComps secondary =
                RimKataVisualUtility.IsSecondaryUsable(
                    pawn,
                    primary,
                    rawSecondary)
                        ? rawSecondary
                        : null;
            if (commandWeapon != primary
                && commandWeapon != secondary)
            {
                return;
            }

            RimKataAutomaticRangeVisualUtility.DrawAutomaticSearchRange(pawn);
        }
    }

    [HarmonyPatch(typeof(PawnAttackGizmoUtility), "GetSquadAttackGizmo")]
    public static class Patch_PawnAttackGizmoUtility_RimKataSquadRange
    {
        private static readonly Action<LocalTargetInfo>
            DrawSelectedAutomaticRangesAction = DrawSelectedAutomaticRanges;

        public static void Postfix(ref Gizmo __result)
        {
            if (!(__result is Command_Target command)
                || !RimKataMultiSelectAttackGizmoUtility
                    .HasSelectedPawnWithAutomaticSearchRange())
            {
                return;
            }

            Action<LocalTargetInfo> originalAction = command.action;
            if (originalAction != null)
            {
                command.action = target =>
                    RimKataAttackGizmoTargetContext.Invoke(
                        originalAction,
                        target);
            }

            if (RimKataMultiSelectAttackGizmoUtility
                .ShouldUseUnifiedAttackGizmo())
            {
                command.onUpdate = DrawSelectedUnifiedRanges;
            }
            else if (command.onUpdate == null)
            {
                command.onUpdate = DrawSelectedAutomaticRangesAction;
            }
            else if (command.onUpdate != DrawSelectedAutomaticRangesAction)
            {
                command.onUpdate += DrawSelectedAutomaticRangesAction;
            }
        }

        private static void DrawSelectedUnifiedRanges(LocalTargetInfo _)
        {
            List<Pawn> selectedPawns = Find.Selector?.SelectedPawns;
            if (selectedPawns == null)
            {
                return;
            }

            for (int i = 0; i < selectedPawns.Count; i++)
            {
                Pawn pawn = selectedPawns[i];
                if (pawn?.Spawned != true || !pawn.IsPlayerControlled)
                {
                    continue;
                }

                RimKataAutomaticRangeVisualUtility
                    .DrawLongestRangedWeaponRange(pawn);
                if (RimKataAutomaticRangeVisualUtility
                    .CanDrawAutomaticSearchRange(pawn))
                {
                    RimKataAutomaticRangeVisualUtility
                        .DrawAutomaticSearchRange(pawn);
                }
            }
        }

        private static void DrawSelectedAutomaticRanges(LocalTargetInfo _)
        {
            List<Pawn> selectedPawns = Find.Selector?.SelectedPawns;
            if (selectedPawns == null)
            {
                return;
            }

            for (int i = 0; i < selectedPawns.Count; i++)
            {
                Pawn pawn = selectedPawns[i];
                if (pawn?.Spawned == true
                    && RimKataAutomaticRangeVisualUtility
                        .CanDrawAutomaticSearchRange(pawn))
                {
                    RimKataAutomaticRangeVisualUtility
                        .DrawAutomaticSearchRange(pawn);
                }
            }
        }
    }
}
