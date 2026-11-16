namespace GHelper.Helpers
{
    // Names that are desktop apps rather than games. Used by the Boost Guard picker so the
    // list of running apps does not offer Explorer, the shell hosts, a browser, or G-Helper
    // itself as a candidate.
    //
    // Game detection is manual now, so this class only classifies names: it no longer looks
    // at the foreground window, enumerates windows, or reads window titles.
    internal static class ForegroundHelper
    {
        private static readonly HashSet<string> DesktopApps = new(StringComparer.OrdinalIgnoreCase)
        {
            "chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "iexplore", "chromium", "librewolf", "arc", "waterfox", "thorium",
            "WindowsTerminal", "conhost", "cmd", "powershell", "pwsh", "alacritty", "wezterm-gui", "mintty",
            "discord", "slack", "Teams", "ms-teams", "Spotify", "WhatsApp", "Signal", "Telegram", "Code", "Notion", "obsidian", "zoom", "Skype", "Element", "Viber", "LINE", "WeChat", "Weixin", "WeChatAppEx",
            "notepad", "notepad++", "sublime_text", "devenv", "rider64", "idea64", "pycharm64", "webstorm64",
            "steam", "steamwebhelper", "EpicGamesLauncher", "Battle.net", "GalaxyClient", "EADesktop", "UbisoftConnect",
            "vlc", "mpv", "mpc-hc64", "mpc-be64", "PotPlayerMini64", "wmplayer", "smplayer", "foobar2000", "aimp",
            "WINWORD", "EXCEL", "POWERPNT", "OUTLOOK", "Acrobat", "AcroRd32", "SumatraPDF", "thunderbird", "Mailspring", "OneNote", "GitHubDesktop", "7zFM", "WinRAR", "SnippingTool", "SpaceSniffer",
            "explorer", "ShellExperienceHost", "ShellHost", "SearchHost", "StartMenuExperienceHost", "ApplicationFrameHost", "SystemSettings", "Taskmgr",
            "PowerToys.PowerLauncher", "PowerToys.Settings", "PowerToys.ColorPickerUI", "PowerToys.FancyZonesEditor", "PowerToys.Peek.UI", "PowerToys.PowerOCR", "PowerToys.MeasureToolUI", "PowerToys.ImageResizer", "PowerToys.PowerRename", "PowerToys.AdvancedPaste", "Microsoft.CmdPal.UI",
        };

        private static bool IsDesktopAppName(string name)
        {
            if (DesktopApps.Contains(name)) return true;
            // G-Helper's own windows are never a game, including the overlay.
            return name.Equals("GHelper", StringComparison.OrdinalIgnoreCase);
        }

        // Used by the Boost guard picker so the list of running apps does not offer
        // Explorer, the shell hosts, or the app itself as candidates for a game.
        public static bool IsDesktop(string name) => IsDesktopAppName(name);
    }
}