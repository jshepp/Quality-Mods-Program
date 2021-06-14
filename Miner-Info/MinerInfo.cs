using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace QualityModsProgram
{
    [BepInPlugin("quality-mods-program.plugins.miner-info", "Miner Info Plugin", "1.0.0.0")]
    public class MinerInfo : BaseUnityPlugin
    {
        private Harmony _harmony;
        new internal static ManualLogSource Logger;

        private class PluginConfig
        {
            public static ConfigEntry<bool> ShowVeinMaxMinerOutput;
            public static ConfigEntry<bool> ShowItemsPerSecond;
        }

        void Awake()
        {
            MinerInfo.Logger = base.Logger;

            PluginConfig.ShowVeinMaxMinerOutput = Config.Bind(
                "MinerInfo",
                "ShowVeinMaxMinerOutput",
                true,
                "Show the maximum number of items per time period output by all miners on a vein.");
            PluginConfig.ShowItemsPerSecond = Config.Bind(
                "MinerInfo",
                "ShowItemsPerSecond",
                true,
                "If true, show items per second. If false, show items per minute.");
            _harmony = Harmony.CreateAndPatchAll(typeof(ShowVeinMaxMinerOutputPatch));
        }

        void OnDestroy()
        {
            // Make sure we unpatch ourselves when reloaded by BepInEx script engine hot-loading.
            _harmony.UnpatchSelf();
        }

        private class ShowVeinMaxMinerOutputPatch
        {
            // Note, we return false along each return path to prevent DSP from calling into the original method.
            [HarmonyPrefix]
            [HarmonyPatch(typeof(UIVeinDetailNode), "_OnUpdate")]
            public static bool UIVeinDetailNode_OnUpdatePatch(
                ref UIVeinDetailNode __instance,
                ref Text ___infoText,
                ref int ___counter,
                ref long ___showingAmount,
                ref VeinProto ___veinProto)
            {
                if (__instance.inspectPlanet == null)
                {
                    __instance._Close();
                    return false;
                }
                PlanetData.VeinGroup veinGroup = __instance.inspectPlanet.veinGroups[__instance.veinGroupIndex];
                if (veinGroup.count == 0)
                {
                    __instance._Close();
                    return false;
                }
                if (___counter % 3 == 0)
                {
                    ___showingAmount = veinGroup.amount;
                    if (veinGroup.type != EVeinType.Oil)
                    {
                        // DSP keeps a global variable miningSpeedScale that starts at 1.0 and increases based on
                        // vein productivity research. e.g. At vein productivity level 4 this variable is 1.4.
                        float miningSpeedScale = __instance.inspectPlanet.factory.gameData.history.miningSpeedScale;

                        int minedVeinsCount = CountTotalMinedVeinsInVeinGroup(__instance.veinGroupIndex, __instance.inspectPlanet);
                        double itemsPerSecond = 0.5 * miningSpeedScale * minedVeinsCount;
                        Logger.LogDebug("itemsPerSecond [" + itemsPerSecond.ToString() + "]");

                        string text = string.Concat(new string[]
                        {
                            veinGroup.count.ToString(),
                            "空格个".Translate(),
                            ___veinProto.name,
                            "储量".Translate(),
                            veinGroup.amount.ToString("#,##0"),
                        });
                        if (itemsPerSecond > 0)
                        {
                            string message;
                            if (PluginConfig.ShowItemsPerSecond.Value)
                            {
                                message = "\nMiners max output : " + itemsPerSecond.ToString("0.0") + "/s";
                            } else
                            {
                                double itemsPerMinute = 60 * itemsPerSecond;
                                message = "\nMiners max output : " + itemsPerMinute.ToString("0") + "/m";
                            }
                            text += message;
                        }
                        ___infoText.text = text;
                    }
                    else
                    {
                        ___infoText.text = string.Concat(new string[]
                        {
                            veinGroup.count.ToString(),
                            "空格个".Translate(),
                            ___veinProto.name,
                            "产量".Translate(),
                            ((float)veinGroup.amount * VeinData.oilSpeedMultiplier).ToString("0.00"),
                            "/s"
                        });
                    }

                }
                ___counter++;
                return false;
            }

            private static int CountTotalMinedVeinsInVeinGroup(int veinGroupIndex, PlanetData planetData)
            {
                Logger.LogDebug("Getting vein counts for veinGroupIndex [" + veinGroupIndex.ToString() + "]");
                int minedVeinsCount = 0;
                foreach (MinerComponent miner in planetData.factory.factorySystem.minerPool)
                {
                    // Skip miners that aren't mining a mineral (e.g. water)
                    if (miner.type != EMinerType.Vein)
                    {
                        continue;
                    }

                    // Make sure veins is initialized (it isn't when first loading a save)
                    if (miner.veins == null)
                    {
                        continue;
                    }

                    // Skip miners that aren't mining anything
                    if (miner.veinCount <= 0)
                    {
                        continue;
                    }

                    // A miner can only be mining one type of vein so just check the first non-none-type one's groupIndex
                    VeinData minerVein = new VeinData();
                    minerVein.SetNull();
                    for(int i = 0; i < miner.veins.Length; i++)
                    {
                        VeinData tVein = planetData.factory.veinPool[miner.veins[i]];
                        if(tVein.type != EVeinType.None)
                        {
                            minerVein = tVein;
                            break;
                        }
                    }

                    // depleted veins get their veinGroupIndex set to 0 which can inflate veinPool[0]'s count - these veins have EVeinType.None
                    // so if none of this miner's veins have a non-none-type vein then it's that case
                    if (minerVein.type == EVeinType.None)
                    {
                        continue;
                    }

                    // Otherwise, if this miner's vein's groupIndex is the VeinGroup's, append its count
                    if(minerVein.groupIndex == veinGroupIndex)
                    {
                        minedVeinsCount += miner.veinCount;
                        Logger.LogDebug("Appending [" + miner.veinCount + "] veins for vein type [" + minerVein.type.ToString() + "] " + "miner type [" + miner.type.ToString() + "] miner ID [" + miner.id.ToString() + "]");
                    }
                }

                Logger.LogDebug("veinGroupIndex has [" + minedVeinsCount.ToString() + "] mined veins");
                return minedVeinsCount;
            }
        }
    }
}
