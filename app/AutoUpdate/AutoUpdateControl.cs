using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace GHelper.AutoUpdate
{
    // Custom build: never installs the upstream zip over this exe, it only points at the release notes.
    public class AutoUpdateControl
    {

        SettingsForm settings;

        public string versionUrl = "https://github.com/seerge/g-helper/releases";
        public bool update = false;

        static long lastUpdate;

        public AutoUpdateControl(SettingsForm settingsForm)
        {
            settings = settingsForm;
            var appVersion = new Version(Assembly.GetExecutingAssembly().GetName().Version.ToString());
            settings.SetVersionLabel(Properties.Strings.VersionLabel + $": {appVersion.Major}.{appVersion.Minor}.{appVersion.Build}");
        }

        public void CheckForUpdates()
        {
            // Run update once per 12 hours
            if (Math.Abs(DateTimeOffset.Now.ToUnixTimeSeconds() - lastUpdate) < 43200) return;
            lastUpdate = DateTimeOffset.Now.ToUnixTimeSeconds();

            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                CheckForUpdatesAsync();
            });
        }

        public void Update()
        {
            if (update)
            {
                LoadReleases();
            }
            else
            {
                Task.Run(() => CheckForUpdatesAsync());
            }
        }

        public void LoadReleases()
        {
            try
            {
                Process.Start(new ProcessStartInfo(versionUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Failed to open releases page:" + ex.Message);
            }
        }

        async void CheckForUpdatesAsync()
        {

            if (AppConfig.Is("skip_updates")) return;

            try
            {

                using (var httpClient = new HttpClient())
                {
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "G-Helper App");
                    var json = await httpClient.GetStringAsync("https://api.github.com/repos/seerge/g-helper/releases/latest");
                    var config = JsonSerializer.Deserialize<JsonElement>(json);
                    var tag = config.GetProperty("tag_name").ToString().Replace("v", "");

                    var gitVersion = new Version(tag);
                    var appVersion = new Version(Assembly.GetExecutingAssembly().GetName().Version.ToString());

                    if (gitVersion.CompareTo(appVersion) > 0)
                    {
                        update = true;
                        settings.SetVersionLabel($"Update available: {appVersion.Major}.{appVersion.Minor}.{appVersion.Build} → {tag}", true);

                        if (AppConfig.GetString("skip_version") != tag)
                        {
                            string text = $"G-Helper {tag} is available.\n\nThis is a custom build, it is not updated automatically.\nOpen the releases page?";
                            DialogResult dialogResult = settings.ShowMessage(text, "Update", MessageBoxButtons.YesNo);
                            if (dialogResult == DialogResult.Yes)
                                LoadReleases();
                            else
                                AppConfig.Set("skip_version", tag);
                        }

                    }
                    else
                    {
                        Logger.WriteLine($"Latest version {appVersion}");
                    }

                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Failed to check for updates:" + ex.Message);
            }

        }

    }
}