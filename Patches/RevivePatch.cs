using System;
using System.Collections.Generic;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace Medic.Patches;

[HarmonyPatch]
internal static class RevivePatch
{
    private static float _lastReviveTime;

    /// <summary>
    /// Postfix on PlayerController.Update — checks for revive key press
    /// and looks for nearby dead players to revive.
    /// </summary>
    [HarmonyPatch(typeof(PlayerController), "Update")]
    [HarmonyPostfix]
    private static void PlayerController_Update_Postfix(PlayerController __instance)
    {
        try
        {
            // Only run for the local player
            if (__instance != PlayerController.instance)
                return;

            // Check if revive key was pressed this frame
            if (!Medic.ReviveKey.Value.IsDown())
                return;

            // Check cooldown
            float cooldown = Medic.ReviveCooldown.Value;
            if (Time.time - _lastReviveTime < cooldown)
            {
                Medic.Logger.LogDebug($"Revive on cooldown ({cooldown - (Time.time - _lastReviveTime):F1}s remaining)");
                return;
            }

            // Check if the local player is alive
            var localAvatar = __instance.playerAvatarScript;
            if (localAvatar == null || localAvatar.deadSet)
                return;

            // Find the nearest dead player within range
            float reviveRange = Medic.ReviveRange.Value;
            Vector3 myPosition = __instance.transform.position;
            PlayerAvatar? nearestDead = null;
            float nearestDistance = float.MaxValue;

            var allPlayers = SemiFunc.PlayerGetAll();
            if (allPlayers == null)
                return;

            foreach (var player in allPlayers)
            {
                // Skip self
                if (player == localAvatar)
                    continue;

                // Check if this player is dead
                if (!player.deadSet)
                    continue;

                // Check distance
                float distance = Vector3.Distance(myPosition, player.transform.position);
                if (distance <= reviveRange && distance < nearestDistance)
                {
                    nearestDead = player;
                    nearestDistance = distance;
                }
            }

            if (nearestDead == null)
            {
                Medic.Logger.LogDebug("No dead players in range to revive.");
                return;
            }

            // Revive the player
            RevivePlayer(nearestDead);
            _lastReviveTime = Time.time;
        }
        catch (Exception ex)
        {
            Medic.Logger.LogError($"Revive exception: {ex.Message}");
        }
    }

    private static void RevivePlayer(PlayerAvatar deadPlayer)
    {
        try
        {
            int reviveHealth = Medic.ReviveHealth.Value;
            string playerName = deadPlayer.playerName ?? "Unknown";

            Medic.Logger.LogInfo($"Reviving {playerName} with {reviveHealth} HP...");

            // Call the game's built-in Revive method
            deadPlayer.Revive(false);

            // Set health to configured value
            if (deadPlayer.playerHealth != null)
            {
                deadPlayer.playerHealth.health = reviveHealth;
            }

            Medic.Logger.LogInfo($"Revived {playerName} successfully!");
        }
        catch (Exception ex)
        {
            Medic.Logger.LogError($"RevivePlayer exception: {ex.Message}");
        }
    }
}
