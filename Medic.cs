using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Medic;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class Medic : BaseUnityPlugin
{
    private const string PluginGuid = "headclef.Medic";
    private const string PluginName = "Medic";
    private const string PluginVersion = "1.1.2";

    internal static Medic Instance { get; private set; } = null!;
    internal new static ManualLogSource Logger => Instance._logger;
    private ManualLogSource _logger => base.Logger;
    internal Harmony? Harmony { get; set; }

    // ── Config ──
    internal static ConfigEntry<KeyboardShortcut> ReviveKey = null!;
    internal static ConfigEntry<float> ReviveRange = null!;
    internal static ConfigEntry<float> ReviveCooldown = null!;
    internal static ConfigEntry<int> ReviveHealth = null!;

    internal static ConfigEntry<bool> SelfReviveEnabled = null!;
    internal static ConfigEntry<float> SelfReviveCooldown = null!;
    internal static ConfigEntry<int> SelfReviveHealth = null!;

    private void Awake()
    {
        Instance = this;
        this.gameObject.transform.parent = null;
        this.gameObject.hideFlags = HideFlags.HideAndDontSave;

        BindConfiguration();
        Harmony ??= new Harmony(Info.Metadata.GUID);
        Harmony.PatchAll();

        Logger.LogInfo($"{Info.Metadata.GUID} v{Info.Metadata.Version} has loaded!");
    }

    private void OnDestroy()
    {
        Harmony?.UnpatchSelf();
    }

    private void BindConfiguration()
    {
        const string section = "Revive";

        ReviveKey = Config.Bind(section, "Revive Key", new KeyboardShortcut(KeyCode.R),
            "Key to press to revive a nearby dead player.");

        ReviveRange = Config.Bind(section, "Revive Range", 3f,
            new ConfigDescription(
                "Maximum distance to a dead player to revive them.",
                new AcceptableValueRange<float>(1f, 15f)));

        ReviveCooldown = Config.Bind(section, "Revive Cooldown", 5f,
            new ConfigDescription(
                "Cooldown in seconds between revive attempts.",
                new AcceptableValueRange<float>(0f, 60f)));

        ReviveHealth = Config.Bind(section, "Revive Health", 100,
            new ConfigDescription(
                "Health the revived player starts with.",
                new AcceptableValueRange<int>(1, 200)));

        const string selfSection = "Self Revive";

        SelfReviveEnabled = Config.Bind(selfSection, "Enabled", true,
            "Allow self-reviving when you are dead by pressing the revive key.");

        SelfReviveCooldown = Config.Bind(selfSection, "Self Revive Cooldown", 10f,
            new ConfigDescription(
                "Cooldown in seconds between self-revive attempts.",
                new AcceptableValueRange<float>(0f, 120f)));

        SelfReviveHealth = Config.Bind(selfSection, "Self Revive Health", 50,
            new ConfigDescription(
                "Health you start with after self-reviving.",
                new AcceptableValueRange<int>(1, 200)));
    }
}
