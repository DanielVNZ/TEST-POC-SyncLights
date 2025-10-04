using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Input;
using Game.Modding;
using Game.SceneFlow;
using Game.Tools;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Entities;

namespace SyncLights
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(SyncLights)}.{nameof(Mod)}").SetShowsErrorsInUI(false);
        private Setting m_Setting;
        public static ProxyAction m_ToggleToolAction;
        public static ProxyAction m_ClearAction;

        public const string kToggleToolActionName = "ToggleSyncTool";
        public const string kClearActionName = "ClearSelection";

        private bool m_IsToolActive = false;
        private UpdateSystem m_UpdateSystem;

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info("=== SyncLights Mod Loading ===");

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                log.Info($"Current mod asset at {asset.path}");

            m_Setting = new Setting(this);
            m_Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(m_Setting));

            m_Setting.RegisterKeyBindings();

            m_ToggleToolAction = m_Setting.GetAction(kToggleToolActionName);
            m_ClearAction = m_Setting.GetAction(kClearActionName);

            m_ToggleToolAction.shouldBeEnabled = true;
            m_ClearAction.shouldBeEnabled = true;

            m_ToggleToolAction.onInteraction += OnToggleTool;
            m_ClearAction.onInteraction += OnClearSelection;

            AssetDatabase.global.LoadSettings(nameof(SyncLights), m_Setting, new Setting(this));

            // Store the update system reference
            m_UpdateSystem = updateSystem;

            // Register the traffic light sync system and tool
            updateSystem.UpdateAt<TrafficLightSyncSystem>(SystemUpdatePhase.PostSimulation);
            updateSystem.UpdateAt<TrafficLightSyncTool>(SystemUpdatePhase.PreTool); // Back to PreTool for proper tool integration

            log.Info("✅ Registered TrafficLightSyncSystem and TrafficLightSyncTool in update system");

            log.Info("=== SyncLights Mod Loaded Successfully ===");
            log.Info($"Toggle Tool Key: {kToggleToolActionName}");
            log.Info($"Clear Selection Key: {kClearActionName}");
        }

        private void OnToggleTool(ProxyAction action, InputActionPhase phase)
        {
            if (phase == InputActionPhase.Performed)
            {
                log.Info($"=== TOGGLE TOOL KEY PRESSED ===");
                log.Info($"Current tool active state: {m_IsToolActive}");

                if (!m_IsToolActive)
                {
                    log.Info("Attempting to activate Traffic Light Sync tool...");
                    ActivateSyncTool();
                    m_IsToolActive = true;
                    log.Info("✅ Traffic Light Sync tool ACTIVATED");
                    log.Info("📝 Instructions: Click on traffic light intersections to select them (max 2)");
                }
                else
                {
                    log.Info("Attempting to save intersection pairing and deactivate tool...");

                    // Save the pairing before deactivating
                    TrafficLightSyncTool.Instance?.SaveIntersectionPairing();

                    DeactivateSyncTool();
                    m_IsToolActive = false;
                    log.Info("❌ Traffic Light Sync tool DEACTIVATED");
                }
            }
        }

        private void OnClearSelection(ProxyAction action, InputActionPhase phase)
        {
            if (phase == InputActionPhase.Performed)
            {
                log.Info("=== CLEAR SELECTION KEY PRESSED ===");
                TrafficLightSyncTool.Instance?.ClearSelection();
            }
        }

        private void ActivateSyncTool()
        {
            log.Info("🔧 Activating sync tool...");

            // Use the correct way to get the world reference
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                log.Error("❌ Failed to get world reference!");
                return;
            }

            log.Info("✅ World reference obtained");

            var toolSystem = world.GetOrCreateSystemManaged<ToolSystem>();
            var syncTool = world.GetOrCreateSystemManaged<TrafficLightSyncTool>();

            if (toolSystem == null)
            {
                log.Error("❌ Failed to get ToolSystem!");
                return;
            }

            if (syncTool == null)
            {
                log.Error("❌ Failed to get TrafficLightSyncTool!");
                return;
            }

            log.Info("✅ Both tool systems obtained successfully");
            log.Info("✅ toolSystem: " + toolSystem);
            log.Info("✅ syncTool: " + syncTool);

            // Set our tool as the active tool (like the traffic lights upgrade tool does)
            toolSystem.activeTool = syncTool;
            log.Info("🎯 Traffic Light Sync tool is now the active tool");
        }

        private void DeactivateSyncTool()
        {
            log.Info("🔧 Deactivating sync tool...");

            TrafficLightSyncTool.Instance?.ClearSelection();

            // Get the world from the update system
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                log.Error("❌ Failed to get world reference for deactivation!");
                return;
            }

            var toolSystem = world.GetOrCreateSystemManaged<ToolSystem>();
            var defaultTool = world.GetOrCreateSystemManaged<DefaultToolSystem>();

            if (toolSystem != null && defaultTool != null)
            {
                toolSystem.activeTool = defaultTool;
                log.Info("✅ Switched back to default tool");
            }
            else
            {
                log.Error("❌ Failed to get tool systems for deactivation!");
            }
        }

        public void OnDispose()
        {
            log.Info("=== SyncLights Mod Disposing ===");
            if (m_Setting != null)
            {
                m_Setting.UnregisterInOptionsUI();
                m_Setting = null;
            }
            log.Info("=== SyncLights Mod Disposed ===");
        }
    }
}