using System;
using UnityEngine;
using Verse;

namespace KRWF.RimKata
{
    public readonly struct RimKataRangeBands
    {
        public readonly float Touch;
        public readonly float Short;
        public readonly float Medium;
        public readonly float Long;

        public RimKataRangeBands(
            float touchRange,
            float shortRange,
            float mediumRange,
            float longRange)
        {
            Touch = touchRange;
            Short = shortRange;
            Medium = mediumRange;
            Long = longRange;
        }
    }

    public static class RimKataRangeUtility
    {
        private const float ProbeLow = 0.2f;
        private const float ProbeHigh = 0.8f;
        private const float ProbeTolerance = 0.000001f;
        private const float ProbeSearchLimit = 4096f;
        private static bool runtimeBandsAvailable;
        private static readonly RimKataRangeBands CachedBands = DetectRuntimeBands();

        public static RimKataRangeBands CurrentBands
        {
            get => CachedBands;
        }

        public static float ResolveCandidateRange(Verb verb)
        {
            float effectiveRange = Mathf.Max(0f, verb?.EffectiveRange ?? 0f);
            RimKataSettings settings = RimKataMod.Settings;
            if (settings == null)
            {
                return RuntimeBandsAvailable
                    ? Mathf.Min(effectiveRange, CurrentBands.Short)
                    : effectiveRange;
            }

            if (!RuntimeBandsAvailable
                && (settings.candidateRangeMode != RimKataCandidateRangeMode.Custom
                    || settings.customCandidateRange <= 0f))
            {
                return effectiveRange;
            }

            float configuredRange;
            RimKataRangeBands bands = CurrentBands;
            switch (settings.candidateRangeMode)
            {
                case RimKataCandidateRangeMode.Medium:
                    configuredRange = bands.Medium;
                    break;
                case RimKataCandidateRangeMode.Long:
                    configuredRange = bands.Long;
                    break;
                case RimKataCandidateRangeMode.Unlimited:
                    configuredRange = effectiveRange;
                    break;
                case RimKataCandidateRangeMode.Custom:
                    configuredRange = settings.customCandidateRange > 0f
                        ? settings.customCandidateRange
                        : bands.Short;
                    break;
                default:
                    configuredRange = bands.Short;
                    break;
            }

            return Mathf.Min(effectiveRange, Mathf.Max(0f, configuredRange));
        }

        public static float PresetRange(RimKataCandidateRangeMode mode)
        {
            RimKataRangeBands bands = CurrentBands;
            switch (mode)
            {
                case RimKataCandidateRangeMode.Medium:
                    return bands.Medium;
                case RimKataCandidateRangeMode.Long:
                    return bands.Long;
                default:
                    return bands.Short;
            }
        }

        public static bool RuntimeBandsAvailable
        {
            get
            {
                _ = CurrentBands;
                return runtimeBandsAvailable;
            }
        }

        private static RimKataRangeBands DetectRuntimeBands()
        {
            try
            {
                VerbProperties touchShortProbe = CreateProbe(
                    ProbeLow,
                    ProbeHigh,
                    ProbeHigh,
                    ProbeHigh);
                VerbProperties shortMediumProbe = CreateProbe(ProbeLow, ProbeLow, ProbeHigh, ProbeHigh);
                VerbProperties mediumLongProbe = CreateProbe(ProbeLow, ProbeLow, ProbeLow, ProbeHigh);
                float touchRange = FindLowPlateauEnd(touchShortProbe);
                float verifiedShortRange = FindHighPlateauStart(touchShortProbe);
                float shortRange = FindLowPlateauEnd(shortMediumProbe);
                float mediumRange = FindHighPlateauStart(shortMediumProbe);
                float verifiedMediumRange = FindLowPlateauEnd(mediumLongProbe);
                float longRange = FindHighPlateauStart(mediumLongProbe);
                touchRange = NormalizeBoundary(touchRange);
                verifiedShortRange = NormalizeBoundary(verifiedShortRange);
                shortRange = NormalizeBoundary(shortRange);
                mediumRange = NormalizeBoundary(mediumRange);
                verifiedMediumRange = NormalizeBoundary(verifiedMediumRange);
                longRange = NormalizeBoundary(longRange);
                if (IsValidBands(
                    touchRange,
                    verifiedShortRange,
                    shortRange,
                    mediumRange,
                    verifiedMediumRange,
                    longRange))
                {
                    runtimeBandsAvailable = true;
                    return new RimKataRangeBands(
                        touchRange,
                        shortRange,
                        mediumRange,
                        longRange);
                }

                Log.Warning("[RimKata] Active shooting range bands failed validation; presets will use each weapon's effective range.");
            }
            catch (Exception exception)
            {
                Log.Warning("[RimKata] Could not read the active shooting range bands; presets will use each weapon's effective range. " + exception.Message);
            }

            runtimeBandsAvailable = false;
            return new RimKataRangeBands(
                ProbeSearchLimit,
                ProbeSearchLimit,
                ProbeSearchLimit,
                ProbeSearchLimit);
        }

        private static VerbProperties CreateProbe(float touch, float shortAccuracy, float mediumAccuracy, float longAccuracy)
        {
            return new VerbProperties
            {
                accuracyTouch = touch,
                accuracyShort = shortAccuracy,
                accuracyMedium = mediumAccuracy,
                accuracyLong = longAccuracy
            };
        }

        private static float FindLowPlateauEnd(VerbProperties probe)
        {
            return FindBoundary(probe, value => value <= ProbeLow + ProbeTolerance);
        }

        private static float FindHighPlateauStart(VerbProperties probe)
        {
            return FindBoundary(probe, value => value < ProbeHigh - ProbeTolerance);
        }

        private static float FindBoundary(VerbProperties probe, Func<float, bool> remainsBeforeBoundary)
        {
            float low = 0f;
            float high = 1f;
            while (high < ProbeSearchLimit)
            {
                float value = probe.GetHitChanceFactor(null, high);
                if (!IsFinite(value))
                {
                    throw new InvalidOperationException("The shooting range curve returned a non-finite value.");
                }

                if (!remainsBeforeBoundary(value))
                {
                    break;
                }

                low = high;
                high *= 2f;
            }

            if (high >= ProbeSearchLimit && remainsBeforeBoundary(probe.GetHitChanceFactor(null, high)))
            {
                throw new InvalidOperationException("The shooting range curve has no detectable boundary.");
            }

            for (int i = 0; i < 24; i++)
            {
                float middle = (low + high) * 0.5f;
                float value = probe.GetHitChanceFactor(null, middle);
                if (!IsFinite(value))
                {
                    throw new InvalidOperationException("The shooting range curve returned a non-finite value.");
                }

                if (remainsBeforeBoundary(value))
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            return high;
        }

        private static bool IsValidBands(
            float touchRange,
            float verifiedShortRange,
            float shortRange,
            float mediumRange,
            float verifiedMediumRange,
            float longRange)
        {
            return IsFinite(touchRange)
                && IsFinite(verifiedShortRange)
                && IsFinite(shortRange)
                && IsFinite(mediumRange)
                && IsFinite(verifiedMediumRange)
                && IsFinite(longRange)
                && touchRange > 0f
                && touchRange < shortRange
                && Mathf.Abs(shortRange - verifiedShortRange) <= 0.01f
                && shortRange < mediumRange
                && Mathf.Abs(mediumRange - verifiedMediumRange) <= 0.01f
                && mediumRange < longRange;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }


        private static float NormalizeBoundary(float value)
        {
            return Mathf.Round(value * 1000f) / 1000f;
        }
    }
}
