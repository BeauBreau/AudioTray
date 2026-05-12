using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Audio Tray")]
[assembly: AssemblyProduct("Audio Tray")]
[assembly: AssemblyVersion("1.2.4.0")]
[assembly: AssemblyFileVersion("1.2.4.0")]

namespace AudioTray
{
    public enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }
    public enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

    [Flags]
    public enum DeviceState { Active = 1 }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROPERTYKEY
    {
        public Guid fmtid;
        public int pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROPVARIANT
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public IntPtr p;
        public int p2;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumerator { }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    internal class PolicyConfigClient { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    internal interface IPolicyConfig
    {
        void GetMixFormat();
        void GetDeviceFormat();
        void ResetDeviceFormat();
        void SetDeviceFormat();
        void GetProcessingPeriod();
        void SetProcessingPeriod();
        void GetShareMode();
        void SetShareMode();
        void GetPropertyValue();
        void SetPropertyValue();
        void SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
        void SetEndpointVisibility();
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, DeviceState stateMask, [MarshalAs(UnmanagedType.Interface)] out IMMDeviceCollection devices);
        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, [MarshalAs(UnmanagedType.Interface)] out IMMDevice endpoint);
        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, [MarshalAs(UnmanagedType.Interface)] out IMMDevice device);
        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    internal interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out uint count);
        [PreserveSig]
        int Item(uint index, [MarshalAs(UnmanagedType.Interface)] out IMMDevice device);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    internal interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig]
        int OpenPropertyStore(int accessMode, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore properties);
        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig]
        int GetState(out DeviceState state);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    internal interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint count);
        [PreserveSig]
        int GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig]
        int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
    }

    public class AudioDevice
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Flow { get; set; }
        public string IconPath { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }

    public class FavoriteDeviceItem
    {
        public AudioDevice Device { get; set; }
        public bool IsChecked { get; set; }

        public override string ToString()
        {
            return Device == null ? "" : Device.Name;
        }
    }

    public class DarkResizableForm : Form
    {
        private const int ResizeBorder = 7;
        private const int TitleBarHeight = 32;
        private const int CornerRadius = 8;
        public Color BorderColor { get; set; }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        public DarkResizableForm()
        {
            BorderColor = Color.FromArgb(55, 104, 142);
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }

        public void DragFromTitleBar()
        {
            const int wmNcLButtonDown = 0x00A1;
            const int htCaption = 2;
            ReleaseCapture();
            SendMessage(Handle, wmNcLButtonDown, htCaption, 0);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            using (var path = CreateRoundedPath(new Rectangle(0, 0, Width, Height), CornerRadius))
            {
                Region = new Region(path);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = CreateRoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), CornerRadius))
            using (var pen = new Pen(BorderColor, 1))
            {
                e.Graphics.DrawPath(pen, path);
            }
        }

        private static System.Drawing.Drawing2D.GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void WndProc(ref Message message)
        {
            const int wmNcHitTest = 0x0084;
            const int htClient = 1;
            const int htCaption = 2;
            const int htLeft = 10;
            const int htRight = 11;
            const int htTop = 12;
            const int htTopLeft = 13;
            const int htTopRight = 14;
            const int htBottom = 15;
            const int htBottomLeft = 16;
            const int htBottomRight = 17;

            base.WndProc(ref message);

            if (message.Msg != wmNcHitTest || (int)message.Result != htClient)
            {
                return;
            }

            Point point = PointToClient(new Point((short)((int)message.LParam), (short)((int)message.LParam >> 16)));
            bool left = point.X <= ResizeBorder;
            bool right = point.X >= ClientSize.Width - ResizeBorder;
            bool top = point.Y <= ResizeBorder;
            bool bottom = point.Y >= ClientSize.Height - ResizeBorder;

            if (left && top) message.Result = (IntPtr)htTopLeft;
            else if (right && top) message.Result = (IntPtr)htTopRight;
            else if (left && bottom) message.Result = (IntPtr)htBottomLeft;
            else if (right && bottom) message.Result = (IntPtr)htBottomRight;
            else if (left) message.Result = (IntPtr)htLeft;
            else if (right) message.Result = (IntPtr)htRight;
            else if (top) message.Result = (IntPtr)htTop;
            else if (bottom) message.Result = (IntPtr)htBottom;
            else if (point.Y <= TitleBarHeight) message.Result = (IntPtr)htCaption;
        }
    }

    public static class AudioManager
    {
        private static readonly PROPERTYKEY FriendlyNameKey = new PROPERTYKEY
        {
            fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
            pid = 14
        };

        private static readonly PROPERTYKEY DevicesIconKey = new PROPERTYKEY
        {
            fmtid = new Guid("78C34FC8-104A-4ACA-9EA4-524D52996E57"),
            pid = 57
        };

        private static readonly PROPERTYKEY DeviceClassIconPathKey = new PROPERTYKEY
        {
            fmtid = new Guid("259ABFFC-50A7-47CE-AF08-68C9A7D73366"),
            pid = 12
        };

        private static readonly PROPERTYKEY DriverPackageIconKey = new PROPERTYKEY
        {
            fmtid = new Guid("CF73BB51-3ABF-44A2-85E0-9A3DC7A12132"),
            pid = 6
        };

        public static List<AudioDevice> GetDevices(EDataFlow flow)
        {
            var result = new List<AudioDevice>();
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            IMMDeviceCollection collection;
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(flow, DeviceState.Active, out collection));

            uint count;
            Marshal.ThrowExceptionForHR(collection.GetCount(out count));

            for (uint i = 0; i < count; i++)
            {
                IMMDevice device;
                Marshal.ThrowExceptionForHR(collection.Item(i, out device));

                string id;
                Marshal.ThrowExceptionForHR(device.GetId(out id));

                IPropertyStore store;
                Marshal.ThrowExceptionForHR(device.OpenPropertyStore(0, out store));

                string name = GetPropertyString(store, FriendlyNameKey);

                result.Add(new AudioDevice
                {
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(name) ? id : name,
                    Flow = flow == EDataFlow.eRender ? "Output" : "Input",
                    IconPath = GetDeviceIconPath(store)
                });
            }

            return result.OrderBy(d => d.Name).ToList();
        }

        public static AudioDevice GetDefaultDevice(EDataFlow flow)
        {
            string defaultId = GetDefaultDeviceId(flow);
            return GetDevices(flow).FirstOrDefault(d => d.Id == defaultId);
        }

        public static string GetDefaultDeviceId(EDataFlow flow)
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            IMMDevice endpoint;
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(flow, ERole.eMultimedia, out endpoint));

            string id;
            Marshal.ThrowExceptionForHR(endpoint.GetId(out id));
            return id;
        }

        public static void SetDefaultDevice(string id)
        {
            var policy = (IPolicyConfig)new PolicyConfigClient();
            policy.SetDefaultEndpoint(id, ERole.eConsole);
            policy.SetDefaultEndpoint(id, ERole.eMultimedia);
            policy.SetDefaultEndpoint(id, ERole.eCommunications);
        }

        private static string GetDeviceIconPath(IPropertyStore store)
        {
            string iconPath = GetPropertyString(store, DevicesIconKey);
            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                return iconPath;
            }

            iconPath = GetPropertyString(store, DeviceClassIconPathKey);
            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                return iconPath;
            }

            return GetPropertyString(store, DriverPackageIconKey);
        }

        private static string GetPropertyString(IPropertyStore store, PROPERTYKEY key)
        {
            try
            {
                PROPVARIANT value;
                PROPERTYKEY localKey = key;
                int hr = store.GetValue(ref localKey, out value);
                if (hr != 0 || value.p == IntPtr.Zero)
                {
                    return null;
                }

                return Marshal.PtrToStringUni(value.p);
            }
            catch
            {
                return null;
            }
        }
    }

    public static class IconLoader
    {
        [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHDefExtractIcon(string iconFile, int iconIndex, uint flags, out IntPtr largeIcon, out IntPtr smallIcon, uint iconSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr icon);

        public static Icon LoadFromIconPath(string iconPath)
        {
            if (string.IsNullOrWhiteSpace(iconPath))
            {
                return null;
            }

            string firstIcon = iconPath.Split(new[] { '\0', ';' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(firstIcon))
            {
                return null;
            }

            string filePath;
            int iconIndex;
            if (!TryParseIconSpecifier(firstIcon.Trim(), out filePath, out iconIndex))
            {
                return null;
            }

            filePath = Environment.ExpandEnvironmentVariables(filePath.Trim().Trim('"'));
            if (filePath.StartsWith("@", StringComparison.Ordinal))
            {
                filePath = filePath.Substring(1);
            }

            if (!File.Exists(filePath))
            {
                return null;
            }

            IntPtr largeIcon;
            IntPtr smallIcon;
            int hr = SHDefExtractIcon(filePath, iconIndex, 0, out largeIcon, out smallIcon, 16u | (32u << 16));
            if (hr != 0)
            {
                return null;
            }

            IntPtr handle = smallIcon != IntPtr.Zero ? smallIcon : largeIcon;
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return (Icon)Icon.FromHandle(handle).Clone();
            }
            finally
            {
                if (smallIcon != IntPtr.Zero)
                {
                    DestroyIcon(smallIcon);
                }

                if (largeIcon != IntPtr.Zero)
                {
                    DestroyIcon(largeIcon);
                }
            }
        }

        private static bool TryParseIconSpecifier(string iconSpecifier, out string filePath, out int iconIndex)
        {
            filePath = iconSpecifier;
            iconIndex = 0;

            int comma = iconSpecifier.LastIndexOf(',');
            if (comma < 0)
            {
                return true;
            }

            filePath = iconSpecifier.Substring(0, comma);
            return int.TryParse(iconSpecifier.Substring(comma + 1).Trim(), out iconIndex);
        }
    }

    [DataContract]
    public class Settings
    {
        [DataMember]
        public List<string> OutputFavorites { get; set; }

        [DataMember]
        public List<string> InputFavorites { get; set; }

        [DataMember]
        public bool NotificationsEnabled { get; set; }

        [DataMember]
        public bool CheckForUpdates { get; set; }

        [DataMember]
        public bool InstallUpdatesAutomatically { get; set; }

        [DataMember]
        public bool ShowDeviceIconInTray { get; set; }

        [DataMember]
        public string LastFailedUpdateVersion { get; set; }

        [DataMember]
        public int? FavoritesWindowX { get; set; }

        [DataMember]
        public int? FavoritesWindowY { get; set; }

        [DataMember]
        public int? FavoritesWindowWidth { get; set; }

        [DataMember]
        public int? FavoritesWindowHeight { get; set; }

        public Settings()
        {
            OutputFavorites = new List<string>();
            InputFavorites = new List<string>();
            NotificationsEnabled = true;
            CheckForUpdates = true;
            InstallUpdatesAutomatically = false;
            ShowDeviceIconInTray = false;
            LastFailedUpdateVersion = null;
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            OutputFavorites = new List<string>();
            InputFavorites = new List<string>();
            NotificationsEnabled = true;
            CheckForUpdates = true;
            InstallUpdatesAutomatically = false;
            ShowDeviceIconInTray = false;
            LastFailedUpdateVersion = null;
        }
    }

    [DataContract]
    public class GitHubRelease
    {
        [DataMember(Name = "tag_name")]
        public string TagName { get; set; }

        [DataMember(Name = "html_url")]
        public string HtmlUrl { get; set; }

        [DataMember(Name = "assets")]
        public List<GitHubAsset> Assets { get; set; }
    }

    [DataContract]
    public class GitHubAsset
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "browser_download_url")]
        public string DownloadUrl { get; set; }
    }

    public class UpdateInfo
    {
        public string VersionText { get; set; }
        public string ReleaseUrl { get; set; }
        public string DownloadUrl { get; set; }
        public string Sha256Url { get; set; }
    }

    public static class UpdateManager
    {
        private const string LatestReleaseUrl = "https://api.github.com/repos/BeauBreau/AudioTray/releases/latest";
        private const string AssetName = "AudioTray.exe";
        private const string Sha256AssetName = "AudioTray.exe.sha256";

        public static string CurrentVersionText
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version.ToString(3); }
        }

        public static UpdateInfo CheckForUpdate()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            using (var client = new WebClient())
            {
                client.Headers.Add("User-Agent", "AudioTray/" + CurrentVersionText);
                using (var stream = client.OpenRead(LatestReleaseUrl))
                {
                    var release = (GitHubRelease)new DataContractJsonSerializer(typeof(GitHubRelease)).ReadObject(stream);
                    if (release == null || string.IsNullOrWhiteSpace(release.TagName))
                    {
                        return null;
                    }

                    Version latestVersion;
                    Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
                    if (!TryParseVersion(release.TagName, out latestVersion) || latestVersion <= currentVersion)
                    {
                        return null;
                    }

                    GitHubAsset asset = null;
                    GitHubAsset sha256Asset = null;
                    if (release.Assets != null)
                    {
                        asset = release.Assets.FirstOrDefault(a => string.Equals(a.Name, AssetName, StringComparison.OrdinalIgnoreCase));
                        sha256Asset = release.Assets.FirstOrDefault(a => string.Equals(a.Name, Sha256AssetName, StringComparison.OrdinalIgnoreCase));
                    }

                    if (asset == null || string.IsNullOrWhiteSpace(asset.DownloadUrl))
                    {
                        throw new InvalidOperationException("The latest GitHub release does not include an AudioTray.exe download.");
                    }

                    if (sha256Asset == null || string.IsNullOrWhiteSpace(sha256Asset.DownloadUrl))
                    {
                        throw new InvalidOperationException("The latest GitHub release does not include an AudioTray.exe.sha256 checksum.");
                    }

                    return new UpdateInfo
                    {
                        VersionText = release.TagName,
                        ReleaseUrl = release.HtmlUrl,
                        DownloadUrl = asset.DownloadUrl,
                        Sha256Url = sha256Asset.DownloadUrl
                    };
                }
            }
        }

        public static string DownloadUpdate(UpdateInfo update)
        {
            if (update == null || string.IsNullOrWhiteSpace(update.DownloadUrl) || string.IsNullOrWhiteSpace(update.Sha256Url))
            {
                throw new ArgumentException("Update download information is missing.");
            }

            string directory = Path.Combine(Path.GetTempPath(), "AudioTrayUpdate");
            Directory.CreateDirectory(directory);
            string destination = Path.Combine(directory, "AudioTray-" + SanitizeFileName(update.VersionText) + ".exe");
            string checksumDestination = destination + ".sha256";

            using (var client = new WebClient())
            {
                client.Headers.Add("User-Agent", "AudioTray/" + CurrentVersionText);
                client.DownloadFile(update.DownloadUrl, destination);
                client.DownloadFile(update.Sha256Url, checksumDestination);
            }

            string expectedHash = ReadExpectedSha256(checksumDestination);
            string actualHash = ComputeSha256(destination);
            if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The downloaded update failed SHA256 verification.");
            }

            return destination;
        }

        public static void StartSelfUpdate(string downloadedExe, string expectedVersion)
        {
            if (string.IsNullOrWhiteSpace(downloadedExe) || !File.Exists(downloadedExe))
            {
                throw new FileNotFoundException("The downloaded update could not be found.", downloadedExe);
            }

            string currentExe = Application.ExecutablePath;
            string scriptPath = Path.Combine(Path.GetTempPath(), "AudioTrayUpdate", "Install-AudioTrayUpdate.ps1");
            string logPath = Path.Combine(Path.GetDirectoryName(scriptPath), "AudioTrayUpdate.log");
            Directory.CreateDirectory(Path.GetDirectoryName(scriptPath));

            string script = string.Join(Environment.NewLine, new[]
            {
                "param([int]$ProcessId, [string]$Source, [string]$Target, [string]$ExpectedVersion, [string]$LogPath)",
                "$ErrorActionPreference = 'Stop'",
                "function Write-UpdateLog([string]$Message) { Add-Content -LiteralPath $LogPath -Value ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ' + $Message) -ErrorAction SilentlyContinue }",
                "$success = $false",
                "try {",
                "  Write-UpdateLog \"Starting update. Source='$Source' Target='$Target' Expected='$ExpectedVersion'\"",
                "  try { Wait-Process -Id $ProcessId -ErrorAction SilentlyContinue -Timeout 30 } catch { Start-Sleep -Seconds 2 }",
                "  Copy-Item -LiteralPath $Source -Destination $Target -Force",
                "  $actualVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Target).FileVersion",
                "  if ($ExpectedVersion -and ($actualVersion -notlike ($ExpectedVersion + '*'))) { throw \"Version verification failed. Expected $ExpectedVersion but found $actualVersion.\" }",
                "  $success = $true",
                "  Write-UpdateLog \"Update succeeded. Installed version $actualVersion.\"",
                "} catch {",
                "  Write-UpdateLog (\"Update failed: \" + $_.Exception.Message)",
                "} finally {",
                "  Start-Process -FilePath $Target",
                "  if ($success) { Remove-Item -LiteralPath $Source -Force -ErrorAction SilentlyContinue }",
                "}",
                "Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue"
            });
            File.WriteAllText(scriptPath, script, Encoding.UTF8);

            string expectedVersionNumber;
            Version parsedVersion;
            expectedVersionNumber = TryParseVersion(expectedVersion, out parsedVersion) ? parsedVersion.ToString(3) : expectedVersion;

            var info = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File " + Quote(scriptPath) +
                            " -ProcessId " + Process.GetCurrentProcess().Id +
                            " -Source " + Quote(downloadedExe) +
                            " -Target " + Quote(currentExe) +
                            " -ExpectedVersion " + Quote(expectedVersionNumber ?? "") +
                            " -LogPath " + Quote(logPath),
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false
            };
            Process.Start(info);
        }

        private static bool TryParseVersion(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            text = text.Trim();
            if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(1);
            }

            int suffix = text.IndexOf('-');
            if (suffix >= 0)
            {
                text = text.Substring(0, suffix);
            }

            return Version.TryParse(text, out version);
        }

        private static string SanitizeFileName(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "latest";
            }

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                text = text.Replace(c, '_');
            }
            return text;
        }

        private static string ReadExpectedSha256(string checksumPath)
        {
            if (!File.Exists(checksumPath))
            {
                throw new FileNotFoundException("The SHA256 checksum file was not downloaded.", checksumPath);
            }

            string text = File.ReadAllText(checksumPath).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("The SHA256 checksum file is empty.");
            }

            string firstToken = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (firstToken == null || firstToken.Length != 64 || firstToken.Any(c => !Uri.IsHexDigit(c)))
            {
                throw new InvalidOperationException("The SHA256 checksum file does not contain a valid SHA256 hash.");
            }

            return firstToken;
        }

        private static string ComputeSha256(string filePath)
        {
            using (var sha256 = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hash = sha256.ComputeHash(stream);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    builder.Append(b.ToString("x2"));
                }

                return builder.ToString();
            }
        }

        private static string Quote(string text)
        {
            return "\"" + text.Replace("\"", "\\\"") + "\"";
        }
    }

    public class TrayAppContext : ApplicationContext
    {
        private readonly NotifyIcon trayIcon;
        private readonly ContextMenuStrip menu;
        private readonly Control uiInvoker;
        private readonly string settingsPath;
        private readonly System.Windows.Forms.Timer iconRefreshTimer;
        private readonly Icon appTrayIcon;
        private Icon loadedTrayIcon;
        private string currentOutputDeviceId;
        private string currentOutputIconPath;
        private Form favoritesWindow;
        private bool updateCheckInProgress;
        private const string StartupRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupValueName = "AudioTray";

        public TrayAppContext()
        {
            settingsPath = Path.Combine(GetSettingsDirectory(), "settings.json");
            uiInvoker = new Control();
            uiInvoker.CreateControl();
            appTrayIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            menu = new ContextMenuStrip();
            menu.Opening += delegate { BuildMenu(); };

            trayIcon = new NotifyIcon
            {
                Text = "Audio Tray",
                Icon = appTrayIcon,
                Visible = true,
                ContextMenuStrip = menu
            };
            trayIcon.DoubleClick += delegate { ShowFavoritesWindow(); };

            UpdateTrayIconFromCurrentOutput(true);

            iconRefreshTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            iconRefreshTimer.Tick += delegate { UpdateTrayIconFromCurrentOutput(false); };
            iconRefreshTimer.Start();

            Settings settings = LoadSettings();
            if (settings.CheckForUpdates)
            {
                CheckForUpdatesAsync(false, settings.InstallUpdatesAutomatically);
            }
        }

        private string GetSettingsDirectory()
        {
            string preferred = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioTray");
            try
            {
                Directory.CreateDirectory(preferred);
                return preferred;
            }
            catch
            {
                string fallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AudioTrayData");
                Directory.CreateDirectory(fallback);
                return fallback;
            }
        }

        private Settings LoadSettings()
        {
            if (!File.Exists(settingsPath))
            {
                return new Settings();
            }

            try
            {
                using (var stream = File.OpenRead(settingsPath))
                {
                    return (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream);
                }
            }
            catch
            {
                return new Settings();
            }
        }

        private void ShowNotification(string message)
        {
            if (LoadSettings().NotificationsEnabled)
            {
                trayIcon.ShowBalloonTip(1800, "Audio Tray", message, ToolTipIcon.Info);
            }
        }

        private void SaveSettings(Settings settings)
        {
            using (var stream = File.Create(settingsPath))
            {
                new DataContractJsonSerializer(typeof(Settings)).WriteObject(stream, settings);
            }
        }

        private void BuildMenu()
        {
            UpdateTrayIconFromCurrentOutput(false);
            menu.Items.Clear();
            Settings settings = LoadSettings();

            AddDeviceMenuItems("Output favorites", AudioManager.GetDevices(EDataFlow.eRender), settings.OutputFavorites, EDataFlow.eRender);
            menu.Items.Add(new ToolStripSeparator());
            AddDeviceMenuItems("Input favorites", AudioManager.GetDevices(EDataFlow.eCapture), settings.InputFavorites, EDataFlow.eCapture);
            menu.Items.Add(new ToolStripSeparator());

            var checkUpdates = new ToolStripMenuItem("Check for updates now");
            checkUpdates.Enabled = !updateCheckInProgress;
            checkUpdates.Click += delegate { CheckForUpdatesAsync(true, false); };
            menu.Items.Add(checkUpdates);

            var favorites = new ToolStripMenuItem("Favorites...");
            favorites.Click += delegate { ShowFavoritesWindow(); };
            menu.Items.Add(favorites);

            var exit = new ToolStripMenuItem("Exit");
            exit.Click += delegate
            {
                trayIcon.Visible = false;
                Application.Exit();
            };
            menu.Items.Add(exit);
        }

        private void AddDeviceMenuItems(string title, List<AudioDevice> devices, List<string> favorites, EDataFlow flow)
        {
            menu.Items.Add(new ToolStripMenuItem(title) { Enabled = false });

            var deviceById = devices.ToDictionary(d => d.Id, d => d);
            var favoriteDevices = favorites
                .Where(id => deviceById.ContainsKey(id))
                .Select(id => deviceById[id])
                .ToList();
            if (favoriteDevices.Count == 0)
            {
                menu.Items.Add(new ToolStripMenuItem("No favorites set") { Enabled = false });
                return;
            }

            string defaultId = null;
            try { defaultId = AudioManager.GetDefaultDeviceId(flow); } catch { }

            foreach (AudioDevice device in favoriteDevices)
            {
                var item = new ToolStripMenuItem(device.Name)
                {
                    Tag = device,
                    Checked = device.Id == defaultId
                };
                item.Click += delegate(object sender, EventArgs args)
                {
                    var selected = (AudioDevice)((ToolStripMenuItem)sender).Tag;
                    AudioManager.SetDefaultDevice(selected.Id);
                    if (flow == EDataFlow.eRender)
                    {
                        UpdateTrayIconFromCurrentOutput(true);
                    }
                    ShowNotification(selected.Name + " is now the default " + selected.Flow.ToLower() + " device.");
                };
                menu.Items.Add(item);
            }
        }

        private void CheckForUpdatesAsync(bool showIfCurrent, bool installAutomatically)
        {
            if (updateCheckInProgress)
            {
                return;
            }

            updateCheckInProgress = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    UpdateInfo update = UpdateManager.CheckForUpdate();
                    RunOnUiThread(delegate
                    {
                        updateCheckInProgress = false;
                        if (update == null)
                        {
                            if (showIfCurrent)
                            {
                                MessageBox.Show("Audio Tray is up to date.", "Audio Tray Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                            return;
                        }

                        Settings settings = LoadSettings();
                        if (!showIfCurrent &&
                            !string.IsNullOrWhiteSpace(settings.LastFailedUpdateVersion) &&
                            string.Equals(settings.LastFailedUpdateVersion, update.VersionText, StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }

                        HandleAvailableUpdate(update, installAutomatically);
                    });
                }
                catch (Exception ex)
                {
                    RunOnUiThread(delegate
                    {
                        updateCheckInProgress = false;
                        if (showIfCurrent)
                        {
                            MessageBox.Show("Audio Tray could not check for updates." + Environment.NewLine + Environment.NewLine + ex.Message, "Audio Tray Updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    });
                }
            });
        }

        private void HandleAvailableUpdate(UpdateInfo update, bool installAutomatically)
        {
            if (installAutomatically)
            {
                InstallUpdateAsync(update);
                return;
            }

            DialogResult result = MessageBox.Show(
                "Audio Tray " + update.VersionText + " is available." + Environment.NewLine + Environment.NewLine + "Download and install it now?",
                "Audio Tray Updates",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (result == DialogResult.Yes)
            {
                InstallUpdateAsync(update);
            }
        }

        private void InstallUpdateAsync(UpdateInfo update)
        {
            updateCheckInProgress = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string downloadedExe = UpdateManager.DownloadUpdate(update);
                    RunOnUiThread(delegate
                    {
                        try
                        {
                            Settings settings = LoadSettings();
                            settings.LastFailedUpdateVersion = update.VersionText;
                            SaveSettings(settings);

                            UpdateManager.StartSelfUpdate(downloadedExe, update.VersionText);
                            trayIcon.Visible = false;
                            Application.Exit();
                        }
                        catch (Exception ex)
                        {
                            updateCheckInProgress = false;
                            MessageBox.Show("Audio Tray downloaded the update but could not install it." + Environment.NewLine + Environment.NewLine + ex.Message, "Audio Tray Updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    });
                }
                catch (Exception ex)
                {
                    RunOnUiThread(delegate
                    {
                        updateCheckInProgress = false;
                        MessageBox.Show("Audio Tray could not download the update." + Environment.NewLine + Environment.NewLine + ex.Message, "Audio Tray Updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    });
                }
            });
        }

        private void RunOnUiThread(MethodInvoker action)
        {
            if (uiInvoker.IsHandleCreated)
            {
                uiInvoker.BeginInvoke(action);
            }
            else
            {
                action();
            }
        }

        private void UpdateTrayIconFromCurrentOutput(bool force)
        {
            try
            {
                Settings settings = LoadSettings();
                if (!settings.ShowDeviceIconInTray)
                {
                    SetAppTrayIcon();
                    return;
                }

                AudioDevice output = AudioManager.GetDefaultDevice(EDataFlow.eRender);
                string deviceId = output == null ? null : output.Id;
                string iconPath = output == null ? null : output.IconPath;
                if (!force && deviceId == currentOutputDeviceId && iconPath == currentOutputIconPath)
                {
                    return;
                }

                Icon nextIcon = IconLoader.LoadFromIconPath(iconPath);
                if (nextIcon == null)
                {
                    nextIcon = appTrayIcon;
                }

                Icon oldIcon = loadedTrayIcon;
                trayIcon.Icon = nextIcon;
                loadedTrayIcon = ReferenceEquals(nextIcon, appTrayIcon) ? null : nextIcon;
                currentOutputDeviceId = deviceId;
                currentOutputIconPath = iconPath;
                trayIcon.Text = BuildTrayText(output);

                if (oldIcon != null)
                {
                    oldIcon.Dispose();
                }
            }
            catch
            {
                SetAppTrayIcon();
                trayIcon.Text = "Audio Tray";
            }
        }

        private void SetAppTrayIcon()
        {
            Icon oldIcon = loadedTrayIcon;
            trayIcon.Icon = appTrayIcon;
            loadedTrayIcon = null;
            currentOutputDeviceId = null;
            currentOutputIconPath = null;

            if (oldIcon != null)
            {
                oldIcon.Dispose();
            }
        }

        private static string BuildTrayText(AudioDevice output)
        {
            string text = output == null ? "Audio Tray" : "Audio Tray - " + output.Name;
            return text.Length <= 63 ? text : text.Substring(0, 60) + "...";
        }

        private void ShowFavoritesWindow()
        {
            if (favoritesWindow != null && !favoritesWindow.IsDisposed)
            {
                if (favoritesWindow.WindowState == FormWindowState.Minimized)
                {
                    favoritesWindow.WindowState = FormWindowState.Normal;
                }

                favoritesWindow.Show();
                favoritesWindow.Activate();
                return;
            }

            Settings settings = LoadSettings();
            List<AudioDevice> outputs = AudioManager.GetDevices(EDataFlow.eRender);
            List<AudioDevice> inputs = AudioManager.GetDevices(EDataFlow.eCapture);

            var form = new DarkResizableForm();
            var titleBar = new Panel();
            var titleIcon = new PictureBox();
            var titleLabel = new Label();
            var titleButtonPanel = new FlowLayoutPanel();
            var minimizeButton = new Button();
            var closeButton = new Button();
            var contentPanel = new Panel();
            var mainPanel = new TableLayoutPanel();
            var tabButtonPanel = new FlowLayoutPanel();
            var outputTabButton = new Button();
            var inputTabButton = new Button();
            var settingsTabButton = new Button();
            var tabContentPanel = new Panel();
            var outputPage = new Panel();
            var inputPage = new Panel();
            var settingsPage = new Panel();
            var outputList = new ListBox();
            var inputList = new ListBox();
            var outputPanel = new TableLayoutPanel();
            var inputPanel = new TableLayoutPanel();
            var outputOrderButtons = new FlowLayoutPanel();
            var inputOrderButtons = new FlowLayoutPanel();
            var outputOrderHint = new Label();
            var inputOrderHint = new Label();
            var outputUpButton = new Button();
            var outputDownButton = new Button();
            var inputUpButton = new Button();
            var inputDownButton = new Button();
            var settingsPanel = new TableLayoutPanel();
            var notificationsCheckBox = new CheckBox();
            var startupCheckBox = new CheckBox();
            var trayDeviceIconPanel = new FlowLayoutPanel();
            var trayDeviceIconCheckBox = new CheckBox();
            var changeIconsLink = new LinkLabel();
            var checkUpdatesCheckBox = new CheckBox();
            var autoInstallUpdatesCheckBox = new CheckBox();
            var bottomPanel = new TableLayoutPanel();
            var watermarkLabel = new Label();
            var rightFooterPanel = new TableLayoutPanel();
            var buttonPanel = new FlowLayoutPanel();
            var versionLabel = new Label();
            var saveButton = new Button();
            var cancelButton = new Button();
            var deviceIconCache = new Dictionary<string, Icon>();
            Color windowBack = Color.FromArgb(14, 32, 52);
            Color panelBack = Color.FromArgb(20, 43, 68);
            Color listBack = Color.FromArgb(10, 26, 43);
            Color borderBlue = Color.FromArgb(55, 104, 142);
            Color accentBlue = Color.FromArgb(72, 178, 232);
            Color textMain = Color.FromArgb(232, 242, 250);
            Color textMuted = Color.FromArgb(145, 172, 194);
                form.Text = "Audio Tray Favorites";
                form.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                form.StartPosition = FormStartPosition.CenterScreen;
                form.Size = new Size(390, 500);
                form.MinimumSize = new Size(350, 390);
                RestoreFavoritesWindowBounds(form, settings);
                form.MaximizeBox = false;
                form.FormBorderStyle = FormBorderStyle.None;
                form.Padding = new Padding(2);
                form.BackColor = windowBack;
                form.BorderColor = borderBlue;

                titleBar.Dock = DockStyle.Top;
                titleBar.Height = 32;
                titleBar.BackColor = windowBack;
                titleBar.MouseDown += delegate(object sender, MouseEventArgs args) { if (args.Button == MouseButtons.Left) form.DragFromTitleBar(); };

                titleIcon.Image = form.Icon.ToBitmap();
                titleIcon.SizeMode = PictureBoxSizeMode.CenterImage;
                titleIcon.Dock = DockStyle.Left;
                titleIcon.Width = 32;
                titleIcon.BackColor = windowBack;
                titleIcon.MouseDown += delegate(object sender, MouseEventArgs args) { if (args.Button == MouseButtons.Left) form.DragFromTitleBar(); };

                titleLabel.Text = "Audio Tray Favorites";
                titleLabel.Dock = DockStyle.Fill;
                titleLabel.ForeColor = textMain;
                titleLabel.BackColor = windowBack;
                titleLabel.TextAlign = ContentAlignment.MiddleLeft;
                titleLabel.MouseDown += delegate(object sender, MouseEventArgs args) { if (args.Button == MouseButtons.Left) form.DragFromTitleBar(); };

                titleButtonPanel.Dock = DockStyle.Right;
                titleButtonPanel.Width = 74;
                titleButtonPanel.BackColor = windowBack;
                titleButtonPanel.FlowDirection = FlowDirection.LeftToRight;
                titleButtonPanel.WrapContents = false;
                titleButtonPanel.Padding = new Padding(0, 2, 4, 0);

                minimizeButton.Text = "\u2500";
                StyleTitleButton(minimizeButton, windowBack, textMain, accentBlue);
                minimizeButton.Click += delegate { form.WindowState = FormWindowState.Minimized; };

                closeButton.Text = "X";
                StyleTitleButton(closeButton, windowBack, textMain, accentBlue);
                closeButton.Click += delegate { form.Close(); };

                titleButtonPanel.Controls.Add(minimizeButton);
                titleButtonPanel.Controls.Add(closeButton);
                titleBar.Controls.Add(titleLabel);
                titleBar.Controls.Add(titleIcon);
                titleBar.Controls.Add(titleButtonPanel);

                contentPanel.Dock = DockStyle.Fill;
                contentPanel.BackColor = windowBack;

                mainPanel.Dock = DockStyle.Fill;
                mainPanel.BackColor = panelBack;
                mainPanel.ColumnCount = 1;
                mainPanel.RowCount = 2;
                mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
                mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

                tabButtonPanel.Dock = DockStyle.Fill;
                tabButtonPanel.BackColor = panelBack;
                tabButtonPanel.FlowDirection = FlowDirection.LeftToRight;
                tabButtonPanel.WrapContents = false;
                tabButtonPanel.Padding = new Padding(0, 2, 0, 0);

                StyleTabButton(outputTabButton, "Output", true, panelBack, windowBack, accentBlue, textMain, textMuted);
                StyleTabButton(inputTabButton, "Input", false, panelBack, windowBack, accentBlue, textMain, textMuted);
                StyleTabButton(settingsTabButton, "Settings", false, panelBack, windowBack, accentBlue, textMain, textMuted);

                tabContentPanel.Dock = DockStyle.Fill;
                tabContentPanel.BackColor = panelBack;
                outputPage.BackColor = panelBack;
                inputPage.BackColor = panelBack;
                settingsPage.BackColor = panelBack;
                outputPage.Dock = DockStyle.Fill;
                inputPage.Dock = DockStyle.Fill;
                settingsPage.Dock = DockStyle.Fill;
                outputList.Dock = DockStyle.Fill;
                ConfigureDeviceListDrawing(outputList, deviceIconCache);
                inputList.Dock = DockStyle.Fill;
                ConfigureDeviceListDrawing(inputList, deviceIconCache);
                StyleDeviceList(outputList, listBack, textMain);
                StyleDeviceList(inputList, listBack, textMain);

                outputPanel.Dock = DockStyle.Fill;
                outputPanel.BackColor = panelBack;
                outputPanel.ColumnCount = 1;
                outputPanel.RowCount = 2;
                outputPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                outputPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                inputPanel.Dock = DockStyle.Fill;
                inputPanel.BackColor = panelBack;
                inputPanel.ColumnCount = 1;
                inputPanel.RowCount = 2;
                inputPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                inputPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                outputOrderButtons.AutoSize = true;
                outputOrderButtons.Dock = DockStyle.Right;
                outputOrderButtons.BackColor = panelBack;
                outputOrderButtons.FlowDirection = FlowDirection.LeftToRight;
                outputOrderButtons.WrapContents = false;
                outputOrderButtons.Padding = new Padding(0, 6, 8, 6);

                inputOrderButtons.AutoSize = true;
                inputOrderButtons.Dock = DockStyle.Right;
                inputOrderButtons.BackColor = panelBack;
                inputOrderButtons.FlowDirection = FlowDirection.LeftToRight;
                inputOrderButtons.WrapContents = false;
                inputOrderButtons.Padding = new Padding(0, 6, 8, 6);

                outputOrderHint.Text = "Select name, then Up/Down to reorder.";
                outputOrderHint.AutoSize = true;
                outputOrderHint.ForeColor = textMuted;
                outputOrderHint.BackColor = panelBack;
                outputOrderHint.Margin = new Padding(3, 9, 8, 3);

                inputOrderHint.Text = "Select name, then Up/Down to reorder.";
                inputOrderHint.AutoSize = true;
                inputOrderHint.ForeColor = textMuted;
                inputOrderHint.BackColor = panelBack;
                inputOrderHint.Margin = new Padding(3, 9, 8, 3);

                outputUpButton.Text = "Up";
                StyleSmallButton(outputUpButton, accentBlue, windowBack);
                outputDownButton.Text = "Down";
                StyleSmallButton(outputDownButton, accentBlue, windowBack);
                inputUpButton.Text = "Up";
                StyleSmallButton(inputUpButton, accentBlue, windowBack);
                inputDownButton.Text = "Down";
                StyleSmallButton(inputDownButton, accentBlue, windowBack);

                settingsPanel.Dock = DockStyle.Top;
                settingsPanel.AutoSize = true;
                settingsPanel.BackColor = panelBack;
                settingsPanel.Padding = new Padding(12);
                settingsPanel.ColumnCount = 1;
                settingsPanel.RowCount = 5;

                notificationsCheckBox.Text = "Show notifications";
                notificationsCheckBox.AutoSize = true;
                notificationsCheckBox.Checked = settings.NotificationsEnabled;
                StyleCheckBox(notificationsCheckBox, panelBack, textMain);

                startupCheckBox.Text = "Run when Windows starts";
                startupCheckBox.AutoSize = true;
                startupCheckBox.Checked = IsStartupEnabledForCurrentLocation();
                StyleCheckBox(startupCheckBox, panelBack, textMain);

                trayDeviceIconCheckBox.Text = "Show current output device icon in system tray";
                trayDeviceIconCheckBox.AutoSize = true;
                trayDeviceIconCheckBox.Checked = settings.ShowDeviceIconInTray;
                StyleCheckBox(trayDeviceIconCheckBox, panelBack, textMain);

                trayDeviceIconPanel.AutoSize = true;
                trayDeviceIconPanel.BackColor = panelBack;
                trayDeviceIconPanel.WrapContents = false;
                trayDeviceIconPanel.FlowDirection = FlowDirection.LeftToRight;
                trayDeviceIconPanel.Margin = new Padding(0, 3, 0, 3);
                trayDeviceIconCheckBox.Margin = new Padding(3, 3, 8, 3);

                changeIconsLink.Text = "Change icons here";
                changeIconsLink.AutoSize = true;
                changeIconsLink.Margin = new Padding(0, 4, 3, 3);
                changeIconsLink.LinkColor = accentBlue;
                changeIconsLink.ActiveLinkColor = Color.White;
                changeIconsLink.VisitedLinkColor = accentBlue;
                changeIconsLink.BackColor = panelBack;
                changeIconsLink.LinkClicked += delegate { OpenSoundControlPanel(); };

                checkUpdatesCheckBox.Text = "Check for updates";
                checkUpdatesCheckBox.AutoSize = true;
                checkUpdatesCheckBox.Checked = settings.CheckForUpdates;
                StyleCheckBox(checkUpdatesCheckBox, panelBack, textMain);

                autoInstallUpdatesCheckBox.Text = "Install updates automatically";
                autoInstallUpdatesCheckBox.AutoSize = true;
                autoInstallUpdatesCheckBox.Checked = settings.InstallUpdatesAutomatically;
                StyleCheckBox(autoInstallUpdatesCheckBox, panelBack, textMain);

                foreach (AudioDevice device in OrderDevicesForFavoritesWindow(outputs, settings.OutputFavorites))
                {
                    outputList.Items.Add(new FavoriteDeviceItem { Device = device, IsChecked = settings.OutputFavorites.Contains(device.Id) });
                }

                foreach (AudioDevice device in OrderDevicesForFavoritesWindow(inputs, settings.InputFavorites))
                {
                    inputList.Items.Add(new FavoriteDeviceItem { Device = device, IsChecked = settings.InputFavorites.Contains(device.Id) });
                }

                outputUpButton.Click += delegate { MoveCheckedListItem(outputList, -1); };
                outputDownButton.Click += delegate { MoveCheckedListItem(outputList, 1); };
                inputUpButton.Click += delegate { MoveCheckedListItem(inputList, -1); };
                inputDownButton.Click += delegate { MoveCheckedListItem(inputList, 1); };
                outputList.MouseDown += delegate(object sender, MouseEventArgs args) { HandleDeviceListMouseDown(outputList, args); };
                inputList.MouseDown += delegate(object sender, MouseEventArgs args) { HandleDeviceListMouseDown(inputList, args); };

                outputOrderButtons.Controls.Add(outputOrderHint);
                outputOrderButtons.Controls.Add(outputUpButton);
                outputOrderButtons.Controls.Add(outputDownButton);
                inputOrderButtons.Controls.Add(inputOrderHint);
                inputOrderButtons.Controls.Add(inputUpButton);
                inputOrderButtons.Controls.Add(inputDownButton);

                outputPanel.Controls.Add(outputList, 0, 0);
                outputPanel.Controls.Add(outputOrderButtons, 0, 1);
                inputPanel.Controls.Add(inputList, 0, 0);
                inputPanel.Controls.Add(inputOrderButtons, 0, 1);

                outputPage.Controls.Add(outputPanel);
                inputPage.Controls.Add(inputPanel);
                trayDeviceIconPanel.Controls.Add(trayDeviceIconCheckBox);
                trayDeviceIconPanel.Controls.Add(changeIconsLink);
                settingsPanel.Controls.Add(notificationsCheckBox);
                settingsPanel.Controls.Add(startupCheckBox);
                settingsPanel.Controls.Add(trayDeviceIconPanel);
                settingsPanel.Controls.Add(checkUpdatesCheckBox);
                settingsPanel.Controls.Add(autoInstallUpdatesCheckBox);
                settingsPage.Controls.Add(settingsPanel);
                tabButtonPanel.Controls.Add(outputTabButton);
                tabButtonPanel.Controls.Add(inputTabButton);
                tabButtonPanel.Controls.Add(settingsTabButton);
                tabContentPanel.Controls.Add(outputPage);
                tabContentPanel.Controls.Add(inputPage);
                tabContentPanel.Controls.Add(settingsPage);
                ShowDarkTab(outputPage, outputTabButton, new[] { inputPage, settingsPage }, new[] { inputTabButton, settingsTabButton }, panelBack, windowBack, accentBlue, textMain, textMuted);
                outputTabButton.Click += delegate { ShowDarkTab(outputPage, outputTabButton, new[] { inputPage, settingsPage }, new[] { inputTabButton, settingsTabButton }, panelBack, windowBack, accentBlue, textMain, textMuted); };
                inputTabButton.Click += delegate { ShowDarkTab(inputPage, inputTabButton, new[] { outputPage, settingsPage }, new[] { outputTabButton, settingsTabButton }, panelBack, windowBack, accentBlue, textMain, textMuted); };
                settingsTabButton.Click += delegate { ShowDarkTab(settingsPage, settingsTabButton, new[] { outputPage, inputPage }, new[] { outputTabButton, inputTabButton }, panelBack, windowBack, accentBlue, textMain, textMuted); };
                mainPanel.Controls.Add(tabButtonPanel, 0, 0);
                mainPanel.Controls.Add(tabContentPanel, 0, 1);

                bottomPanel.Dock = DockStyle.Bottom;
                bottomPanel.Height = 92;
                bottomPanel.BackColor = windowBack;
                bottomPanel.ColumnCount = 2;
                bottomPanel.RowCount = 1;
                bottomPanel.Padding = new Padding(12, 8, 10, 10);
                bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

                watermarkLabel.Text = "Audio Tray by BeauBreau" + Environment.NewLine + "A light weight audio switcher Inspired by a classic.";
                watermarkLabel.Dock = DockStyle.Fill;
                watermarkLabel.AutoEllipsis = true;
                watermarkLabel.ForeColor = textMuted;
                watermarkLabel.BackColor = windowBack;
                watermarkLabel.Font = new Font(form.Font.FontFamily, 7.5f, FontStyle.Regular);
                watermarkLabel.TextAlign = ContentAlignment.MiddleLeft;

                rightFooterPanel.Dock = DockStyle.Right;
                rightFooterPanel.AutoSize = true;
                rightFooterPanel.BackColor = windowBack;
                rightFooterPanel.ColumnCount = 1;
                rightFooterPanel.RowCount = 2;

                buttonPanel.Dock = DockStyle.Right;
                buttonPanel.AutoSize = true;
                buttonPanel.BackColor = windowBack;
                buttonPanel.WrapContents = false;
                buttonPanel.FlowDirection = FlowDirection.LeftToRight;
                buttonPanel.Padding = new Padding(0, 8, 0, 0);

                versionLabel.Text = "Version " + UpdateManager.CurrentVersionText;
                versionLabel.AutoSize = true;
                versionLabel.Dock = DockStyle.Right;
                versionLabel.ForeColor = textMuted;
                versionLabel.BackColor = windowBack;
                versionLabel.TextAlign = ContentAlignment.MiddleRight;
                versionLabel.Margin = new Padding(0, 0, 0, 0);

                saveButton.Text = "Save";
                StyleSmallButton(saveButton, accentBlue, windowBack);
                cancelButton.Text = "Cancel";
                StyleSmallButton(cancelButton, accentBlue, windowBack);

                buttonPanel.Controls.Add(cancelButton);
                buttonPanel.Controls.Add(saveButton);
                rightFooterPanel.Controls.Add(buttonPanel, 0, 0);
                rightFooterPanel.Controls.Add(versionLabel, 0, 1);
                bottomPanel.Controls.Add(watermarkLabel, 0, 0);
                bottomPanel.Controls.Add(rightFooterPanel, 1, 0);
                contentPanel.Controls.Add(mainPanel);
                contentPanel.Controls.Add(bottomPanel);
                form.Controls.Add(contentPanel);
                form.Controls.Add(titleBar);

                cancelButton.Click += delegate { form.Close(); };
                saveButton.Click += delegate
                {
                    settings.OutputFavorites = GetCheckedDeviceIdsInDisplayOrder(outputList);
                    settings.InputFavorites = GetCheckedDeviceIdsInDisplayOrder(inputList);
                    settings.NotificationsEnabled = notificationsCheckBox.Checked;
                    settings.ShowDeviceIconInTray = trayDeviceIconCheckBox.Checked;
                    settings.CheckForUpdates = checkUpdatesCheckBox.Checked;
                    settings.InstallUpdatesAutomatically = autoInstallUpdatesCheckBox.Checked;
                    settings.LastFailedUpdateVersion = null;
                    SaveSettings(settings);
                    SetStartupEnabled(startupCheckBox.Checked);
                    UpdateTrayIconFromCurrentOutput(true);
                    form.Close();
                    ShowNotification("Settings saved. Right-click the tray icon to switch between favorites.");
                };

                form.FormClosed += delegate
                {
                    Settings latestSettings = LoadSettings();
                    SaveFavoritesWindowBounds(form, latestSettings);
                    SaveSettings(latestSettings);
                    favoritesWindow = null;
                    foreach (Icon icon in deviceIconCache.Values)
                    {
                        if (icon != null)
                        {
                            icon.Dispose();
                        }
                    }
                };

                favoritesWindow = form;
                form.Show();
        }

        private static List<AudioDevice> OrderDevicesForFavoritesWindow(List<AudioDevice> devices, List<string> favorites)
        {
            var deviceById = devices.ToDictionary(d => d.Id, d => d);
            var ordered = favorites
                .Where(id => deviceById.ContainsKey(id))
                .Select(id => deviceById[id])
                .ToList();

            ordered.AddRange(devices.Where(d => !favorites.Contains(d.Id)));
            return ordered;
        }

        private static void RestoreFavoritesWindowBounds(Form form, Settings settings)
        {
            if (!settings.FavoritesWindowX.HasValue ||
                !settings.FavoritesWindowY.HasValue ||
                !settings.FavoritesWindowWidth.HasValue ||
                !settings.FavoritesWindowHeight.HasValue)
            {
                return;
            }

            int width = Math.Max(settings.FavoritesWindowWidth.Value, form.MinimumSize.Width);
            int height = Math.Max(settings.FavoritesWindowHeight.Value, form.MinimumSize.Height);
            var bounds = new Rectangle(settings.FavoritesWindowX.Value, settings.FavoritesWindowY.Value, width, height);
            if (!IsWindowVisibleOnAnyScreen(bounds))
            {
                return;
            }

            form.StartPosition = FormStartPosition.Manual;
            form.Bounds = bounds;
        }

        private static void SaveFavoritesWindowBounds(Form form, Settings settings)
        {
            Rectangle bounds = form.WindowState == FormWindowState.Normal ? form.Bounds : form.RestoreBounds;
            settings.FavoritesWindowX = bounds.X;
            settings.FavoritesWindowY = bounds.Y;
            settings.FavoritesWindowWidth = bounds.Width;
            settings.FavoritesWindowHeight = bounds.Height;
        }

        private static bool IsWindowVisibleOnAnyScreen(Rectangle bounds)
        {
            foreach (Screen screen in Screen.AllScreens)
            {
                Rectangle visibleArea = Rectangle.Intersect(screen.WorkingArea, bounds);
                if (visibleArea.Width >= 80 && visibleArea.Height >= 80)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<string> GetCheckedDeviceIdsInDisplayOrder(ListBox list)
        {
            var ids = new List<string>();
            foreach (object item in list.Items)
            {
                var favoriteItem = item as FavoriteDeviceItem;
                if (favoriteItem != null && favoriteItem.IsChecked && favoriteItem.Device != null)
                {
                    ids.Add(favoriteItem.Device.Id);
                }
            }

            return ids;
        }

        private static void StyleSmallButton(Button button, Color accentBlue, Color backColor)
        {
            button.Width = 64;
            button.Height = 26;
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = Color.FromArgb(18, 45, 72);
            button.ForeColor = Color.FromArgb(235, 246, 252);
            button.FlatAppearance.BorderColor = accentBlue;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(34, 75, 110);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(46, 96, 132);
            button.Margin = new Padding(4, 3, 0, 3);
        }

        private static void StyleTitleButton(Button button, Color backColor, Color foreColor, Color accentBlue)
        {
            button.Width = 32;
            button.Height = 26;
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = backColor;
            button.ForeColor = foreColor;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(34, 75, 110);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(46, 96, 132);
            button.Margin = new Padding(1, 1, 0, 0);
            button.TabStop = false;
        }

        private static void StyleTabButton(Button button, string text, bool selected, Color selectedBack, Color unselectedBack, Color accentBlue, Color textMain, Color textMuted)
        {
            button.Text = text;
            button.Width = 82;
            button.Height = 30;
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = selected ? selectedBack : unselectedBack;
            button.ForeColor = selected ? textMain : textMuted;
            button.FlatAppearance.BorderColor = selected ? accentBlue : Color.FromArgb(55, 104, 142);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(34, 75, 110);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(46, 96, 132);
            button.Margin = new Padding(0, 0, 2, 0);
            button.TabStop = false;
        }

        private static void ShowDarkTab(Control selectedPage, Button selectedButton, Control[] otherPages, Button[] otherButtons, Color selectedBack, Color unselectedBack, Color accentBlue, Color textMain, Color textMuted)
        {
            selectedPage.Visible = true;
            selectedPage.BringToFront();
            StyleTabButton(selectedButton, selectedButton.Text, true, selectedBack, unselectedBack, accentBlue, textMain, textMuted);

            foreach (Control page in otherPages)
            {
                page.Visible = false;
            }

            foreach (Button button in otherButtons)
            {
                StyleTabButton(button, button.Text, false, selectedBack, unselectedBack, accentBlue, textMain, textMuted);
            }
        }

        private static void StyleCheckBox(CheckBox checkBox, Color backColor, Color foreColor)
        {
            checkBox.BackColor = backColor;
            checkBox.ForeColor = foreColor;
            checkBox.Margin = new Padding(3, 4, 3, 4);
        }

        private static void StyleDeviceList(ListBox list, Color backColor, Color foreColor)
        {
            list.BackColor = backColor;
            list.ForeColor = foreColor;
            list.BorderStyle = BorderStyle.None;
        }

        private static void ConfigureDeviceListDrawing(ListBox list, Dictionary<string, Icon> iconCache)
        {
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = 36;
            list.DrawItem += delegate(object sender, DrawItemEventArgs args)
            {
                DrawDeviceListItem((ListBox)sender, args, iconCache);
            };
        }

        private static void DrawDeviceListItem(ListBox list, DrawItemEventArgs args, Dictionary<string, Icon> iconCache)
        {
            if (args.Index < 0)
            {
                return;
            }

            bool selected = (args.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color selectedBack = Color.FromArgb(30, 82, 120);
            Color selectedText = Color.FromArgb(245, 250, 255);
            using (Brush background = new SolidBrush(selected ? selectedBack : list.BackColor))
            {
                args.Graphics.FillRectangle(background, args.Bounds);
            }

            var favoriteItem = list.Items[args.Index] as FavoriteDeviceItem;
            if (favoriteItem == null || favoriteItem.Device == null)
            {
                return;
            }

            bool isChecked = favoriteItem.IsChecked;
            System.Windows.Forms.VisualStyles.CheckBoxState checkState = isChecked
                ? System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal
                : System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal;
            Rectangle checkBounds = new Rectangle(args.Bounds.Left + 6, args.Bounds.Top + ((args.Bounds.Height - 16) / 2), 16, 16);
            CheckBoxRenderer.DrawCheckBox(args.Graphics, checkBounds.Location, checkState);

            Icon icon = GetCachedDeviceIcon(favoriteItem.Device, iconCache);
            Rectangle iconBounds = new Rectangle(args.Bounds.Left + 30, args.Bounds.Top + 6, 24, 24);
            if (icon != null)
            {
                args.Graphics.DrawIcon(icon, iconBounds);
            }

            Color textColor = selected ? selectedText : list.ForeColor;
            Rectangle textBounds = new Rectangle(args.Bounds.Left + 62, args.Bounds.Top, args.Bounds.Width - 68, args.Bounds.Height);
            TextRenderer.DrawText(args.Graphics, favoriteItem.Device.Name, list.Font, textBounds, textColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.Left);
            args.DrawFocusRectangle();
        }

        private static Icon GetCachedDeviceIcon(AudioDevice device, Dictionary<string, Icon> iconCache)
        {
            string key = device.Id ?? device.Name ?? "";
            if (!iconCache.ContainsKey(key))
            {
                iconCache[key] = IconLoader.LoadFromIconPath(device.IconPath);
            }

            return iconCache[key];
        }

        private static void MoveCheckedListItem(ListBox list, int direction)
        {
            int oldIndex = list.SelectedIndex;
            if (oldIndex < 0)
            {
                return;
            }

            int newIndex = oldIndex + direction;
            if (newIndex < 0 || newIndex >= list.Items.Count)
            {
                return;
            }

            object selectedItem = list.Items[oldIndex];

            list.Items.RemoveAt(oldIndex);
            list.Items.Insert(newIndex, selectedItem);
            list.SelectedIndex = newIndex;
        }

        private static void HandleDeviceListMouseDown(ListBox list, MouseEventArgs args)
        {
            int index = list.IndexFromPoint(args.Location);
            if (index < 0)
            {
                return;
            }

            list.SelectedIndex = index;
            int checkBoxWidth = 28;
            bool clickedCheckBox = args.X <= checkBoxWidth;
            bool doubleClickedText = args.Clicks >= 2 && !clickedCheckBox;

            if (clickedCheckBox || doubleClickedText)
            {
                var favoriteItem = list.Items[index] as FavoriteDeviceItem;
                if (favoriteItem != null)
                {
                    favoriteItem.IsChecked = !favoriteItem.IsChecked;
                    list.Invalidate(list.GetItemRectangle(index));
                }
            }
        }

        private static void OpenSoundControlPanel()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "control.exe",
                    Arguments = "mmsys.cpl",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Audio Tray could not open the Sound control panel." + Environment.NewLine + Environment.NewLine + ex.Message, "Audio Tray", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static bool IsStartupEnabledForCurrentLocation()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, false))
                {
                    string value = key == null ? null : key.GetValue(StartupValueName) as string;
                    string startupPath = GetExecutablePathFromStartupCommand(value);
                    return string.Equals(startupPath, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        private static void SetStartupEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true))
            {
                if (key == null)
                {
                    return;
                }

                if (enabled)
                {
                    key.SetValue(StartupValueName, GetStartupCommand(), RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(StartupValueName, false);
                }
            }
        }

        private static string GetStartupCommand()
        {
            return "\"" + Application.ExecutablePath + "\"";
        }

        private static string GetExecutablePathFromStartupCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return null;
            }

            command = Environment.ExpandEnvironmentVariables(command.Trim());
            if (command.StartsWith("\"", StringComparison.Ordinal))
            {
                int closeQuote = command.IndexOf('"', 1);
                return closeQuote > 1 ? command.Substring(1, closeQuote - 1) : null;
            }

            string exeMarker = ".exe";
            int exeIndex = command.IndexOf(exeMarker, StringComparison.OrdinalIgnoreCase);
            if (exeIndex >= 0)
            {
                return command.Substring(0, exeIndex + exeMarker.Length).Trim();
            }

            return command;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                iconRefreshTimer.Stop();
                iconRefreshTimer.Dispose();
                if (loadedTrayIcon != null)
                {
                    loadedTrayIcon.Dispose();
                }
                if (!ReferenceEquals(appTrayIcon, SystemIcons.Application))
                {
                    appTrayIcon.Dispose();
                }
                trayIcon.Dispose();
                menu.Dispose();
                uiInvoker.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    static class Program
    {
        private const string SingleInstanceMutexName = "BeauBreau.AudioTray.SingleInstance";

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Any(a => string.Equals(a, "--list-devices", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (AudioDevice device in AudioManager.GetDevices(EDataFlow.eRender))
                {
                    Console.WriteLine("Output: " + device.Name + " | Icon: " + (device.IconPath ?? ""));
                }
                foreach (AudioDevice device in AudioManager.GetDevices(EDataFlow.eCapture))
                {
                    Console.WriteLine("Input: " + device.Name + " | Icon: " + (device.IconPath ?? ""));
                }
                return;
            }

            bool createdNew;
            using (var mutex = new Mutex(true, SingleInstanceMutexName, out createdNew))
            {
                if (!createdNew)
                {
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayAppContext());
                GC.KeepAlive(mutex);
            }
        }
    }
}
