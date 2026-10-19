using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace MonitorSwitch
{
    [DataContract] internal sealed class ReleaseAsset
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "browser_download_url")] public string Url { get; set; }
        [DataMember(Name = "digest")] public string Digest { get; set; }
        [DataMember(Name = "size")] public long Size { get; set; }
        [DataMember(Name = "state")] public string State { get; set; }
    }
    [DataContract] internal sealed class GitHubRelease
    {
        [DataMember(Name = "tag_name")] public string Tag { get; set; }
        [DataMember(Name = "body")] public string Body { get; set; }
        [DataMember(Name = "draft")] public bool Draft { get; set; }
        [DataMember(Name = "prerelease")] public bool Prerelease { get; set; }
        [DataMember(Name = "assets")] public List<ReleaseAsset> Assets { get; set; }
    }
    internal sealed class UpdateInfo
    { public Version Version; public string Tag, Notes, Url, Hash; public long Size; }

    public sealed class UpdateManifest
    {
        public string Target { get; set; }
        public string Payload { get; set; }
        public string Hash { get; set; }
        public string Version { get; set; }
        public int ParentId { get; set; }
        public long ParentStarted { get; set; }
        public bool InTray { get; set; }
    }
    internal static class Updates
    {
        public const string Repository = "https://github.com/carellice/MonitorSwitch";
        public const string Endpoint = "https://api.github.com/repos/carellice/MonitorSwitch/releases/latest";
        public const long MaxBytes = 32 * 1024 * 1024;
        public static readonly string Folder = Path.Combine(ProfileStore.Folder, "updates");
        public static Version Current { get { return Assembly.GetExecutingAssembly().GetName().Version; } }
        public static string CurrentLabel { get { return Current.ToString(3); } }
        public static string StartupReceipt;
        public static bool RestartInTray;

        public static Version ParseVersion(string value)
        {
            if (value == null || !Regex.IsMatch(value, @"^v?\d+\.\d+\.\d+(\.\d+)?$")) return null;
            Version version;
            if (!Version.TryParse(value.TrimStart('v'), out version)) return null;
            return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
        }
        public static UpdateInfo ParseRelease(string json, Version current)
        {
            if (json == null || json.Length > 2 * 1024 * 1024) throw new InvalidDataException("Risposta GitHub non valida.");
            GitHubRelease release;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                release = (GitHubRelease)new DataContractJsonSerializer(typeof(GitHubRelease)).ReadObject(stream);
            if (release == null || release.Draft || release.Prerelease) return null;
            Version version = ParseVersion(release.Tag);
            if (version == null) throw new InvalidDataException("La versione della release GitHub non e riconoscibile.");
            if (version <= current) return null;
            ReleaseAsset asset = (release.Assets ?? new List<ReleaseAsset>()).FirstOrDefault(a => a.Name == "MonitorSwitch.exe" && a.State == "uploaded");
            string expected = Repository + "/releases/download/" + Uri.EscapeDataString(release.Tag) + "/MonitorSwitch.exe";
            if (asset == null || asset.Size < 1024 || asset.Size > MaxBytes || !String.Equals(asset.Url, expected, StringComparison.Ordinal))
                throw new InvalidDataException("La nuova release non contiene un eseguibile Windows pronto per l'aggiornamento. Riprova piu tardi.");
            if (asset.Digest == null || !Regex.IsMatch(asset.Digest, @"^sha256:[a-fA-F0-9]{64}$"))
                throw new InvalidDataException("Il checksum della nuova release non e ancora disponibile. Riprova piu tardi.");
            return new UpdateInfo { Version = version, Tag = release.Tag, Notes = release.Body ?? "", Url = asset.Url,
                Hash = asset.Digest.Substring(7).ToLowerInvariant(), Size = asset.Size };
        }
        private static HttpWebRequest Request(string url)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "MonitorSwitch/" + CurrentLabel;
            request.Accept = "application/vnd.github+json";
            request.Timeout = 20000; request.ReadWriteTimeout = 20000; request.AllowAutoRedirect = true; request.MaximumAutomaticRedirections = 5;
            return request;
        }
        private static void CheckResponse(HttpWebResponse response)
        {
            string host = response.ResponseUri.Host;
            if (response.ResponseUri.Scheme != "https" || !(host == "api.github.com" || host == "github.com" || host == "release-assets.githubusercontent.com" || host == "objects.githubusercontent.com"))
                throw new InvalidDataException("Destinazione del download GitHub non valida.");
        }
        public static Task<UpdateInfo> CheckAsync(CancellationToken cancel, Version current = null)
        {
            return Task.Run(delegate
            {
                HttpWebRequest request = Request(Endpoint);
                using (cancel.Register(request.Abort))
                {
                    try
                    {
                        using (var response = (HttpWebResponse)request.GetResponse())
                        using (var input = response.GetResponseStream())
                        using (var output = new MemoryStream())
                        {
                            CheckResponse(response); CopyBounded(input, output, 2 * 1024 * 1024, cancel, null, 0);
                            return ParseRelease(Encoding.UTF8.GetString(output.ToArray()), current ?? Current);
                        }
                    }
                    catch (WebException ex)
                    {
                        cancel.ThrowIfCancellationRequested();
                        var response = ex.Response as HttpWebResponse;
                        if (response != null && response.StatusCode == HttpStatusCode.NotFound) { response.Dispose(); return null; }
                        if (response != null) response.Dispose();
                        throw new IOException("Impossibile contattare GitHub. Verifica la connessione e riprova.", ex);
                    }
                }
            }, cancel);
        }
        private static void CopyBounded(Stream input, Stream output, long maximum, CancellationToken cancel, Action<int> progress, long expected)
        {
            byte[] buffer = new byte[32768]; long total = 0; int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
            {
                cancel.ThrowIfCancellationRequested(); total += read;
                if (total > maximum) throw new InvalidDataException("Il download supera la dimensione prevista.");
                output.Write(buffer, 0, read);
                if (progress != null && expected > 0) progress((int)Math.Min(100, total * 100 / expected));
            }
            if (expected > 0 && total != expected) throw new InvalidDataException("Download incompleto. Riprova.");
        }
        public static Task<string> DownloadAsync(UpdateInfo update, Action<int> progress, CancellationToken cancel, string destinationRoot = null)
        {
            return Task.Run(delegate
            {
                string directory = Path.Combine(destinationRoot ?? Folder, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
                string file = Path.Combine(directory, "MonitorSwitch.exe");
                HttpWebRequest request = Request(update.Url); request.Accept = "application/octet-stream";
                using (cancel.Register(request.Abort))
                {
                    try
                    {
                        using (var response = (HttpWebResponse)request.GetResponse())
                        using (var input = response.GetResponseStream())
                        using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        { CheckResponse(response); CopyBounded(input, output, MaxBytes, cancel, progress, update.Size); output.Flush(true); }
                        VerifyBinary(file, update.Hash, update.Version); return file;
                    }
                    catch { if (File.Exists(file)) File.Delete(file); throw; }
                }
            }, cancel);
        }
        public static string HashFile(string file)
        {
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(file))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        public static void VerifyBinary(string file, string expectedHash, Version expectedVersion)
        {
            if (expectedHash == null || !Regex.IsMatch(expectedHash, @"^[a-fA-F0-9]{64}$") || !String.Equals(HashFile(file), expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Il checksum dell'aggiornamento non corrisponde. Il programma attuale non e stato modificato.");
            using (var reader = new BinaryReader(File.OpenRead(file)))
            {
                if (reader.BaseStream.Length < 1024 || reader.BaseStream.Length > MaxBytes || reader.ReadUInt16() != 0x5A4D) throw new InvalidDataException("Eseguibile di aggiornamento non valido.");
                reader.BaseStream.Position = 0x3c; int offset = reader.ReadInt32();
                if (offset < 64 || offset > reader.BaseStream.Length - 6) throw new InvalidDataException("Intestazione eseguibile non valida.");
                reader.BaseStream.Position = offset;
                if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != 0x8664) throw new InvalidDataException("L'aggiornamento non e un eseguibile Windows x64.");
            }
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(file);
            if (info.ProductName != "MonitorSwitch" || ParseVersion(info.FileVersion) != expectedVersion)
                throw new InvalidDataException("La versione del file scaricato non corrisponde alla release.");
        }
        internal static void ReplaceAndStart(string payload, string target, string backup, string hash, Version version, Action start)
        {
            VerifyBinary(payload, hash, version);
            string temporary = target + ".update-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(payload, temporary); VerifyBinary(temporary, hash, version);
                File.Replace(temporary, target, backup);
                try { start(); }
                catch { File.Replace(backup, target, null); throw; }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public static string Quote(string value)
        {
            if (value.IndexOf('"') >= 0 || value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0) throw new InvalidDataException("Percorso non valido.");
            return "\"" + value + "\"";
        }
        private static bool InFolder(string path, string folder)
        { return Path.GetFullPath(path).StartsWith(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); }
        internal static void CheckManifest(UpdateManifest manifest, string manifestPath, string root)
        {
            if (manifest == null || !InFolder(manifestPath, root) || Path.GetFileName(manifestPath) != "update.xml") throw new InvalidDataException("Richiesta aggiornamento non valida.");
            string directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath));
            if (!String.Equals(Path.GetFullPath(manifest.Payload), Path.Combine(directory, "MonitorSwitch.exe"), StringComparison.OrdinalIgnoreCase) ||
                !Path.IsPathRooted(manifest.Target) || !File.Exists(manifest.Target) || !manifest.Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || InFolder(manifest.Target, root) ||
                manifest.ParentId <= 0 || manifest.ParentStarted <= 0 || ParseVersion(manifest.Version) == null)
                throw new InvalidDataException("Percorsi di aggiornamento non validi.");
            VerifyBinary(manifest.Payload, manifest.Hash, ParseVersion(manifest.Version));
        }
        public static Task LaunchAsync(string payload, bool inTray)
        {
            string directory = Path.GetDirectoryName(payload), target = System.Windows.Forms.Application.ExecutablePath;
            var manifest = new UpdateManifest { Target = target, Payload = payload, Hash = HashFile(payload),
                Version = FileVersionInfo.GetVersionInfo(payload).FileVersion, ParentId = Process.GetCurrentProcess().Id,
                ParentStarted = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks, InTray = inTray };
            string manifestPath = Path.Combine(directory, "update.xml");
            CheckManifest(manifest, manifestPath, Folder);
            string probe = Path.Combine(Path.GetDirectoryName(target), ".MonitorSwitch-write-" + Guid.NewGuid().ToString("N"));
            try { using (File.Create(probe)) { } File.Delete(probe); }
            catch (UnauthorizedAccessException) { throw new IOException("La cartella del programma non e scrivibile. Sposta MonitorSwitch in una cartella personale e riprova."); }
            using (var stream = File.Create(manifestPath)) new XmlSerializer(typeof(UpdateManifest)).Serialize(stream, manifest);
            string helper = Path.Combine(directory, "MonitorSwitch.Updater.exe"); File.Copy(target, helper, true);
            return Task.Run(delegate
            {
                using (Process process = Process.Start(new ProcessStartInfo(helper, "--apply-update " + Quote(manifestPath)) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }))
                {
                    Stopwatch wait = Stopwatch.StartNew(); string ready = Path.Combine(directory, "ready.ok");
                    while (!File.Exists(ready))
                    { if (process.HasExited || wait.ElapsedMilliseconds > 8000) throw new IOException("Impossibile avviare l'aggiornamento. Il programma resta aperto."); Thread.Sleep(50); }
                }
            });
        }
        public static int Apply(string manifestPath)
        {
            try
            {
                UpdateManifest manifest;
                using (XmlReader reader = XmlReader.Create(manifestPath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65536 }))
                    manifest = (UpdateManifest)new XmlSerializer(typeof(UpdateManifest)).Deserialize(reader);
                CheckManifest(manifest, manifestPath, Folder);
                string directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath));
                if (!String.Equals(System.Windows.Forms.Application.ExecutablePath, Path.Combine(directory, "MonitorSwitch.Updater.exe"), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Processo di aggiornamento non valido.");
                using (Process parent = Process.GetProcessById(manifest.ParentId))
                {
                    if (parent.StartTime.ToUniversalTime().Ticks != manifest.ParentStarted || !String.Equals(parent.MainModule.FileName, manifest.Target, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Il processo da aggiornare e cambiato.");
                    File.WriteAllText(Path.Combine(directory, "ready.ok"), "ready");
                    if (!parent.WaitForExit(20000)) throw new IOException("MonitorSwitch non si e chiuso. Chiudilo e riprova l'aggiornamento.");
                }
                string backup = manifest.Target + ".previous", receipt = Path.Combine(directory, "started.ok");
                if (File.Exists(backup)) File.Delete(backup);
                ReplaceAndStart(manifest.Payload, manifest.Target, backup, manifest.Hash, ParseVersion(manifest.Version), delegate
                {
                    using (Process restarted = Process.Start(new ProcessStartInfo(manifest.Target, "--updated " + Quote(receipt) + (manifest.InTray ? " --tray" : "")) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(manifest.Target) }))
                    {
                        Stopwatch wait = Stopwatch.StartNew();
                        while (!File.Exists(receipt))
                        {
                            if (restarted.HasExited) throw new IOException("La nuova versione non si e avviata. Ripristinata la versione precedente.");
                            if (wait.ElapsedMilliseconds > 20000) return; // Keep backup if startup is slow; do not kill a live app.
                            Thread.Sleep(100);
                        }
                    }
                });
                return 0;
            }
            catch (Exception ex)
            {
                Recovery.Log(ex);
                System.Windows.Forms.MessageBox.Show(ex.Message + "\r\nPuoi riaprire MonitorSwitch e riprovare.", "Aggiornamento non riuscito", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
                return 1;
            }
        }
        public static void SignalStarted()
        {
            if (String.IsNullOrEmpty(StartupReceipt)) return;
            if (InFolder(StartupReceipt, Folder) && Path.GetFileName(StartupReceipt) == "started.ok" && File.Exists(Path.Combine(Path.GetDirectoryName(StartupReceipt), "update.xml")))
                File.WriteAllText(StartupReceipt, "started");
        }
    }
}
