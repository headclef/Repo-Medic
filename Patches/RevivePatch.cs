using System;
using System.Collections.Generic;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace Medic.Patches;

[HarmonyPatch]
internal static class RevivePatch
{
    private static float _lastReviveTime;
    private static float _lastSelfReviveTime;

    /// <summary>
    /// Prefix on PlayerAvatar.ReviveRPC — bypasses the MasterOnlyRPC check
    /// so that any client (not just the host) can trigger a revive.
    /// Without this patch, non-host revive calls are silently rejected by
    /// the game's SemiFunc.MasterOnlyRPC validation.
    /// </summary>
    [HarmonyPatch(typeof(PlayerAvatar), "ReviveRPC")]
    [HarmonyPrefix]
    private static bool ReviveRPC_Prefix(PlayerAvatar __instance, bool _revivedByTruck, PhotonMessageInfo _info)
    {
        // In singleplayer _info is default — let the original method handle it
        if (!SemiFunc.IsMultiplayer())
            return true;

        // If the sender IS the master client, let the original run as normal
        // Harmony003 is a false positive below: _info.Sender is only read, never modified.
#pragma warning disable Harmony003
        if (_info.Sender == PhotonNetwork.MasterClient)
            return true;

        // Non-master sender: run the revive logic ourselves, skipping MasterOnlyRPC
        try
        {
            Medic.Logger.LogDebug($"Bypassing MasterOnlyRPC for revive from {_info.Sender?.NickName ?? "unknown"}");
#pragma warning restore Harmony003

            // Replicate the game's ReviveRPC body (minus the master check)
            if (!__instance.playerDeathHead)
            {
                Medic.Logger.LogWarning("Tried to revive without death head.");
                return false;
            }

            var position = __instance.playerDeathHead.physGrabObject.centerPoint - Vector3.up * 0.25f;
            var eulerAngles = __instance.playerDeathHead.physGrabObject.transform.eulerAngles;

            if (SemiFunc.RunIsTutorial())
            {
                position = Vector3.zero + Vector3.up * 2f - Vector3.right * 5f;
                __instance.playerDeathHead.transform.position = position;
            }

            if (SemiFunc.IsMasterClientOrSingleplayer())
            {
                __instance.tumble.physGrabObject.Teleport(position, __instance.transform.rotation);
            }

            __instance.transform.position = position;
            __instance.clientPositionCurrent = __instance.transform.position;
            __instance.clientPosition = __instance.transform.position;
            __instance.clientPhysRiding = false;
            __instance.gameObject.SetActive(true);
            __instance.playerAvatarVisuals.gameObject.SetActive(true);
            __instance.playerAvatarVisuals.transform.position = __instance.transform.position;
            __instance.playerAvatarVisuals.visualPosition = __instance.transform.position;
            __instance.playerAvatarVisuals.Revive();
            __instance.isDisabled = false;
            __instance.playerDeathHead.Reset();
            __instance.playerDeathEffects.Reset();
            __instance.playerReviveEffects.Trigger();
            __instance.deadSet = false;
            __instance.deadTimer = __instance.deadTime;

            if ((bool)__instance.voiceChat)
            {
                __instance.voiceChat.ToggleMixer(false);
            }

            __instance.playerAvatarCollision.SetCrouch();
            __instance.playerHealth.SetMaterialGreen();

            if (__instance.isLocal)
            {
                __instance.playerHealth.HealOther(1, true);
                __instance.playerTransform.position = __instance.transform.position;
                __instance.playerTransform.parent.gameObject.SetActive(true);

                if (!SpectateCamera.instance || !SpectateCamera.instance.CheckState(SpectateCamera.State.Head))
                {
                    CameraAim.Instance.SetPlayerAim(Quaternion.Euler(0f, eulerAngles.y, 0f), true);
                }

                CameraPosition.instance.transform.position = position;
                CameraAim.Instance.OverrideNoSmooth(0.25f);
                GameDirector.instance.Revive();
                SpectateCamera.instance.StopSpectate();
                PlayerController.instance.Revive(eulerAngles);
                CameraGlitch.Instance.PlayLongHeal();
            }

            __instance.RoomVolumeCheck.CheckSet();
        }
        catch (Exception ex)
        {
            Medic.Logger.LogError($"ReviveRPC bypass exception: {ex.Message}");
        }

        // Skip the original method — we handled it
        return false;
    }

    /// <summary>
    /// Postfix on SpectateCamera.Update — listens for the revive key while
    /// the local player is dead and spectating, since PlayerController's
    /// GameObject is deactivated on death and its Update never runs.
    /// </summary>
    [HarmonyPatch(typeof(SpectateCamera), "Update")]
    [HarmonyPostfix]
    private static void SpectateCamera_Update_Postfix()
    {
        try
        {
            if (!Medic.SelfReviveEnabled.Value)
                return;

            if (!Medic.ReviveKey.Value.IsDown())
                return;

            var localAvatar = PlayerController.instance?.playerAvatarScript;
            if (localAvatar == null || !localAvatar.deadSet)
                return;

            float selfCooldown = Medic.SelfReviveCooldown.Value;
            if (Time.time - _lastSelfReviveTime < selfCooldown)
            {
                Medic.Logger.LogDebug($"Self-revive on cooldown ({selfCooldown - (Time.time - _lastSelfReviveTime):F1}s remaining)");
                return;
            }

            SelfRevive(localAvatar);
            _lastSelfReviveTime = Time.time;
        }
        catch (Exception ex)
        {
            Medic.Logger.LogError($"Self-revive (spectate) exception: {ex.Message}");
        }
    }

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

            // Get the local player's avatar
            var localAvatar = __instance.playerAvatarScript;
            if (localAvatar == null)
                return;

            // Skip if dead — self-revive is handled by SpectateCamera_Update_Postfix
            if (localAvatar.deadSet)
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

            // Call the game's built-in Revive method — sends ReviveRPC to all clients.
            // Our ReviveRPC_Prefix patch ensures this works even when we are not the host.
            deadPlayer.Revive(false);

            // Set health to configured value and sync over network
            if (deadPlayer.playerHealth != null)
            {
                deadPlayer.playerHealth.health = reviveHealth;
                deadPlayer.playerHealth.maxHealth = Mathf.Max(deadPlayer.playerHealth.maxHealth, reviveHealth);

                if (SemiFunc.IsMultiplayer())
                {
                    deadPlayer.playerHealth.photonView.RPC(
                        "UpdateHealthRPC", RpcTarget.Others,
                        reviveHealth, deadPlayer.playerHealth.maxHealth, true);
                }
            }

            Medic.Logger.LogInfo($"Revived {playerName} successfully!");
        }
        catch (Exception ex)
        {
            Medic.Logger.LogError($"RevivePlayer exception: {ex.Message}");
        }
    }

    private static void SelfRevive(PlayerAvatar localAvatar)
    {
        try
        {
            int selfReviveHealth = Medic.SelfReviveHealth.Value;
            string playerName = localAvatar.playerName ?? "Unknown";

            Medic.Logger.LogInfo($"Self-reviving {playerName} with {selfReviveHealth} HP...");

            // Call the game's built-in Revive method — sends ReviveRPC to all clients.
            // Our ReviveRPC_Prefix patch ensures this works even when we are not the host.
            localAvatar.Revive(false);

            // Set health to configured value and sync over network
            if (localAvatar.playerHealth != null)
            {
                localAvatar.playerHealth.health = selfReviveHealth;
                localAvatar.playerHealth.maxHealth = Mathf.Max(localAvatar.playerHealth.maxHealth, selfReviveHealth);

                if (SemiFunc.IsMultiplayer())
                {
                    localAvatar.playerHealth.photonView.RPC(
                        "UpdateHealthRPC", RpcTarget.Others,
                        selfReviveHealth, localAvatar.playerHealth.maxHealth, true);
                }
            }

            Medic.Logger.LogInfo($"Self-revived {playerName} successfully!");
        }
        catch (Exception ex)
        {
            Medic.Logger.LogError($"SelfRevive exception: {ex.Message}");
        }
    }
}
