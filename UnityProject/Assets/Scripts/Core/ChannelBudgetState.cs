// ChannelBudgetState — per-channel budget monitoring for the TickScheduler.
namespace Waystation.Core
{
    public struct ChannelBudgetState
    {
        public int ChannelId;
        public string Name;
        public float BudgetAllocatedMs;
        public float BudgetUsedMs;
        public int SystemsScheduled;
        public int SystemsDeferred;

        public float UsagePercent => BudgetAllocatedMs > 0f
            ? BudgetUsedMs / BudgetAllocatedMs
            : 0f;
    }
}
