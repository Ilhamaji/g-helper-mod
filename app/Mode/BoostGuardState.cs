namespace GHelper.Mode
{
    // The decision half of the boost guard: given what the machine is doing right now and
    // the current time, say which boost mode should be active. It holds no timer, touches
    // no power setting, and reads no config, so the debounce behaviour can be exercised
    // without a laptop or a power scheme.
    internal sealed class BoostGuardState
    {
        private bool isGame;
        private long gameSince;
        private long desktopSince;

        // -1 means nothing has been applied yet.
        public int AppliedBoost { get; private set; } = -1;
        public bool IsGameActive => isGame;

        public void Reset(long now)
        {
            isGame = false;
            gameSince = 0;
            desktopSince = now;
            AppliedBoost = -1;
        }

        // Used when the guard applies a value directly (startup, setting change) rather than
        // through a transition, so the state machine knows what is already in effect.
        public void ForceApplied(int boost)
        {
            AppliedBoost = boost;
        }

        // Same idea, but also pins which side the machine is on. Needed when the user
        // changes a guard setting mid-game: the answer must be recomputed now instead of
        // falling back to the idle mode just because the debounce had not latched yet.
        public void ForceState(bool game, int boost, long now)
        {
            isGame = game;
            gameSince = game ? now : 0;
            desktopSince = game ? 0 : now;
            AppliedBoost = boost;
        }

        // Returns the boost mode to write, or null when the current one is already correct.
        public int? Update(bool game, long now, int gameBoost, int idleBoost, int gameDelayMs, int idleDelayMs)
        {
            if (game)
            {
                desktopSince = 0;
                if (gameSince == 0) gameSince = now;

                // Entering a game waits for the window to settle, so a launcher that closes
                // again two seconds later does not spend a boost cycle.
                if (!isGame && now - gameSince >= gameDelayMs)
                {
                    isGame = true;
                    return Commit(gameBoost);
                }
            }
            else
            {
                gameSince = 0;
                if (desktopSince == 0) desktopSince = now;

                // Leaving a game waits longer: alt-tabbing to a browser or a chat window for
                // a few seconds is not the idle condition the freeze needs.
                if (isGame && now - desktopSince >= idleDelayMs)
                {
                    isGame = false;
                    return Commit(idleBoost);
                }
            }

            // Already on the game side and the wanted value changed: another listed game with
            // a different override took over, so apply it now instead of waiting for a
            // game-to-idle transition that may never come.
            if (isGame && gameBoost != AppliedBoost) return Commit(gameBoost);

            return null;
        }

        private int? Commit(int boost)
        {
            if (boost == AppliedBoost) return null;
            AppliedBoost = boost;
            return boost;
        }
    }
}
