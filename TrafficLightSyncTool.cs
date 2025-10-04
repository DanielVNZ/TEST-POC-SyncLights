using Colossal.Logging;
using Game;
using Game.Common;
using Game.Input;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Tools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine.Scripting;

namespace SyncLights
{
    public partial class TrafficLightSyncTool : ObjectToolBaseSystem
    {
        public static TrafficLightSyncTool Instance { get; private set; }

        private readonly ILog log = LogManager.GetLogger($"{nameof(SyncLights)}.{nameof(TrafficLightSyncTool)}").SetShowsErrorsInUI(false);

        private NativeList<Entity> m_SelectedIntersections;
        private ComponentLookup<TrafficLights> m_TrafficLightsData;
        private Entity m_LastHoveredEntity = Entity.Null;

        public override string toolID => "Traffic Light Sync Tool";

        protected override void OnCreate()
        {
            base.OnCreate();
            Instance = this;
            log.Info("🔧 TrafficLightSyncTool OnCreate called");

            m_SelectedIntersections = new NativeList<Entity>(2, Allocator.Persistent);
            m_TrafficLightsData = GetComponentLookup<TrafficLights>();
        }

        protected override void OnDestroy()
        {
            if (m_SelectedIntersections.IsCreated)
            {
                m_SelectedIntersections.Dispose();
            }
            Instance = null;
            base.OnDestroy();
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_SelectedIntersections.Clear();
            m_LastHoveredEntity = Entity.Null;

            // Set up actions directly since we can't override UpdateActions
            base.applyAction.enabled = true;
            base.applyActionOverride = null; // Use the default apply action

            // Disable secondary apply and cancel actions since we don't need them
            base.secondaryApplyAction.enabled = false;
            base.secondaryApplyActionOverride = null;
            base.cancelAction.enabled = false;
            base.cancelActionOverride = null;

            log.Info("🚦 Traffic Light Sync Tool OnStartRunning - Click on intersections to select them (max 2)");
        }

        protected override void OnStopRunning()
        {
            log.Info("🛑 Traffic Light Sync Tool OnStopRunning");
            base.OnStopRunning();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            // Add a simple log to confirm OnUpdate is being called
            if (UnityEngine.Time.frameCount % 60 == 0) // Log every 60 frames to avoid spam
            {
                log.Info($"🔄 TrafficLightSyncTool OnUpdate called - Frame {UnityEngine.Time.frameCount}");
            }

            m_TrafficLightsData.Update(this);

            // Check for clicks using the existing apply action
            if (base.applyAction.WasPressedThisFrame())
            {
                log.Info("🖱️ CLICK DETECTED! Checking what was clicked...");

                if (GetRaycastResult(out Entity clickedEntity, out _))
                {
                    log.Info($"🎯 Clicked on entity: {clickedEntity.Index}");

                    // Check if the hit entity is a node with traffic lights
                    if (m_TrafficLightsData.HasComponent(clickedEntity))
                    {
                        log.Info($"🚦 Clicked on traffic light intersection {clickedEntity.Index}!");
                        SelectIntersection(clickedEntity);
                    }
                    else
                    {
                        log.Info($"❌ Clicked on entity {clickedEntity.Index} (not a traffic light intersection)");
                    }
                }
                else
                {
                    log.Info("❌ Clicked but raycast hit nothing");
                }
            }

            // Use our own raycast to detect what we're hovering over
            if (GetRaycastResult(out Entity hoveredEntity, out _))
            {
                // Check if this is a new entity we're hovering over
                if (hoveredEntity != m_LastHoveredEntity)
                {
                    m_LastHoveredEntity = hoveredEntity;
                    log.Info($"🎯 Hovering over entity: {hoveredEntity.Index}");

                    // Check if the hit entity is a node with traffic lights
                    if (m_TrafficLightsData.HasComponent(hoveredEntity))
                    {
                        log.Info($"🚦 Entity {hoveredEntity.Index} has TrafficLights component - this is a traffic light intersection!");
                    }
                    else
                    {
                        log.Info($"❌ Entity {hoveredEntity.Index} does NOT have TrafficLights component");
                    }
                }
            }
            else
            {
                // Not hovering over anything
                if (m_LastHoveredEntity != Entity.Null)
                {
                    log.Info("🔍 No longer hovering over any entity");
                    m_LastHoveredEntity = Entity.Null;
                }
            }

            return inputDeps;
        }

        private void SelectIntersection(Entity intersection)
        {
            // Check if already selected
            for (int i = 0; i < m_SelectedIntersections.Length; i++)
            {
                if (m_SelectedIntersections[i] == intersection)
                {
                    log.Info($"🔄 Intersection {intersection.Index} already selected");
                    return;
                }
            }

            // Add to selection if we have space
            if (m_SelectedIntersections.Length < 2)
            {
                m_SelectedIntersections.Add(intersection);
                log.Info($"✅ Selected intersection {intersection.Index} ({m_SelectedIntersections.Length}/2)");

                if (m_SelectedIntersections.Length == 2)
                {
                    log.Info("🎯 Two intersections selected! Press the hotkey again to save this pairing.");
                }
            }
            else
            {
                log.Info("⚠️ Maximum of 2 intersections can be selected. Clear selection first.");
            }
        }

        public bool HasSelectedIntersections()
        {
            return m_SelectedIntersections.Length > 0;
        }

        public NativeList<Entity> GetSelectedIntersections()
        {
            return m_SelectedIntersections;
        }

        public void SaveIntersectionPairing()
        {
            if (m_SelectedIntersections.Length == 2)
            {
                var pairing = new IntersectionPairing
                {
                    PrimaryIntersection = m_SelectedIntersections[0].Index,
                    SecondaryIntersection = m_SelectedIntersections[1].Index,
                    CreatedAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };

                SavePairingToFile(pairing);
                log.Info($"💾 Saved intersection pairing: {pairing.PrimaryIntersection} ↔ {pairing.SecondaryIntersection}");
            }
            else
            {
                log.Info("⚠️ Need exactly 2 intersections to save a pairing");
            }
        }

        private void SavePairingToFile(IntersectionPairing pairing)
        {
            try
            {
                // Use the correct path to LocalLow instead of Local/Low
                string localLowPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow");
                string modsDataPath = Path.Combine(localLowPath, "Colossal Order", "Cities Skylines II", "ModsData", "SyncLights");
                Directory.CreateDirectory(modsDataPath);

                string filePath = Path.Combine(modsDataPath, "intersection_pairings.txt");

                // Simple text format: PrimaryIntersection,SecondaryIntersection,CreatedAt
                string line = $"{pairing.PrimaryIntersection},{pairing.SecondaryIntersection},{pairing.CreatedAt}";

                // Append to file
                File.AppendAllText(filePath, line + Environment.NewLine);

                log.Info($"✅ Saved pairing to: {filePath}");
            }
            catch (Exception ex)
            {
                log.Error($"❌ Failed to save pairing: {ex.Message}");
            }
        }

        public void ClearSelection()
        {
            m_SelectedIntersections.Clear();
            m_LastHoveredEntity = Entity.Null;
            log.Info("🧹 Selection cleared");
        }

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();

            // Set up raycast to target network elements (roads, intersections)
            m_ToolRaycastSystem.typeMask = TypeMask.Net;
            m_ToolRaycastSystem.netLayerMask = Layer.Road;
        }

        // Required abstract method implementations
        public override PrefabBase GetPrefab()
        {
            return null; // This tool doesn't use prefabs
        }

        public override bool TrySetPrefab(PrefabBase prefab)
        {
            return false; // This tool doesn't use prefabs
        }
    }

    // Data class for intersection pairings
    public class IntersectionPairing
    {
        public int PrimaryIntersection { get; set; }
        public int SecondaryIntersection { get; set; }
        public string CreatedAt { get; set; }
    }
}