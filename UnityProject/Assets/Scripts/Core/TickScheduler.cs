// TickScheduler — multi-channel load-balanced simulation clock (WO-SYS-001).
//
// Five channels with independent cadences and frame budgets. Each system
// registers with a preferred channel and maximum delay tolerance. The
// scheduler assigns work to the channel with the most remaining budget
// within the system's delay window.
//
// Gated by FeatureFlags.UseTickScheduler.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Waystation.Core
{
    public class TickScheduler
    {
        // ── Channel definition ────────────────────────────────────────────
        public class Channel
        {
            public int Id;
            public string Name;
            public int DefaultCadence;
            public float BudgetMs;

            // Runtime state
            public float BudgetUsedMs;
            public int SystemsScheduled;
            public int SystemsDeferred;

            public float RemainingBudget => Mathf.Max(0f, BudgetMs - BudgetUsedMs);
            public float UsagePercent => BudgetMs > 0f ? BudgetUsedMs / BudgetMs : 0f;
        }

        // ── Configuration ─────────────────────────────────────────────────
        public float TargetFrameBudgetMs = 10f;
        public float BudgetWarningThreshold = 0.9f;
        public bool CostCalibrationEnabled = true;
        public int CostCalibrationWindowTicks = 100;

        // ── Channels ──────────────────────────────────────────────────────
        private readonly List<Channel> _channels = new List<Channel>();
        private readonly List<TickRegistration> _registrations = new List<TickRegistration>();
        private readonly Stopwatch _systemStopwatch = new Stopwatch();

        // Budget state snapshots for debug overlay
        private ChannelBudgetState[] _budgetSnapshot;

        public TickScheduler()
        {
            // Default 5-channel setup (can be overridden by LoadConfig)
            _channels.Add(new Channel { Id = 0, Name = "Immediate", DefaultCadence = 1,   BudgetMs = 4.0f });
            _channels.Add(new Channel { Id = 1, Name = "Fast",      DefaultCadence = 4,   BudgetMs = 2.5f });
            _channels.Add(new Channel { Id = 2, Name = "Medium",    DefaultCadence = 10,  BudgetMs = 1.5f });
            _channels.Add(new Channel { Id = 3, Name = "Slow",      DefaultCadence = 24,  BudgetMs = 1.2f });
            _channels.Add(new Channel { Id = 4, Name = "Weekly",    DefaultCadence = 168, BudgetMs = 0.8f });

            _budgetSnapshot = new ChannelBudgetState[5];
        }

        // ── Config loading ────────────────────────────────────────────────

        /// <summary>
        /// Loads channel definitions and budget allocations from SchedulerConfig.json.
        /// </summary>
        public void LoadConfig(string json)
        {
            if (string.IsNullOrEmpty(json)) return;

            var data = MiniJSON.Json.Deserialize(json) as Dictionary<string, object>;
            if (data == null) return;

            if (data.ContainsKey("channels") && data["channels"] is List<object> chList)
            {
                foreach (var item in chList)
                {
                    if (item is not Dictionary<string, object> chData) continue;

                    // Match channel by "id" field; fall back to positional if no id present
                    Channel ch = null;
                    if (chData.ContainsKey("id"))
                    {
                        int configId = Convert.ToInt32(chData["id"]);
                        ch = _channels.Find(c => c.Id == configId);
                    }
                    if (ch == null) continue;

                    if (chData.ContainsKey("default_cadence"))
                        ch.DefaultCadence = Convert.ToInt32(chData["default_cadence"]);
                    if (chData.ContainsKey("budget_ms"))
                        ch.BudgetMs = Convert.ToSingle(chData["budget_ms"]);
                    if (chData.ContainsKey("name"))
                        ch.Name = chData["name"].ToString();
                }
            }

            if (data.ContainsKey("target_frame_budget_ms"))
                TargetFrameBudgetMs = Convert.ToSingle(data["target_frame_budget_ms"]);
            if (data.ContainsKey("budget_warning_threshold"))
                BudgetWarningThreshold = Convert.ToSingle(data["budget_warning_threshold"]);
            if (data.ContainsKey("cost_calibration_enabled"))
                CostCalibrationEnabled = Convert.ToBoolean(data["cost_calibration_enabled"]);
            if (data.ContainsKey("cost_calibration_window_ticks"))
                CostCalibrationWindowTicks = Convert.ToInt32(data["cost_calibration_window_ticks"]);
        }

        // ── Registration ──────────────────────────────────────────────────

        public void Register(TickRegistration reg)
        {
            if (reg == null || string.IsNullOrEmpty(reg.SystemId)) return;

            // Remove existing registration with same ID
            _registrations.RemoveAll(r => r.SystemId == reg.SystemId);

            reg.CalibratedCostMs = reg.EstimatedCostMs;
            reg.CalibrationSamples = 0;
            _registrations.Add(reg);

            Debug.Log($"[TickScheduler] Registered system '{reg.SystemId}' on channel {reg.PreferredChannel} " +
                      $"(max delay: {reg.MaxDelayTicks}, est: {reg.EstimatedCostMs:F1}ms)");
        }

        public void Unregister(string systemId)
        {
            _registrations.RemoveAll(r => r.SystemId == systemId);
        }

        public int RegistrationCount => _registrations.Count;

        // ── Tick ──────────────────────────────────────────────────────────

        /// <summary>
        /// Run one scheduler tick. Called from GameManager.AdvanceTick().
        /// Evaluates which systems are due and assigns them to channels based on budget.
        /// </summary>
        public void Tick(int currentTick)
        {
            // Reset channel budgets
            foreach (var ch in _channels)
            {
                ch.BudgetUsedMs = 0f;
                ch.SystemsScheduled = 0;
                ch.SystemsDeferred = 0;
            }

            // Evaluate each registered system
            foreach (var reg in _registrations)
            {
                int preferredChannel = Mathf.Clamp(reg.PreferredChannel, 0, _channels.Count - 1);
                var preferred = _channels[preferredChannel];
                int cadence = preferred.DefaultCadence;

                // Is this system due?
                int ticksSinceLastRun = currentTick - reg.LastExecutedTick;
                if (ticksSinceLastRun < cadence) continue;

                // Try preferred channel first
                float cost = CostCalibrationEnabled ? reg.CalibratedCostMs : reg.EstimatedCostMs;

                if (preferred.RemainingBudget >= cost)
                {
                    ExecuteSystem(reg, preferred, currentTick);
                }
                else
                {
                    // Find alternative channel with budget within MaxDelayTicks
                    bool assigned = false;
                    if (ticksSinceLastRun < cadence + reg.MaxDelayTicks)
                    {
                        // Try to find a channel with budget
                        for (int i = 0; i < _channels.Count; i++)
                        {
                            if (i == preferredChannel) continue;
                            if (_channels[i].RemainingBudget >= cost)
                            {
                                ExecuteSystem(reg, _channels[i], currentTick);
                                assigned = true;
                                break;
                            }
                        }
                    }

                    if (!assigned)
                    {
                        // MaxDelayTicks exceeded — force onto least-loaded channel
                        if (ticksSinceLastRun >= cadence + reg.MaxDelayTicks)
                        {
                            Channel leastLoaded = preferred;
                            float maxRemaining = preferred.RemainingBudget;
                            foreach (var ch in _channels)
                            {
                                if (ch.RemainingBudget > maxRemaining)
                                {
                                    leastLoaded = ch;
                                    maxRemaining = ch.RemainingBudget;
                                }
                            }
                            ExecuteSystem(reg, leastLoaded, currentTick);
                            Debug.LogWarning(
                                $"[TickScheduler] Budget warning: '{reg.SystemId}' forced onto " +
                                $"channel {leastLoaded.Id} ({leastLoaded.Name}). " +
                                $"MaxDelayTicks ({reg.MaxDelayTicks}) exceeded.");
                        }
                        else
                        {
                            // Defer to next tick
                            preferred.SystemsDeferred++;
                        }
                    }
                }
            }

            // Snapshot for debug overlay
            UpdateBudgetSnapshot();

            // Warn on high budget usage
            foreach (var ch in _channels)
            {
                if (ch.BudgetMs > 0 && ch.UsagePercent >= BudgetWarningThreshold)
                {
                    Debug.LogWarning(
                        $"[TickScheduler] Channel {ch.Id} ({ch.Name}) at " +
                        $"{ch.UsagePercent:P0} budget usage ({ch.BudgetUsedMs:F2}ms / {ch.BudgetMs:F1}ms)");
                }
            }
        }

        private void ExecuteSystem(TickRegistration reg, Channel channel, int currentTick)
        {
            _systemStopwatch.Restart();

            try
            {
                reg.OnTick?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TickScheduler] System '{reg.SystemId}' threw: {ex.Message}");
            }

            _systemStopwatch.Stop();
            float actualMs = (float)_systemStopwatch.Elapsed.TotalMilliseconds;

            channel.BudgetUsedMs += actualMs;
            channel.SystemsScheduled++;
            reg.LastExecutedTick = currentTick;

            // Cost calibration
            if (CostCalibrationEnabled)
            {
                reg.CalibrationSamples++;
                if (reg.CalibrationSamples <= CostCalibrationWindowTicks)
                {
                    // Running average
                    float weight = 1f / reg.CalibrationSamples;
                    reg.CalibratedCostMs = reg.CalibratedCostMs * (1f - weight) + actualMs * weight;
                }
            }

            // Warn on significant overrun
            if (actualMs > reg.EstimatedCostMs * 2f)
            {
                Debug.LogWarning(
                    $"[TickScheduler] System '{reg.SystemId}' cost overrun: " +
                    $"{actualMs:F2}ms actual vs {reg.EstimatedCostMs:F1}ms declared");
            }
        }

        // ── Budget monitoring ─────────────────────────────────────────────

        private void UpdateBudgetSnapshot()
        {
            if (_budgetSnapshot == null || _budgetSnapshot.Length != _channels.Count)
                _budgetSnapshot = new ChannelBudgetState[_channels.Count];

            for (int i = 0; i < _channels.Count; i++)
            {
                _budgetSnapshot[i] = new ChannelBudgetState
                {
                    ChannelId = _channels[i].Id,
                    Name = _channels[i].Name,
                    BudgetAllocatedMs = _channels[i].BudgetMs,
                    BudgetUsedMs = _channels[i].BudgetUsedMs,
                    SystemsScheduled = _channels[i].SystemsScheduled,
                    SystemsDeferred = _channels[i].SystemsDeferred,
                };
            }
        }

        /// <summary>Returns a snapshot of current channel budget states for debug overlay.</summary>
        public ChannelBudgetState[] GetBudgetSnapshot()
        {
            return _budgetSnapshot ?? Array.Empty<ChannelBudgetState>();
        }

        /// <summary>Returns channel count.</summary>
        public int ChannelCount => _channels.Count;
    }
}
