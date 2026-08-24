using UnityEngine;
using Verse;

namespace KRWF.RimKata
{
    public sealed class Dialog_RimKataCombatFeatures : Window
    {
        private const float RowHeight = 30f;
        private const float RestrictionButtonHeight = 30f;
        private const float CloseButtonHeight = 30f;
        private const float HorizontalPadding = 24f;
        private const float VerticalPadding = 18f;
        private readonly RimKataSettings settings;
        private bool secondaryWeaponEnabled;
        private bool singleShotConversionEnabled;
        private bool randomAttackEnabled;
        private bool explosiveInterceptionEnabled;
        private bool movingFireEnabled;
        private bool closeFireEnabled;
        private bool targetRushEnabled;
        private bool responseEnabled;
        private bool rangedDodgeEnabled;
        private bool tumbleEnabled;

        private static readonly string[] LabelKeys =
        {
            "KRWF_RimKata_FeatureSecondaryWeapon",
            "KRWF_RimKata_FeatureSingleShotConversion",
            "KRWF_RimKata_FeatureRandomAttack",
            "KRWF_RimKata_FeatureExplosiveInterception",
            "KRWF_RimKata_FeatureMovingFire",
            "KRWF_RimKata_FeatureCloseFire",
            "KRWF_RimKata_FeatureTargetRush",
            "KRWF_RimKata_FeatureResponse",
            "KRWF_RimKata_FeatureRangedDodge",
            "KRWF_RimKata_FeatureTumble"
        };

        public Dialog_RimKataCombatFeatures(RimKataSettings settings)
        {
            this.settings = settings;
            if (settings != null)
            {
                secondaryWeaponEnabled = settings.secondaryWeaponEnabled;
                singleShotConversionEnabled =
                    settings.singleShotConversionEnabled;
                randomAttackEnabled = settings.randomAttackEnabled;
                explosiveInterceptionEnabled =
                    settings.explosiveInterceptionEnabled;
                movingFireEnabled = settings.movingFireEnabled;
                closeFireEnabled = settings.closeFireEnabled;
                targetRushEnabled = settings.targetRushEnabled;
                responseEnabled = settings.responseEnabled;
                rangedDodgeEnabled = settings.rangedDodgeEnabled;
                tumbleEnabled = settings.tumbleEnabled;
            }

            doCloseX = false;
            doCloseButton = false;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = false;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize
        {
            get
            {
                float labelWidth = 0f;
                for (int i = 0; i < LabelKeys.Length; i++)
                {
                    labelWidth = Mathf.Max(labelWidth, Text.CalcSize(LabelKeys[i].Translate()).x);
                }

                labelWidth = Mathf.Max(labelWidth, Text.CalcSize("KRWF_RimKata_RemoveRestrictions".Translate()).x);
                labelWidth = Mathf.Max(labelWidth, Text.CalcSize("KRWF_RimKata_RestoreRestrictions".Translate()).x);
                float closeWidth = Text.CalcSize("Close".Translate()).x + 28f;
                float width = Mathf.Max(labelWidth + 54f, closeWidth + HorizontalPadding * 2f);
                float height = VerticalPadding * 2f
                    + RestrictionButtonHeight
                    + 10f
                    + LabelKeys.Length * RowHeight
                    + 10f
                    + CloseButtonHeight;
                return new Vector2(Mathf.Clamp(width, 220f, 420f), height);
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (settings == null)
            {
                Close();
                return;
            }

            float y = inRect.y;
            DrawRestrictionButton(inRect, ref y);
            y += 10f;
            DrawCheckbox(inRect, ref y, LabelKeys[0], ref secondaryWeaponEnabled);
            DrawCheckbox(inRect, ref y, LabelKeys[1], ref singleShotConversionEnabled);
            DrawCheckbox(inRect, ref y, LabelKeys[2], ref randomAttackEnabled);
            DrawCheckbox(inRect, ref y, LabelKeys[3], ref explosiveInterceptionEnabled);
            DrawCheckbox(inRect, ref y, LabelKeys[4], ref movingFireEnabled);
            DrawCheckbox(inRect, ref y, LabelKeys[5], ref closeFireEnabled);
            DrawCheckbox(inRect, ref y, LabelKeys[6], ref targetRushEnabled);
            DrawCheckbox(inRect, ref y, LabelKeys[7], ref responseEnabled);
            DrawCheckbox(inRect, ref y, LabelKeys[8], ref rangedDodgeEnabled);
            DrawCheckbox(inRect, ref y, LabelKeys[9], ref tumbleEnabled);

            y += 10f;
            string closeLabel = "Close".Translate();
            float closeWidth = Mathf.Clamp(Text.CalcSize(closeLabel).x + 28f, 90f, inRect.width);
            Rect closeRect = new Rect(inRect.x + (inRect.width - closeWidth) * 0.5f, y, closeWidth, CloseButtonHeight);
            if (Widgets.ButtonText(closeRect, closeLabel))
            {
                Close();
            }
        }

        public override void PostClose()
        {
            base.PostClose();
            if (settings == null)
            {
                return;
            }

            bool changed = settings.secondaryWeaponEnabled
                    != secondaryWeaponEnabled
                || settings.singleShotConversionEnabled
                    != singleShotConversionEnabled
                || settings.randomAttackEnabled != randomAttackEnabled
                || settings.explosiveInterceptionEnabled
                    != explosiveInterceptionEnabled
                || settings.movingFireEnabled != movingFireEnabled
                || settings.closeFireEnabled != closeFireEnabled
                || settings.targetRushEnabled != targetRushEnabled
                || settings.responseEnabled != responseEnabled
                || settings.rangedDodgeEnabled != rangedDodgeEnabled
                || settings.tumbleEnabled != tumbleEnabled;

            settings.secondaryWeaponEnabled = secondaryWeaponEnabled;
            settings.singleShotConversionEnabled =
                singleShotConversionEnabled;
            settings.randomAttackEnabled = randomAttackEnabled;
            settings.explosiveInterceptionEnabled =
                explosiveInterceptionEnabled;
            settings.movingFireEnabled = movingFireEnabled;
            settings.closeFireEnabled = closeFireEnabled;
            settings.targetRushEnabled = targetRushEnabled;
            settings.responseEnabled = responseEnabled;
            settings.rangedDodgeEnabled = rangedDodgeEnabled;
            settings.tumbleEnabled = tumbleEnabled;

            if (changed)
            {
                RimKataWeaponSlotUtility.NotifyCombatFeaturesChanged();
            }
        }

        private static void DrawCheckbox(Rect inRect, ref float y, string key, ref bool value)
        {
            Widgets.CheckboxLabeled(new Rect(inRect.x, y, inRect.width, RowHeight), key.Translate(), ref value);
            y += RowHeight;
        }

        private void DrawRestrictionButton(Rect inRect, ref float y)
        {
            bool restrictionsDisabled = settings.accessRestrictionsDisabled;
            string label = (restrictionsDisabled
                ? "KRWF_RimKata_RestoreRestrictions"
                : "KRWF_RimKata_RemoveRestrictions").Translate();
            float width = Mathf.Clamp(Text.CalcSize(label).x + 28f, 90f, inRect.width);
            Rect buttonRect = new Rect(inRect.x + (inRect.width - width) * 0.5f, y, width, RestrictionButtonHeight);
            y += RestrictionButtonHeight;
            if (!Widgets.ButtonText(buttonRect, label))
            {
                return;
            }

            bool disableRestrictions = !restrictionsDisabled;
            string message = (disableRestrictions
                ? "KRWF_RimKata_RemoveRestrictionsWarning"
                : "KRWF_RimKata_RestoreRestrictionsConfirmation").Translate();
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                message,
                delegate{ settings.accessRestrictionsDisabled = disableRestrictions; RimKataWeaponSlotUtility.NotifyCombatFeaturesChanged();}, false, null, WindowLayer.Dialog));
        }
    }
}
