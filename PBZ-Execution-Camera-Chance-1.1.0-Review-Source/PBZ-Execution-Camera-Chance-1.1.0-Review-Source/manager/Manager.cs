using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;

[assembly: AssemblyTitle("影之刃零 · 处决特写概率")]
[assembly: AssemblyDescription("独立安装、概率设置与完整卸载")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace PBZCamera
{
    public sealed class ModStatus
    {
        public bool Installed;
        public decimal Chance = 15;
        public string Description;
    }

    // These are the only dependency paths this program can install or own.
    public sealed class ManagerCore
    {
        public const string Version = "1.1.0";
        public const string ModName = "PBZExecutionCameraChance";
        public const string GameVersion = "CL-301524_JKS-17219";
        public static readonly string[] LoaderFiles = {
            "dwmapi.dll", "ue4ss/UE4SS.dll", "ue4ss/MemberVariableLayout.ini",
            "ue4ss/UE4SS-settings.ini", "ue4ss/LICENSE",
            "ue4ss/UE4SS_Signatures/GUObjectArray.lua", "ue4ss/UE4SS_Signatures/GNatives.lua"
        };
        public static readonly string[] ModFiles = {
            "enabled.txt", "Scripts/main.lua", "Scripts/policy.lua", "Scripts/sessions.lua", "Scripts/config.lua"
        };
        private const string StateName = "install-state.xml";
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static readonly Regex ChancePattern = new Regex(
            @"(?:^|[,{])\s*ChancePercent\s*=\s*(?<value>[+-]?(?:[0-9]+(?:\.[0-9]+)?|\.[0-9]+))(?=\s*[,}])", RegexOptions.Multiline);
        private readonly Dictionary<string, byte[]> payload;

        public ManagerCore()
        {
            payload = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            Assembly assembly = typeof(ManagerCore).Assembly;
            using (Stream manifest = assembly.GetManifestResourceStream("PBZ.Manifest"))
            using (Stream data = assembly.GetManifestResourceStream("PBZ.Payload"))
            {
                if (manifest == null || data == null) throw new InvalidDataException("程序的内置安装包缺失。请重新下载完整文件。");
                XDocument doc = ReadXml(manifest);
                HashSet<string> allowed = new HashSet<string>(LoaderFiles, StringComparer.OrdinalIgnoreCase);
                foreach (string file in ModFiles) allowed.Add("mod/" + file);
                using (ZipArchive zip = new ZipArchive(data, ZipArchiveMode.Read))
                {
                    foreach (XElement item in doc.Root.Elements("File"))
                    {
                        string path = (string)item.Attribute("Path");
                        if (!allowed.Remove(path)) throw new InvalidDataException("内置文件清单异常。");
                        ZipArchiveEntry entry = zip.GetEntry(path);
                        if (entry == null) throw new InvalidDataException("内置文件缺失：" + path);
                        byte[] bytes;
                        using (Stream input = entry.Open())
                        using (MemoryStream output = new MemoryStream()) { input.CopyTo(output); bytes = output.ToArray(); }
                        if (!String.Equals(Hash(bytes), (string)item.Attribute("SHA256"), StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("内置文件校验失败：" + path);
                        payload.Add(path, bytes);
                    }
                    if (allowed.Count != 0 || zip.Entries.Count != payload.Count) throw new InvalidDataException("内置安装包不完整。");
                }
            }
        }

        public static string NormalizeRoot(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("请选择 PBZ 游戏目录。");
            string root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
            if (String.Equals(Path.GetExtension(root), ".exe", StringComparison.OrdinalIgnoreCase)) root = Path.GetDirectoryName(root);
            if (String.Equals(Path.GetFileName(root), "steam", StringComparison.OrdinalIgnoreCase))
            {
                string parent = Path.GetDirectoryName(root);
                if (String.Equals(Path.GetFileName(parent), "platform", StringComparison.OrdinalIgnoreCase)) root = Path.GetDirectoryName(parent);
                else if (Directory.Exists(Path.Combine(parent, "ProjectPBZ"))) root = parent;
            }
            return root.TrimEnd(Path.DirectorySeparatorChar);
        }

        private static string Bin(string root) { return Path.Combine(root, "platform", "steam"); }
        private static string Mod(string root) { return Path.Combine(Bin(root), "ue4ss", "Mods", ModName); }
        private static string Under(string root, string relative)
        {
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("文件路径超出目标目录。");
            // Refuse junctions/symlinks inside the selected game, including at the root.
            for (string current = path; current != null && current.Length >= root.Length; current = Path.GetDirectoryName(current))
            {
                if ((Directory.Exists(current) || File.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("目标路径包含链接，请选择实际的游戏目录：" + current);
                if (String.Equals(current, root, StringComparison.OrdinalIgnoreCase)) break;
            }
            return path;
        }

        private static void CheckGame(string root, bool version, bool stopped)
        {
            string exe = Under(root, "platform/steam/PBZ.exe");
            if (!File.Exists(exe)) throw new InvalidOperationException("未找到 platform\\steam\\PBZ.exe，请选择 PBZ 游戏根目录。");
            if (version && FileVersionInfo.GetVersionInfo(exe).FileVersion != GameVersion)
                throw new InvalidOperationException("此安装包适配 " + GameVersion + "（Steam build 26266538）。当前游戏版本尚未验证，已停止安装。");
            if (!stopped) return;
            foreach (Process process in Process.GetProcessesByName("PBZ"))
            using (process)
            {
                string file;
                try { file = process.MainModule.FileName; }
                catch { throw new InvalidOperationException("检测到游戏正在运行。请正常退出游戏后再安装或卸载。"); }
                if (String.Equals(file, exe, StringComparison.OrdinalIgnoreCase) ||
                    file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("请正常退出游戏后再安装或卸载。");
            }
        }

        public ModStatus Inspect(string gameRoot)
        {
            string root = NormalizeRoot(gameRoot);
            CheckGame(root, false, false);
            string config = Under(root, "platform/steam/ue4ss/Mods/" + ModName + "/Scripts/config.lua");
            ModStatus status = new ModStatus { Installed = File.Exists(config) };
            if (status.Installed)
            {
                status.Chance = ReadChance(File.ReadAllText(config));
                status.Description = "已安装 · 当前普通敌人特写概率 " + status.Chance.ToString("0.#", CultureInfo.InvariantCulture) + "%";
            }
            else status.Description = "尚未安装 · 新安装默认 15%，也可以先调整概率再安装。";
            return status;
        }

        public string Install(string gameRoot, decimal? chance)
        {
            string root = NormalizeRoot(gameRoot);
            CheckGame(root, true, true);
            if (chance.HasValue) CheckChance(chance.Value);
            string bin = Bin(root), mod = Mod(root), runtime = Path.Combine(bin, "ue4ss");
            Under(root, "platform/steam/ue4ss/Mods/" + ModName);
            // Existing loader settings are preserved. Binary/layout/signature conflicts are rejected before any writes.
            foreach (string rel in LoaderFiles)
            {
                string target = Under(root, "platform/steam/" + rel);
                if (File.Exists(target) && rel != "ue4ss/UE4SS-settings.ini" && rel != "ue4ss/LICENSE" &&
                    Hash(File.ReadAllBytes(target)) != Hash(payload[rel]))
                    throw new InvalidOperationException("已有加载器与内置版本不同，未覆盖任何文件：" + rel);
            }
            string settingsPath = Under(root, "platform/steam/ue4ss/UE4SS-settings.ini");
            if (File.Exists(settingsPath))
            {
                foreach (Match setting in Regex.Matches(File.ReadAllText(settingsPath), @"(?m)^(?:ModsFolderPath|ControllingModsTxt)[ \t]*=[ \t]*([^;\r\n]*)"))
                    if (!String.IsNullOrWhiteSpace(setting.Groups[1].Value))
                        throw new InvalidOperationException("已有加载器自定义了 Mod 目录或控制列表。请先恢复 UE4SS-settings.ini 中的 ModsFolderPath / ControllingModsTxt 默认值，再安装。");
            }
            string configPath = Path.Combine(mod, "Scripts", "config.lua");
            string config = File.Exists(configPath) ? File.ReadAllText(configPath) : Utf8.GetString(payload["mod/Scripts/config.lua"]);
            if (chance.HasValue) config = ReplaceChance(config, chance.Value);
            else ReadChance(config); // Validate before creating anything; never execute a Lua configuration in the manager.
            XElement previous = LoadState(root);
            bool ownRuntime = previous != null ? (bool?)previous.Attribute("OwnRuntime") == true : !Directory.Exists(runtime);
            HashSet<string> owned = previous == null ? new HashSet<string>() : new HashSet<string>(previous.Elements("Owned").Select(e => (string)e.Attribute("Path")));
            foreach (string rel in LoaderFiles) if (!File.Exists(Path.Combine(bin, rel))) owned.Add(rel);
            XElement state = new XElement("PBZExecutionCameraInstall", new XAttribute("Root", root),
                new XAttribute("Version", Version), new XAttribute("OwnRuntime", ownRuntime),
                LoaderFiles.Where(owned.Contains).Select(p => new XElement("Owned", new XAttribute("Path", p), new XAttribute("SHA256", Hash(payload[p])))));
            using (FileTransaction tx = new FileTransaction(root))
            {
                foreach (string rel in LoaderFiles)
                {
                    string target = Under(root, "platform/steam/" + rel);
                    if (!File.Exists(target)) tx.Write(target, payload[rel]);
                }
                // Preserve the previous code as a local backup on upgrades. Uninstall removes these backups too.
                string backup = "Backups/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + "/";
                foreach (string rel in ModFiles)
                {
                    string target = Under(root, "platform/steam/ue4ss/Mods/" + ModName + "/" + rel);
                    byte[] bytes = rel == "Scripts/config.lua" ? Utf8.GetBytes(config) : payload["mod/" + rel];
                    if (File.Exists(target) && rel != "enabled.txt" && rel != "Scripts/config.lua" && Hash(File.ReadAllBytes(target)) != Hash(bytes))
                        tx.Write(Under(mod, backup + Path.GetFileName(rel)), File.ReadAllBytes(target));
                    if (rel != "Scripts/config.lua" || chance.HasValue || !File.Exists(target)) tx.Write(target, bytes);
                }
                UpdateList(root, tx, false);
                tx.Write(Path.Combine(mod, StateName), Utf8.GetBytes(state.ToString()));
                tx.Commit();
            }
            return "安装完成 · 普通敌人特写概率 " + ReadChance(config).ToString("0.#", CultureInfo.InvariantCulture) + "%。“Boss”保留原有特写。\r\n现在可以从 Steam 启动游戏。";
        }

        public string SaveChance(string gameRoot, decimal chance)
        {
            CheckChance(chance);
            string root = NormalizeRoot(gameRoot);
            CheckGame(root, false, false);
            string path = Under(root, "platform/steam/ue4ss/Mods/" + ModName + "/Scripts/config.lua");
            if (!File.Exists(path)) throw new InvalidOperationException("请先安装 Mod，再保存概率。");
            using (FileTransaction tx = new FileTransaction(root))
            {
                tx.Write(path, Utf8.GetBytes(ReplaceChance(File.ReadAllText(path), chance)));
                tx.Commit();
            }
            return "已保存 " + chance.ToString("0.#", CultureInfo.InvariantCulture) + "% 。\r\n游戏已启动时，切回游戏按 Ctrl+Alt+F6 生效；下次启动也会自动读取。";
        }

        public string Uninstall(string gameRoot)
        {
            string root = NormalizeRoot(gameRoot);
            CheckGame(root, false, true); // Uninstall must still work after a game update.
            string bin = Bin(root), mod = Under(root, "platform/steam/ue4ss/Mods/" + ModName);
            string runtime = Path.Combine(bin, "ue4ss");
            XElement state = LoadState(root);
            bool cleanRuntime = CanRemoveRuntime(root, state);
            bool ownProxy = state != null && state.Elements("Owned").Any(e => (string)e.Attribute("Path") == "dwmapi.dll");
            if (Directory.Exists(mod)) EnsureTreeHasNoLinks(mod);
            if (cleanRuntime) EnsureTreeHasNoLinks(runtime);
            // Stage whole directories by rename. If a file is locked, rollback restores its original path.
            string staged = null;
            using (FileTransaction tx = new FileTransaction(root))
            {
                try
                {
                    if (Directory.Exists(mod))
                    {
                        staged = Under(root, "platform/steam/.pbz-camera-remove-" + Guid.NewGuid().ToString("N"));
                        Directory.Move(mod, staged);
                    }
                    UpdateList(root, tx, true);
                    if (cleanRuntime)
                    {
                        foreach (string path in Directory.GetFiles(runtime, "*", SearchOption.AllDirectories)) tx.Delete(path);
                        if (ownProxy) tx.Delete(Under(root, "platform/steam/dwmapi.dll"));
                    }
                    // Delete staged mod files transactionally as well, including config and code backups.
                    if (staged != null)
                        foreach (string path in Directory.GetFiles(staged, "*", SearchOption.AllDirectories)) tx.Delete(path);
                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    if (staged != null && Directory.Exists(staged) && !Directory.Exists(mod)) Directory.Move(staged, mod);
                    throw;
                }
            }
            if (staged != null) RemoveEmptyTree(staged);
            if (cleanRuntime) RemoveEmptyTree(runtime);
            return cleanRuntime
                ? "已彻底卸载 · 本 Mod、配置、备份和由本程序安装的独立加载器已删除。"
                : "已彻底卸载 · 本 Mod、配置及备份已删除。\r\n既有加载器、共享加载器或新增文件已保留，其他 Mod 可继续使用。";
        }

        private bool CanRemoveRuntime(string root, XElement state)
        {
            if (state == null || (bool?)state.Attribute("OwnRuntime") != true) return false;
            string bin = Bin(root), runtime = Path.Combine(bin, "ue4ss"), mods = Path.Combine(runtime, "Mods");
            if (!Directory.Exists(runtime)) return false;
            if (Directory.Exists(mods) && Directory.GetDirectories(mods).Any(p => Path.GetFileName(p) != ModName)) return false;
            HashSet<string> owned = new HashSet<string>(state.Elements("Owned").Select(e => (string)e.Attribute("Path")), StringComparer.OrdinalIgnoreCase);
            // Only remove a whole runtime which this installer created, whose original files are unchanged.
            foreach (string rel in LoaderFiles.Where(p => p.StartsWith("ue4ss/")))
            {
                string file = Under(root, "platform/steam/" + rel);
                if (!owned.Contains(rel) || (File.Exists(file) && Hash(File.ReadAllBytes(file)) != Hash(payload[rel]))) return false;
            }
            if (owned.Contains("dwmapi.dll") && File.Exists(Path.Combine(bin, "dwmapi.dll")) &&
                Hash(File.ReadAllBytes(Path.Combine(bin, "dwmapi.dll"))) != Hash(payload["dwmapi.dll"])) return false;
            EnsureTreeHasNoLinks(runtime);
            string prefix = runtime + Path.DirectorySeparatorChar;
            foreach (string path in Directory.GetDirectories(runtime, "*", SearchOption.AllDirectories))
            {
                string rel = path.Substring(prefix.Length).Replace('\\', '/');
                if (rel != "Mods" && rel != "UE4SS_Signatures" && rel != "Mods/" + ModName && !rel.StartsWith("Mods/" + ModName + "/")) return false;
            }
            foreach (string path in Directory.GetFiles(runtime, "*", SearchOption.AllDirectories))
            {
                string rel = path.Substring(prefix.Length).Replace('\\', '/');
                if (rel.StartsWith("Mods/" + ModName + "/")) continue;
                if (LoaderFiles.Contains("ue4ss/" + rel)) continue;
                if (rel == "Mods/mods.txt")
                {
                    string text = RemoveOwnListEntries(File.ReadAllText(path));
                    if (text.Split('\n').Any(line => line.Trim().Length > 0 && !line.TrimStart().StartsWith(";"))) return false;
                    continue;
                }
                if (rel == "UE4SS.log" || Regex.IsMatch(rel, @"^crash_[0-9_\.]+\.dmp$")) continue;
                return false; // Keep unknown user data and shared dependencies.
            }
            return true;
        }

        private static XElement LoadState(string root)
        {
            string path = Under(root, "platform/steam/ue4ss/Mods/" + ModName + "/" + StateName);
            if (!File.Exists(path)) return null;
            XElement element;
            try { using (Stream input = File.OpenRead(path)) element = ReadXml(input).Root; }
            catch (Exception e) { throw new InvalidDataException("安装记录损坏，未修改游戏文件：" + e.Message); }
            HashSet<string> seen = new HashSet<string>();
            if (element.Name != "PBZExecutionCameraInstall") throw new InvalidDataException("安装记录格式无效，未修改游戏文件。");
            foreach (XElement item in element.Elements("Owned"))
            {
                string rel = (string)item.Attribute("Path");
                if (!LoaderFiles.Contains(rel) || !seen.Add(rel)) throw new InvalidDataException("安装记录包含无效路径。");
            }
            // Steam can move a game library. Uninstall remains available, but do not infer loader ownership at the new location.
            if (!String.Equals((string)element.Attribute("Root"), root, StringComparison.OrdinalIgnoreCase))
            {
                element.SetAttributeValue("OwnRuntime", false);
                element.Elements("Owned").Remove();
            }
            return element;
        }

        private static XDocument ReadXml(Stream stream)
        {
            using (XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                return XDocument.Load(reader);
        }
        public static string Hash(byte[] bytes)
        {
            using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");
        }
        private static decimal ReadChance(string text)
        {
            MatchCollection matches = ChancePattern.Matches(text);
            decimal chance;
            if (matches.Count != 1 || !Decimal.TryParse(matches[0].Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out chance))
                throw new InvalidDataException("config.lua 中的 ChancePercent 格式无法识别。请保留一项 ChancePercent = 15, 这样的数字配置。");
            CheckChance(chance);
            return chance;
        }
        private static string ReplaceChance(string text, decimal chance)
        {
            ReadChance(text);
            Group value = ChancePattern.Match(text).Groups["value"];
            return text.Substring(0, value.Index) + chance.ToString("0.#", CultureInfo.InvariantCulture) + text.Substring(value.Index + value.Length);
        }
        private static void CheckChance(decimal chance)
        {
            if (chance < 0 || chance > 100 || Decimal.Round(chance, 1) != chance) throw new ArgumentOutOfRangeException("chance", "概率范围为 0–100%，最多一位小数。");
        }
        private static string RemoveOwnListEntries(string text)
        {
            return Regex.Replace(text, @"(?m)^[ \t]*PBZExecutionCameraChance[ \t]*:[^\r\n]*(?:\r?\n|$)", "");
        }
        private static void UpdateList(string root, FileTransaction tx, bool remove)
        {
            string path = Under(root, "platform/steam/ue4ss/Mods/mods.txt");
            if (!File.Exists(path)) return; // enabled.txt is sufficient when no list exists.
            string text;
            Encoding encoding;
            using (StreamReader reader = new StreamReader(path, Utf8, true)) { text = reader.ReadToEnd(); encoding = reader.CurrentEncoding; }
            string updated = remove ? RemoveOwnListEntries(text) : Regex.Replace(text,
                @"(?m)^([ \t]*PBZExecutionCameraChance[ \t]*:[ \t]*)[01](?=[ \t]*(?:;[^\r\n]*)?\r?$)", "${1}1");
            if (text == updated) return;
            byte[] preamble = encoding.GetPreamble();
            byte[] content = encoding.GetBytes(updated);
            // Preserve the input BOM, rather than adding a BOM to BOM-less UTF-8 lists.
            byte[] original = File.ReadAllBytes(path);
            bool hadBom = preamble.Length > 0 && original.Take(preamble.Length).SequenceEqual(preamble);
            tx.Write(path, hadBom ? preamble.Concat(content).ToArray() : content);
        }
        private static void EnsureTreeHasNoLinks(string directory)
        {
            foreach (string path in Directory.GetFileSystemEntries(directory))
            {
                FileAttributes attr = File.GetAttributes(path);
                if ((attr & FileAttributes.ReparsePoint) != 0) throw new IOException("目录包含链接，已停止清理：" + path);
                if ((attr & FileAttributes.Directory) != 0) EnsureTreeHasNoLinks(path);
            }
        }
        private static void RemoveEmptyTree(string directory)
        {
            if (!Directory.Exists(directory)) return;
            foreach (string child in Directory.GetDirectories(directory)) RemoveEmptyTree(child);
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory, false);
        }

        public static string FindSteamGame()
        {
            HashSet<string> steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in new[] { @"HKEY_CURRENT_USER\Software\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam" })
                foreach (string value in new[] { "SteamPath", "InstallPath" })
                {
                    try { string path = Registry.GetValue(key, value, null) as string; if (!String.IsNullOrEmpty(path)) steamRoots.Add(path); }
                    catch (System.Security.SecurityException) { }
                    catch (UnauthorizedAccessException) { }
                }
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!String.IsNullOrEmpty(programFiles)) steamRoots.Add(Path.Combine(programFiles, "Steam"));
            HashSet<string> libraries = new HashSet<string>(steamRoots, StringComparer.OrdinalIgnoreCase);
            foreach (string steam in steamRoots)
            {
                string path = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                try {
                    if (File.Exists(path)) foreach (Match match in Regex.Matches(File.ReadAllText(path), "\"path\"\\s+\"([^\"\\r\\n]+)\""))
                        libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
                } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            foreach (string library in libraries)
            {
                string manifest = Path.Combine(library, "steamapps", "appmanifest_4115450.acf"), folder = "PBZ";
                if (File.Exists(manifest))
                {
                    Match match = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"\\r\\n]+)\"");
                    if (match.Success) folder = match.Groups[1].Value;
                }
                string candidate = Path.Combine(library, "steamapps", "common", folder);
                if (File.Exists(Path.Combine(candidate, "platform", "steam", "PBZ.exe"))) return candidate;
            }
            return null;
        }

        // In-memory journal: no settings, ownership or staging files are left after a failed operation.
        private sealed class FileTransaction : IDisposable
        {
            private readonly string root;
            private readonly Dictionary<string, byte[]> before = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> createdDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private bool finished;
            public FileTransaction(string gameRoot) { root = gameRoot; }
            public void Write(string path, byte[] bytes)
            {
                Under(root, path.Substring(root.Length + 1));
                bool first = !before.ContainsKey(path);
                byte[] original = first && File.Exists(path) ? File.ReadAllBytes(path) : null;
                string parent = Path.GetDirectoryName(path);
                for (string current = parent; !Directory.Exists(current); current = Path.GetDirectoryName(current)) createdDirs.Add(current);
                Directory.CreateDirectory(parent);
                AtomicWrite(path, bytes);
                if (first) before.Add(path, original);
            }
            public void Delete(string path)
            {
                Under(root, path.Substring(root.Length + 1));
                bool first = !before.ContainsKey(path);
                byte[] original = first && File.Exists(path) ? File.ReadAllBytes(path) : null;
                File.Delete(path);
                if (first) before.Add(path, original);
            }
            public void Commit() { finished = true; }
            public void Rollback()
            {
                if (finished) return;
                foreach (KeyValuePair<string, byte[]> entry in before.Reverse())
                {
                    if (entry.Value == null) { if (File.Exists(entry.Key)) File.Delete(entry.Key); }
                    else { Directory.CreateDirectory(Path.GetDirectoryName(entry.Key)); AtomicWrite(entry.Key, entry.Value); }
                }
                foreach (string path in createdDirs.OrderByDescending(p => p.Length))
                    if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path, false);
                finished = true;
            }
            private static void AtomicWrite(string path, byte[] bytes)
            {
                string temp = Path.Combine(Path.GetDirectoryName(path), "~pbz-" + Guid.NewGuid().ToString("N").Substring(0, 10) + ".tmp");
                try
                {
                    File.WriteAllBytes(temp, bytes);
                    if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            public void Dispose() { if (!finished) Rollback(); }
        }
    }

    public sealed class ManagerForm : Form
    {
        private readonly ManagerCore core;
        private readonly TextBox gamePath = new TextBox();
        private readonly TrackBar slider = new TrackBar();
        private readonly NumericUpDown percent = new NumericUpDown();
        private readonly TextBox status = new TextBox();
        private readonly Button install = new Button(), save = new Button(), uninstall = new Button();
        private bool syncing;
        private bool dirty;
        public ManagerForm(ManagerCore manager)
        {
            core = manager;
            Text = "影之刃零 · 处决特写概率 " + ManagerCore.Version;
            Font = new Font("Microsoft YaHei UI", 10F);
            ClientSize = new Size(742, 513);
            MinimumSize = Size;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(245, 247, 250);
            AddLabel("处决特写概率", 28, 22, 670, 40, 22, true);
            AddLabel("影之刃零 · 独立 Mod 管理器 " + ManagerCore.Version + " · 已内置加载器", 30, 69, 680, 25, 10, false);
            AddLabel("游戏目录", 30, 116, 660, 23, 10, true);
            gamePath.SetBounds(30, 146, 490, 30);
            gamePath.AccessibleName = "游戏目录";
            Controls.Add(gamePath);
            Button browse = MakeButton("选择目录…", 531, 144, 102, 34);
            Button detect = MakeButton("自动查找", 641, 144, 76, 34);
            detect.Font = new Font(Font.FontFamily, 9F);
            browse.Click += delegate {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog()) {
                    dialog.Description = "选择 PBZ 游戏根目录（内含 platform 文件夹）";
                    dialog.ShowNewFolderButton = false;
                    if (Directory.Exists(gamePath.Text)) dialog.SelectedPath = gamePath.Text;
                    if (dialog.ShowDialog(this) == DialogResult.OK) { gamePath.Text = dialog.SelectedPath; RefreshStatus(); }
                }
            };
            detect.Click += delegate { DetectGame(); };
            gamePath.Leave += delegate { RefreshStatus(); };
            AddLabel("普通敌人播放特写的概率", 30, 207, 460, 28, 12, true);
            slider.SetBounds(24, 248, 530, 48);
            slider.Minimum = 0; slider.Maximum = 100; slider.TickFrequency = 10; slider.Value = 15;
            slider.AccessibleName = "普通敌人特写概率滑块";
            Controls.Add(slider);
            percent.SetBounds(580, 249, 90, 31);
            percent.Minimum = 0; percent.Maximum = 100; percent.DecimalPlaces = 1; percent.Increment = 1; percent.Value = 15;
            percent.AccessibleName = "普通敌人特写概率数值";
            Controls.Add(percent);
            AddLabel("%", 680, 252, 30, 25, 12, true);
            slider.ValueChanged += delegate { if (!syncing) { syncing = true; percent.Value = slider.Value; syncing = false; dirty = true; } };
            percent.ValueChanged += delegate { if (!syncing) { syncing = true; slider.Value = (int)Decimal.Round(percent.Value); syncing = false; dirty = true; } };
            AddLabel("0%：不播放特写                 100%：每次保留特写", 30, 297, 670, 24, 10, false);
            AddLabel("Boss 保留原有特写。处决动作和伤害保持原有行为。", 30, 325, 680, 24, 10, false);
            SetupButton(install, "安装 / 更新", 30, 370, 216, 42);
            install.BackColor = Color.FromArgb(32, 92, 174); install.ForeColor = Color.White;
            SetupButton(save, "保存概率", 263, 370, 216, 42);
            SetupButton(uninstall, "彻底卸载（含配置）", 496, 370, 216, 42);
            install.Click += delegate { RunAction(delegate { return core.Install(gamePath.Text, percent.Value); }); };
            save.Click += delegate { RunAction(delegate { return core.SaveChance(gamePath.Text, percent.Value); }); };
            uninstall.Click += delegate {
                if (MessageBox.Show(this, "将删除处决 Mod、配置和本 Mod 的备份。\r\n若加载器由本程序安装且未被共用，也会一并清理。\r\n\r\n确认卸载？", "卸载处决特写概率 Mod", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                    RunAction(delegate { return core.Uninstall(gamePath.Text); });
            };
            status.SetBounds(30, 431, 682, 62);
            status.Multiline = true; status.ReadOnly = true; status.BorderStyle = BorderStyle.None;
            status.BackColor = BackColor; status.ForeColor = Color.FromArgb(65, 77, 93);
            status.AccessibleName = "操作状态";
            Controls.Add(status);
            Shown += delegate { DetectGame(); };
        }
        private void AddLabel(string text, int x, int y, int width, int height, float size, bool bold)
        {
            Label label = new Label { Text = text, Font = new Font(Font.FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular) };
            label.SetBounds(x, y, width, height); Controls.Add(label);
        }
        private Button MakeButton(string text, int x, int y, int width, int height)
        { Button b = new Button(); SetupButton(b, text, x, y, width, height); return b; }
        private void SetupButton(Button b, string text, int x, int y, int width, int height)
        {
            b.Text = text; b.SetBounds(x, y, width, height); b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Color.FromArgb(207, 214, 224); b.BackColor = Color.White;
            b.Cursor = Cursors.Hand; Controls.Add(b);
        }
        private void DetectGame()
        {
            try {
                string found = ManagerCore.FindSteamGame();
                if (found == null) { status.Text = "未自动找到游戏，请点击“选择目录”。"; return; }
                gamePath.Text = found; dirty = false; RefreshStatus();
            } catch { status.Text = "未自动找到游戏，请点击“选择目录”。"; }
        }
        private void RefreshStatus()
        {
            if (String.IsNullOrWhiteSpace(gamePath.Text)) return;
            try {
                gamePath.Text = ManagerCore.NormalizeRoot(gamePath.Text);
                ModStatus current = core.Inspect(gamePath.Text);
                if (!dirty) { syncing = true; percent.Value = current.Chance; slider.Value = (int)Decimal.Round(current.Chance); syncing = false; }
                status.Text = current.Description;
            } catch (Exception e) { status.Text = e.Message; }
        }
        private void RunAction(Func<string> action)
        {
            install.Enabled = save.Enabled = uninstall.Enabled = false; Cursor = Cursors.WaitCursor;
            try { status.Text = action(); dirty = false; }
            catch (UnauthorizedAccessException) {
                status.Text = "没有游戏目录的写入权限。请右键本程序，选择“以管理员身份运行”，再重试。";
                MessageBox.Show(this, status.Text, "需要目录写入权限", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception e) { status.Text = e.Message; MessageBox.Show(this, e.Message, "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            finally { install.Enabled = save.Enabled = uninstall.Enabled = true; Cursor = Cursors.Default; }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { Application.Run(new ManagerForm(new ManagerCore())); return 0; }
            catch (Exception e) { MessageBox.Show(e.Message, "处决特写概率 Mod", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
        }
    }
}
