using GHelper.Helpers;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GHelper.AutoUpdate
{
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
            // Update feature completely disabled by user request
            return;
        }

        public void Update()
        {
            // Update feature completely disabled by user request
            return;
        }

        public void LoadReleases()
        {
            return;
        }

        async void CheckForUpdatesAsync(bool force = false)
        {
            return;
        }

        public static string EscapeString(string input)
        {
            return Regex.Replace(Regex.Replace(input, @"\[|\]", "`$0"), @"\'", "''");
        }

        async void AutoUpdate(string requestUri)
        {

            Uri uri = new Uri(requestUri);
            string zipName = Path.GetFileName(uri.LocalPath);

            string exeLocation = Application.ExecutablePath;
            string exeDir = Path.GetDirectoryName(exeLocation);
            //exeDir = "C:\\Program Files\\GHelper";
            string exeName = Path.GetFileName(exeLocation);
            string zipLocation = exeDir + "\\" + zipName;

            using (HttpClient client = new HttpClient())
            {

                client.DefaultRequestHeaders.Add("User-Agent", "G-Helper App");
                Logger.WriteLine(requestUri);
                Logger.WriteLine(exeDir);
                Logger.WriteLine(zipName);
                Logger.WriteLine(exeName);

                try
                {
                    var bytes = await client.GetByteArrayAsync(uri);
                    File.WriteAllBytes(zipLocation, bytes);
                    Logger.WriteLine($"Downloaded {bytes.Length}b: {zipLocation} (exists={File.Exists(zipLocation)}, size={new FileInfo(zipLocation).Length})");
                }
                catch (Exception ex)
                {
                    Logger.WriteLine(ex.Message);
                    if (!ProcessHelper.IsUserAdministrator())
                    {
                        ProcessHelper.RunAsAdmin("autoupdate");
                        Application.Exit();
                    } else
                    {
                        LoadReleases();
                    }
                    return;
                }

                string command = $"$ErrorActionPreference = \"Stop\"; Set-Location -Path '{EscapeString(exeDir)}'; Wait-Process -Name \"GHelper\"; Expand-Archive \"{zipName}\" -DestinationPath . -Force; Remove-Item \"{zipName}\" -Force; \".\\{exeName}\"; ";
                Logger.WriteLine(command);

                try
                {
                    var cmd = new Process();
                    cmd.StartInfo.WorkingDirectory = exeDir;
                    cmd.StartInfo.UseShellExecute = false;
                    cmd.StartInfo.CreateNoWindow = true;
                    cmd.StartInfo.FileName = "powershell";
                    cmd.StartInfo.Arguments = command;
                    if (ProcessHelper.IsUserAdministrator()) cmd.StartInfo.Verb = "runas";
                    cmd.Start();
                }
                catch (Exception ex)
                {
                    Logger.WriteLine(ex.Message);
                }

                Application.Exit();
            }

        }

    }
}
