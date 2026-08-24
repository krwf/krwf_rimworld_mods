using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace KRWF.RimKata
{
    [StaticConstructorOnStartup]
    public static class RimKataBootstrap
    {
        static RimKataBootstrap()
        {
            Harmony harmony;
            try
            {
                harmony = new Harmony("krwf.rimkata");
            }
            catch (Exception exception)
            {
                Log.Error("[RimKata] Could not create the Harmony instance.\n" + exception);
                return;
            }

            try
            {
                harmony.PatchAll(Assembly.GetExecutingAssembly());
            }
            catch (Exception exception)
            {
                Log.Error("[RimKata] Core Harmony patching failed; initialization will continue.\n" + exception);
            }

            try
            {
                Patch_Projectile_Impact_Context.Apply(harmony);
            }
            catch (Exception exception)
            {
                Log.Error("[RimKata] Projectile.Impact patch discovery failed; initialization will continue.\n" + exception);
            }
        }
    }
}
