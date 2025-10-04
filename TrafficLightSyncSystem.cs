using Colossal.Logging;
using Game;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Simulation;
using Game.Tools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using Unity.Entities;

namespace SyncLights
{
    public partial class TrafficLightSyncSystem : GameSystemBase
    {
        public static TrafficLightSyncSystem Instance { get; private set; }

        private readonly ILog log = LogManager.GetLogger($"{nameof(SyncLights)}.{nameof(TrafficLightSyncSystem)}").SetShowsErrorsInUI(false);

        private ComponentLookup<TrafficLights> m_TrafficLightsData;
        private ComponentLookup<TrafficLight> m_TrafficLightData;
        private readonly List<IntersectionPairing> m_Pairings = new();
        private float m_LastFileCheckTime = 0f;
        private float m_LastSyncLogTime = 0f;
        private float m_LastDebugLogTime = 0f;
        private float m_LastTimerLogTime = 0f;
        private const float FILE_CHECK_INTERVAL = 2f;
        private const float SYNC_LOG_INTERVAL = 5f;
        private const float DEBUG_LOG_INTERVAL = 10f;
        private const float TIMER_LOG_INTERVAL = 1f;

        // PROPER STATE MACHINE SETTINGS
        private const float GROUP_DURATION = 5f; // 5 seconds per group
        private const float STATE_TRANSITION_TIME = 0.5f; // 0.5 seconds for state transitions
        private float m_LastCycleLogTime = 0f;
        private const float CYCLE_LOG_INTERVAL = 1f;

        // Track previous states for change detection
        private readonly Dictionary<Entity, TrafficLights> m_PreviousStates = new();

        // Track which entities we've already disabled vanilla system for
        private readonly HashSet<Entity> m_DisabledEntities = new();

        protected override void OnCreate()
        {
            base.OnCreate();
            Instance = this;
            log.Info("🔧 TrafficLightSyncSystem OnCreate called - PROPER STATE MACHINE MODE");

            m_TrafficLightsData = GetComponentLookup<TrafficLights>();
            m_TrafficLightData = GetComponentLookup<TrafficLight>();
        }

        protected override void OnUpdate()
        {
            m_TrafficLightsData.Update(this);
            m_TrafficLightData.Update(this);

            // Check for new pairings periodically
            if (UnityEngine.Time.time - m_LastFileCheckTime > FILE_CHECK_INTERVAL)
            {
                m_LastFileCheckTime = UnityEngine.Time.time;
                LoadPairingsFromFile();
            }

            // Apply PROPER STATE MACHINE synchronizations for all loaded pairings
            ApplyProperStateMachineSynchronizations();

            // Debug logging
            if (UnityEngine.Time.time - m_LastDebugLogTime > DEBUG_LOG_INTERVAL)
            {
                m_LastDebugLogTime = UnityEngine.Time.time;
                DebugLogSystemState();
            }

            // Timer logging
            if (UnityEngine.Time.time - m_LastTimerLogTime > TIMER_LOG_INTERVAL)
            {
                m_LastTimerLogTime = UnityEngine.Time.time;
                LogTimerStates();
            }
        }

        private void LogTimerStates()
        {
            if (m_Pairings.Count == 0) return;

            foreach (var pairing in m_Pairings)
            {
                Entity primaryEntity = FindEntityByIndex(pairing.PrimaryIntersection);
                Entity secondaryEntity = FindEntityByIndex(pairing.SecondaryIntersection);

                if (primaryEntity != Entity.Null && secondaryEntity != Entity.Null)
                {
                    var primaryLights = m_TrafficLightsData[primaryEntity];
                    var secondaryLights = m_TrafficLightsData[secondaryEntity];

                    log.Info($"⏱️ Primary {primaryEntity.Index}: State={primaryLights.m_State}, Timer={primaryLights.m_Timer}, Group={primaryLights.m_CurrentSignalGroup}");
                    log.Info($"⏱️ Secondary {secondaryEntity.Index}: State={secondaryLights.m_State}, Timer={secondaryLights.m_Timer}, Group={secondaryLights.m_CurrentSignalGroup}");
                }
            }
        }

        private void DebugLogSystemState()
        {
            log.Info($"🔍 DEBUG: System has {m_Pairings.Count} pairings loaded");

            if (m_Pairings.Count > 0)
            {
                var query = GetEntityQuery(
                    ComponentType.ReadWrite<TrafficLights>(),
                    ComponentType.ReadOnly<Node>(),
                    ComponentType.Exclude<Deleted>(),
                    ComponentType.Exclude<Destroyed>(),
                    ComponentType.Exclude<Temp>()
                );

                var entities = query.ToEntityArray(Allocator.Temp);
                log.Info($"🔍 DEBUG: Found {entities.Length} traffic light entities in the world");

                for (int i = 0; i < Math.Min(5, entities.Length); i++)
                {
                    log.Info($"🔍 DEBUG: Entity {i}: Index={entities[i].Index}");
                }

                foreach (var pairing in m_Pairings)
                {
                    bool primaryFound = false;
                    bool secondaryFound = false;

                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (entities[i].Index == pairing.PrimaryIntersection)
                            primaryFound = true;
                        if (entities[i].Index == pairing.SecondaryIntersection)
                            secondaryFound = true;
                    }

                    log.Info($"🔍 DEBUG: Pairing {pairing.PrimaryIntersection} ↔ {pairing.SecondaryIntersection}");
                    log.Info($"🔍 DEBUG: Primary {pairing.PrimaryIntersection} found: {primaryFound}");
                    log.Info($"🔍 DEBUG: Secondary {pairing.SecondaryIntersection} found: {secondaryFound}");
                }

                entities.Dispose();
            }
        }

        private void LoadPairingsFromFile()
        {
            try
            {
                // Yo Sam if you read this, this doesnt work. Idk how to make this persistant lol
                string localLowPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow");
                string modsDataPath = Path.Combine(localLowPath, "Colossal Order", "Cities Skylines II", "ModsData", "SyncLights");
                string filePath = Path.Combine(modsDataPath, "intersection_pairings.txt");

                if (!File.Exists(filePath))
                {
                    return;
                }

                string[] lines = File.ReadAllLines(filePath);
                var newPairings = new List<IntersectionPairing>();

                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    string[] parts = line.Split(',');
                    if (parts.Length >= 2)
                    {
                        if (int.TryParse(parts[0], out int primary) && int.TryParse(parts[1], out int secondary))
                        {
                            var pairing = new IntersectionPairing
                            {
                                PrimaryIntersection = primary,
                                SecondaryIntersection = secondary,
                                CreatedAt = parts.Length > 2 ? parts[2] : "Unknown"
                            };
                            newPairings.Add(pairing);
                        }
                    }
                }

                if (newPairings.Count != m_Pairings.Count || !newPairings.SequenceEqual(m_Pairings))
                {
                    m_Pairings.Clear();
                    m_Pairings.AddRange(newPairings);
                    log.Info($"📂 Loaded {m_Pairings.Count} intersection pairings from file");

                    foreach (var pairing in m_Pairings)
                    {
                        log.Info($"📋 Pairing: {pairing.PrimaryIntersection} ↔ {pairing.SecondaryIntersection}");
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, $"❌ Failed to load pairings: {ex.Message}");
            }
        }

        private void ApplyProperStateMachineSynchronizations()
        {
            if (m_Pairings.Count == 0) return;

            if (UnityEngine.Time.time - m_LastSyncLogTime > SYNC_LOG_INTERVAL)
            {
                m_LastSyncLogTime = UnityEngine.Time.time;
                log.Info($"🔄 Applying PROPER STATE MACHINE to {m_Pairings.Count} intersection pairings...");
            }

            foreach (var pairing in m_Pairings)
            {
                Entity primaryEntity = FindEntityByIndex(pairing.PrimaryIntersection);
                Entity secondaryEntity = FindEntityByIndex(pairing.SecondaryIntersection);

                if (primaryEntity != Entity.Null && secondaryEntity != Entity.Null)
                {
                    ApplyProperStateMachine(primaryEntity, secondaryEntity);
                }
                else
                {
                    if (UnityEngine.Time.time - m_LastSyncLogTime > SYNC_LOG_INTERVAL)
                    {
                        if (primaryEntity == Entity.Null)
                            log.Info($"⚠️ Primary intersection {pairing.PrimaryIntersection} not found");
                        if (secondaryEntity == Entity.Null)
                            log.Info($"⚠️ Secondary intersection {pairing.SecondaryIntersection} not found");
                    }
                }
            }
        }

        private Entity FindEntityByIndex(int entityIndex)
        {
            var query = GetEntityQuery(
                ComponentType.ReadWrite<TrafficLights>(),
                ComponentType.ReadOnly<Node>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Destroyed>(),
                ComponentType.Exclude<Temp>()
            );

            var entities = query.ToEntityArray(Allocator.Temp);
            Entity foundEntity = Entity.Null;

            for (int i = 0; i < entities.Length; i++)
            {
                if (entities[i].Index == entityIndex)
                {
                    foundEntity = entities[i];
                    break;
                }
            }

            entities.Dispose();
            return foundEntity;
        }

        private void ApplyProperStateMachine(Entity primary, Entity secondary)
        {
            if (!m_TrafficLightsData.HasComponent(primary) || !m_TrafficLightsData.HasComponent(secondary))
            {
                return;
            }

            // DISABLE VANILLA SYSTEM by removing UpdateFrame component (only once per entity)
            if (!m_DisabledEntities.Contains(primary))
            {
                if (EntityManager.HasComponent<UpdateFrame>(primary))
                {
                    EntityManager.RemoveComponent<UpdateFrame>(primary);
                    m_DisabledEntities.Add(primary);
                    log.Info($"🚫 Disabled vanilla system for primary {primary.Index}");
                }
            }

            if (!m_DisabledEntities.Contains(secondary))
            {
                if (EntityManager.HasComponent<UpdateFrame>(secondary))
                {
                    EntityManager.RemoveComponent<UpdateFrame>(secondary);
                    m_DisabledEntities.Add(secondary);
                    log.Info($"🚫 Disabled vanilla system for secondary {secondary.Index}");
                }
            }

            var primaryLights = m_TrafficLightsData[primary];
            var secondaryLights = m_TrafficLightsData[secondary];

            // Calculate which state we should be in based on time
            var (targetState, targetGroup, targetTimer) = CalculateTargetState();

            // Store previous states to detect changes
            var prevPrimaryState = primaryLights.m_State;
            var prevPrimaryGroup = primaryLights.m_CurrentSignalGroup;
            var prevSecondaryState = secondaryLights.m_State;
            var prevSecondaryGroup = secondaryLights.m_CurrentSignalGroup;

            // Apply the calculated state to both intersections
            bool needsUpdate = false;

            // Primary intersection
            if (primaryLights.m_State != targetState ||
                primaryLights.m_CurrentSignalGroup != targetGroup ||
                primaryLights.m_Timer != targetTimer)
            {
                primaryLights.m_State = targetState;
                primaryLights.m_CurrentSignalGroup = (byte)targetGroup;
                primaryLights.m_NextSignalGroup = (byte)(targetGroup == 1 ? 2 : 1);
                primaryLights.m_Timer = (byte)targetTimer;
                m_TrafficLightsData[primary] = primaryLights;
                needsUpdate = true;
            }

            // Secondary intersection - force to match primary exactly
            if (secondaryLights.m_State != targetState ||
                secondaryLights.m_CurrentSignalGroup != targetGroup ||
                secondaryLights.m_Timer != targetTimer)
            {
                secondaryLights.m_State = targetState;
                secondaryLights.m_CurrentSignalGroup = (byte)targetGroup;
                secondaryLights.m_NextSignalGroup = (byte)(targetGroup == 1 ? 2 : 1);
                secondaryLights.m_Timer = (byte)targetTimer;
                m_TrafficLightsData[secondary] = secondaryLights;
                needsUpdate = true;
            }

            // Update visual components and lane signals if state changed
            if (needsUpdate)
            {
                UpdateVisualComponents(primary, secondary);
            }

            // Log state changes
            if (prevPrimaryState != targetState || prevPrimaryGroup != targetGroup)
            {
                log.Info($"🔄 PRIMARY {primary.Index} STATE CHANGE: {prevPrimaryState} → {targetState}");
                log.Info($"⏱️ Timer: {targetTimer}, Group: {targetGroup}");
            }

            if (prevSecondaryState != targetState || prevSecondaryGroup != targetGroup)
            {
                log.Info($"🔄 SECONDARY {secondary.Index} STATE CHANGE: {prevSecondaryState} → {targetState}");
                log.Info($"⏱️ Timer: {targetTimer}, Group: {targetGroup}");
            }

            // Log cycle info periodically
            if (UnityEngine.Time.time - m_LastCycleLogTime > CYCLE_LOG_INTERVAL)
            {
                m_LastCycleLogTime = UnityEngine.Time.time;
                float cycleProgress = (UnityEngine.Time.time % (GROUP_DURATION * 2)) / (GROUP_DURATION * 2);
                float timeRemaining = (GROUP_DURATION * 2) - (UnityEngine.Time.time % (GROUP_DURATION * 2));
                log.Info($"🕐 STATE MACHINE: Group {targetGroup}, State {targetState}, Progress: {cycleProgress:P0}, Remaining: {timeRemaining:F1}s");
            }
        }

        private (Game.Net.TrafficLightState state, int group, int timer) CalculateTargetState()
        {
            float currentTime = UnityEngine.Time.time;

            // Calculate which group we should be in (alternates every GROUP_DURATION seconds)
            float cycleTime = currentTime % (GROUP_DURATION * 2); // Full cycle is 10 seconds (5s each group)
            int targetGroup = cycleTime < GROUP_DURATION ? 1 : 2;

            // Calculate state within the current group
            float groupTime = cycleTime % GROUP_DURATION;

            if (groupTime < STATE_TRANSITION_TIME)
            {
                // First 0.5s: Ending state (transitioning out of previous group)
                return (Game.Net.TrafficLightState.Ending, targetGroup, (int)(groupTime * 2)); // Timer 0-1
            }
            else if (groupTime < STATE_TRANSITION_TIME * 2)
            {
                // Next 0.5s: Changing state (all red)
                return (Game.Net.TrafficLightState.Changing, targetGroup, (int)((groupTime - STATE_TRANSITION_TIME) * 2)); // Timer 0-1
            }
            else if (groupTime < STATE_TRANSITION_TIME * 3)
            {
                // Next 0.5s: Beginning state (starting new group)
                return (Game.Net.TrafficLightState.Beginning, targetGroup, (int)((groupTime - STATE_TRANSITION_TIME * 2) * 2)); // Timer 0-1
            }
            else
            {
                // Remaining 3.5s: Ongoing state (main green/red phase)
                return (Game.Net.TrafficLightState.Ongoing, targetGroup, (int)((groupTime - STATE_TRANSITION_TIME * 3) * 2)); // Timer 0-7
            }
        }

        private void UpdateLaneSignals(Entity intersection)
        {
            // Get all lane signals connected to this intersection
            if (EntityManager.HasComponent<SubLane>(intersection))
            {
                var subLanes = EntityManager.GetBuffer<SubLane>(intersection, true);
                var trafficLights = m_TrafficLightsData[intersection];

                for (int i = 0; i < subLanes.Length; i++)
                {
                    Entity laneEntity = subLanes[i].m_SubLane;
                    if (EntityManager.HasComponent<LaneSignal>(laneEntity))
                    {
                        var laneSignal = EntityManager.GetComponentData<LaneSignal>(laneEntity);
                        Game.Simulation.TrafficLightSystem.UpdateLaneSignal(trafficLights, ref laneSignal);
                        EntityManager.SetComponentData(laneEntity, laneSignal);
                    }
                }
            }
        }

        private void UpdateVisualComponents(Entity primary, Entity secondary)
        {
            var primaryLights = m_TrafficLightsData[primary];
            var secondaryLights = m_TrafficLightsData[secondary];

            // Update lane signals (controls car movement)
            UpdateLaneSignals(primary);
            UpdateLaneSignals(secondary);

            // Update ALL traffic light visual objects (SubObjects)
            UpdateTrafficLightObjects(primary, primaryLights);
            UpdateTrafficLightObjects(secondary, secondaryLights);
        }

        private void UpdateTrafficLightObjects(Entity intersection, TrafficLights trafficLights)
        {
            // Get all SubObject entities (the actual visual traffic light objects)
            if (EntityManager.HasComponent<SubObject>(intersection))
            {
                var subObjects = EntityManager.GetBuffer<SubObject>(intersection, true);

                for (int i = 0; i < subObjects.Length; i++)
                {
                    Entity subObject = subObjects[i].m_SubObject;
                    if (EntityManager.HasComponent<TrafficLight>(subObject))
                    {
                        var trafficLight = EntityManager.GetComponentData<TrafficLight>(subObject);
                        Game.Simulation.TrafficLightSystem.UpdateTrafficLightState(trafficLights, ref trafficLight);
                        EntityManager.SetComponentData(subObject, trafficLight);
                    }
                }
            }
        }

        protected override void OnDestroy()
        {
            Instance = null;
            base.OnDestroy();
        }
    }
}