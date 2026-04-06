using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using HQoL.Util;

namespace HQoL.Patches;

[HarmonyPatch(typeof(GameNetworkManager))]
internal class GameNetworkManagerPatches
{
    [HarmonyPatch(nameof(GameNetworkManager.Start))]
    [HarmonyPostfix]
    private static void PostStart(GameNetworkManager __instance)
    {
        Network.HQoLNetwork.CreateAndRegisterPrefab();
    }

    [HarmonyPatch(nameof(GameNetworkManager.Disconnect))]
    [HarmonyPrefix]
    private static void PreDisconnect(GameNetworkManager __instance)
    {
        bool isChalFile = false;
        if (StartOfRoundPatches.hasChallengeFile)
            isChalFile = StartOfRoundPatches.isChallengeFileRef(StartOfRound.Instance);

        if (Network.HQoLNetwork.Instance.storageHasBeenModified && __instance.isHostingGame && StartOfRound.Instance.inShipPhase && !isChalFile)
        {
            try
            {
                List<ItemReference> itemRefList = new();
                foreach (ItemReference itemRef in Network.HQoLNetwork.Instance.netStorage)
                    itemRefList.Add(itemRef);

                ES3.Save("HQoL.ScrapList", itemRefList, GameNetworkManager.Instance.currentSaveFileName);
                Network.HQoLNetwork.Instance.storageHasBeenModified = false;
            }
            catch (System.Exception arg)
            {
                HQoL.Logger.LogError($"Error while trying to save game values when disconnecting as host: {arg}");
            }
        }

        Network.HQoLNetwork.DespawnNetworkHandler();
    }

    private static void NewCodeFromV81()
    {
				ES3.DeleteKey("shipGrabbableItemIDs", GameNetworkManager.Instance.currentSaveFileName);
				ES3.DeleteKey("shipGrabbableItemPos", GameNetworkManager.Instance.currentSaveFileName);
				ES3.DeleteKey("shipScrapValues", GameNetworkManager.Instance.currentSaveFileName);
				ES3.DeleteKey("shipItemSaveData", GameNetworkManager.Instance.currentSaveFileName);
    }

    //Fix the game not saving 0 items if there is no scrap and respawning them upon lobby restart
    //Basically an infinite money glitch :p
    [HarmonyPatch(nameof(GameNetworkManager.SaveItemsInShip))]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> TranspileSaveItemsInShip(IEnumerable<CodeInstruction> codes)
    {
        CodeMatcher matcher =
            new CodeMatcher(codes)
            .MatchForward(false,
                new CodeMatch(OpCodes.Ldloc_1),
                new CodeMatch(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(List<int>), nameof(List<int>.Count))),
                new CodeMatch(OpCodes.Ldc_I4_0),
                new CodeMatch(OpCodes.Bgt));

        CodeInstruction[] PatchedCode = 
        {
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(GameNetworkManagerPatches), nameof(GameNetworkManagerPatches.NewCodeFromV81))),
            new CodeInstruction(OpCodes.Ret)
        };

        matcher.Advance(6);
        if (matcher.Opcode != OpCodes.Ret)
        {
            HQoL.Logger.LogInfo("0 item save bug already fix, this is likely v80+");
            return codes;
        }

        List<Label> label = matcher.Labels;
        return matcher
            .Advance(-2)
            .RemoveInstructions(3)
            .Insert(PatchedCode)
            .AddLabels(label)
            .InstructionEnumeration();
    }
}
