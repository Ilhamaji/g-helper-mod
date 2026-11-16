using System.Diagnostics;

namespace GHelper.Mode
{
    // Keeps the CPU boost mode matched to the games the user selected by hand.
    //
    // The problem this solves: on some Intel laptops a boost mode above "Disabled" is
    // stable while a game is running and unstable the moment the machine goes idle. The
    // boost request stays armed, the cores keep dropping to and jumping out of deep idle,
    // and the resulting voltage/frequency transient hangs the machine. Turning boost off
    // for good fixes the freeze but also removes the performance the game needs.
    //
    // Detection is deliberately manual: only a process named in the user's game list counts
    // as a game. The guard never guesses from the foreground window, a fullscreen check, or
    // CPU load, so it can not turn boost/turbo on for something the user did not pick. That
    // also means no window enumeration and no scanning of other processes.
    internal class BoostGuard
    {
        private const int DefaultIdleBoost = 0; // Disabled
        private const int DefaultGameBoost = 2; // Aggressive
        private const int TickMs = 500;

        private static System.Timers.Timer? timer;
        private static readonly BoostGuardState state = new();
        private static readonly object sync = new();

        // The timer keeps firing every 500ms whether or not the previous pass finished, so
        // only one detection pass may run at a time.
        private static int tickRunning;

        // The list entry currently holding the boost, for the status line.
        private static string activeGame = "";

        // Raised after the guard writes a boost mode, so the UI can show the live state
        // without polling the power setting on a timer.
        public static event Action? Applied;

        // Raised when the set of things the guard considers a game changes, which is not the
        // same moment as a boost write: a game can be detected while the boost value stays
        // the same. The UI needs this to keep its running-app picker and status in sync.
        public static event Action? StateChanged;

        public static int IdleBoost => AppConfig.Get("boost_guard_idle", DefaultIdleBoost);
        public static int GameBoost => AppConfig.Get("boost_guard_game", DefaultGameBoost);
        public static int GameDelay => Math.Max(AppConfig.Get("boost_guard_game_delay", 3), 0);
        // Two seconds: enough that a game restarting its process does not flap the setting.
        public static int IdleDelay => Math.Max(AppConfig.Get("boost_guard_idle_delay", 2), 0);

        public static bool IsEnabled => AppConfig.Is("boost_guard");
        public static bool IsGameActive => state.IsGameActive;
        public static int AppliedBoost => state.AppliedBoost;

        // The game currently holding the boost, for the status line.
        public static string ActiveGame => activeGame;

        // The user-managed watch list. A process named here counts as a game whenever it is
        // running, whether or not it is in front and whether or not it is fullscreen. This
        // is the only source of detection: nothing is ever added automatically.
        public static string[] GameList =>
            (AppConfig.GetString("boost_guard_games") ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        public static void SetGameList(IEnumerable<string> names)
        {
            string value = string.Join(",", names
                .Where(n => !string.IsNullOrWhiteSpace(n))
                // Process names are matched without the extension, and a user typing
                // "game.exe" means the same process as "game".
                .Select(n => Normalize(n))
                .Where(n => n.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase));

            AppConfig.Set("boost_guard_games", value);

            // Drop per-game overrides for games that left the list, so a removed game does
            // not come back with its old boost when it is added again.
            var kept = GameModes().Where(kv => GameList.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)).ToList();
            AppConfig.Set("boost_guard_game_modes", string.Join(",", kept.Select(kv => kv.Key + ":" + kv.Value)));

            Logger.WriteLine("Boost guard games: " + value);

            // A list edit can change the answer immediately, so re-evaluate rather than
            // waiting up to two seconds for the next tick.
            Refresh();
        }

        // Per-game boost overrides, stored as "name:mode" pairs. A game without an entry
        // uses GameBoost, so the table only records exceptions.
        static Dictionary<string, int> GameModes()
        {
            var modes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string entry in (AppConfig.GetString("boost_guard_game_modes") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = entry.LastIndexOf(':');
                if (colon <= 0) continue;
                if (!int.TryParse(entry[(colon + 1)..], out int mode) || mode < 0) continue;
                modes[entry[..colon].Trim()] = mode;
            }
            return modes;
        }

        // -1 means "same as the global game boost".
        public static int GameMode(string name) =>
            GameModes().TryGetValue(name, out int mode) ? mode : -1;

        public static void SetGameMode(string name, int mode)
        {
            var modes = GameModes();
            if (mode < 0) modes.Remove(name);
            else modes[name] = mode;
            AppConfig.Set("boost_guard_game_modes", string.Join(",", modes.Select(kv => kv.Key + ":" + kv.Value)));
            Refresh();
        }

        // The boost the running game should get: its own pick when it has one.
        static int ActiveGameBoost() => GameMode(activeGame) is int mode && mode >= 0 ? mode : GameBoost;

        private static string Normalize(string name)
        {
            string trimmed = name.Trim().Trim('"');

            // Some users paste a full path; the process name is the last segment.
            int slash = trimmed.LastIndexOfAny(new[] { '\\', '/' });
            if (slash >= 0) trimmed = trimmed[(slash + 1)..];

            if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[..^4];

            return trimmed.Trim();
        }

        public static void AddGame(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            SetGameList(GameList.Append(name));
        }

        public static void RemoveGame(string name)
        {
            SetGameList(GameList.Where(n => !string.Equals(n, name, StringComparison.OrdinalIgnoreCase)));
        }

        public static void Init()
        {
            timer ??= new System.Timers.Timer(TickMs) { AutoReset = true };
            timer.Elapsed -= Tick;
            timer.Elapsed += Tick;
            timer.Enabled = IsEnabled;

            if (!IsEnabled)
            {
                state.Reset(Environment.TickCount64);
                activeGame = "";
                return;
            }

            // Start from the idle side: the guard only grants boost once a listed game is
            // actually running.
            state.Reset(Environment.TickCount64);
            Apply(IdleBoost);
        }

        public static void Stop()
        {
            if (timer is not null) timer.Enabled = false;

            bool wasHolding = state.AppliedBoost >= 0;
            state.Reset(Environment.TickCount64);
            activeGame = "";

            // Hand the setting back to the per-mode value the user configured, so turning
            // the guard off leaves the app exactly as it would be without it.
            if (wasHolding)
            {
                int configured = AppConfig.GetMode("auto_boost");
                if (configured != -1) PowerNative.SetCPUBoost(configured);
            }
        }

        // Re-read the settings after the user edits them, without waiting for the next tick.
        // The current condition decides the side immediately: changing the game boost mode
        // while a game is running has to take effect now, not after the idle delay.
        public static void Refresh()
        {
            if (!IsEnabled)
            {
                Stop();
                return;
            }
            if (timer is not null) timer.Enabled = true;

            // Detect outside the lock, write under it.
            bool game = IsGameRunning();
            int boost = game ? ActiveGameBoost() : IdleBoost;

            lock (sync)
            {
                state.ForceState(game, boost, Environment.TickCount64);
                PowerNative.SetCPUBoost(boost);
            }

            Applied?.Invoke();
        }

        private static bool IsProcessRunning(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            try
            {
                var processes = Process.GetProcessesByName(name);
                try { return processes.Length > 0; }
                finally { foreach (var p in processes) p.Dispose(); }
            }
            catch { return false; }
        }

        // True only while one of the user's listed games is running. There is no fallback to
        // the foreground window, fullscreen windows, or CPU load on purpose.
        private static bool IsGameRunning()
        {
            foreach (var name in GameList)
            {
                if (!IsProcessRunning(name)) continue;
                activeGame = name;
                return true;
            }

            activeGame = "";
            return false;
        }

        private static void Tick(object? sender, System.Timers.ElapsedEventArgs e)
        {
            if (!IsEnabled) return;

            // Drop the tick when a pass is already running instead of queueing threads
            // behind the same lock.
            if (Interlocked.CompareExchange(ref tickRunning, 1, 0) != 0) return;

            try
            {
                string previousGame;
                lock (sync) previousGame = activeGame;

                bool game = IsGameRunning();

                // Off by default. When the user asks why detection was slow, this line shows
                // what each pass actually saw instead of leaving it to guesswork.
                if (AppConfig.Get("boost_guard_debug") == 1)
                    Logger.WriteLine($"Boost guard tick: game={game} active='{activeGame}' listed={GameList.Length}");

                int? boost;

                lock (sync)
                {
                    boost = state.Update(
                        game,
                        Environment.TickCount64,
                        ActiveGameBoost(), IdleBoost,
                        GameDelay * 1000, IdleDelay * 1000);

                    if (boost is int value)
                    {
                        Logger.WriteLine($"Boost guard: {(state.IsGameActive ? "game" : "idle")}, boost {value}");
                        PowerNative.SetCPUBoost(value);
                    }
                }

                // Raised outside the lock: a handler is free to call back into the guard.
                if (boost is not null) Applied?.Invoke();

                // The detected game can change without the boost value changing, so this is
                // reported separately from the write above.
                if (!string.Equals(previousGame, activeGame, StringComparison.OrdinalIgnoreCase))
                    StateChanged?.Invoke();
            }
            finally
            {
                Volatile.Write(ref tickRunning, 0);
            }
        }

        private static void Apply(int boost)
        {
            if (boost == state.AppliedBoost) return;
            state.ForceApplied(boost);
            PowerNative.SetCPUBoost(boost);
            Applied?.Invoke();
        }
    }
}