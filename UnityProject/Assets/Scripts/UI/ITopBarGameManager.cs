// ITopBarGameManager — abstraction over GameManager for the TopBarController.
//
// Introduced WO-UI-004 to allow TopBarController to be unit-tested without
// requiring a live MonoBehaviour.
//
// GameManager implements this interface directly.
// Tests supply a StubGameManager that also implements it.
namespace Waystation.UI
{
    /// <summary>
    /// Minimal game-manager surface required by <see cref="TopBarController"/>.
    /// </summary>
    public interface ITopBarGameManager
    {
        /// <summary>Whether the game tick is currently paused.</summary>
        bool  IsPaused       { get; set; }

        /// <summary>Current real-time seconds per game tick.</summary>
        float SecondsPerTick { get; }

        /// <summary>Sets the tick rate (ticks per second).</summary>
        void SetSpeed(float ticksPerSecond);
    }
}
