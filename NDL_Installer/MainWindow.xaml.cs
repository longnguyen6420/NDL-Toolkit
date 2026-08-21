using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace NDL_Installer
{
    public partial class MainWindow : Window
    {
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteFile(string name);

        private string _baseDir;

        public MainWindow()
        {
            InitializeComponent();
            _baseDir = ResolveBaseDir();
            ScanNDLTools();
            ScanRevitVersions();
        }

        private string ResolveBaseDir()
        {
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');

                if (Directory.Exists(Path.Combine(appDir, "Core")) ||
                    Directory.Exists(Path.Combine(appDir, "AutoSleeveTool")) ||
                    Directory.Exists(Path.Combine(appDir, "AlignTagTool")))
                {
                    return appDir;
                }

                DirectoryInfo parent = new DirectoryInfo(appDir);
                while (parent != null)
                {
                    if (Directory.Exists(Path.Combine(parent.FullName, "Core")) ||
                        Directory.Exists(Path.Combine(parent.FullName, "AutoSleeveTool")) ||
                        Directory.Exists(Path.Combine(parent.FullName, "AlignTagTool")))
                    {
                        return parent.FullName;
                    }
                    parent = parent.Parent;
                }
            }
            catch { }

            if (Directory.Exists(@"D:\NDL")) return @"D:\NDL";

            string progDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Autodesk", "Revit", "NDL_Toolkit");
            if (Directory.Exists(progDataDir)) return progDataDir;

            string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "NDL_Toolkit");
            if (Directory.Exists(appDataDir)) return appDataDir;

            return AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
        }

        private string GetCoreDllPath(bool isNet8)
        {
            string targetFramework = isNet8 ? "net8.0-windows" : "net48";
            string filter = isNet8 ? "net8" : "net48";

            // 1. Direct Release path for target framework
            string pRelease = Path.Combine(_baseDir, "Core", "bin", "Release", targetFramework, "NDLCore.dll");
            if (File.Exists(pRelease)) return pRelease;

            // 2. Direct Debug path for target framework
            string pDebug = Path.Combine(_baseDir, "Core", "bin", "Debug", targetFramework, "NDLCore.dll");
            if (File.Exists(pDebug)) return pDebug;

            // 3. Search in Core folder for matching framework
            if (Directory.Exists(Path.Combine(_baseDir, "Core")))
            {
                var match = Directory.GetFiles(Path.Combine(_baseDir, "Core"), "NDLCore.dll", SearchOption.AllDirectories)
                    .Where(f => f.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) < 0 &&
                                f.IndexOf("\\ref\\", StringComparison.OrdinalIgnoreCase) < 0 &&
                                f.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderByDescending(f => f.IndexOf("\\Release\\", StringComparison.OrdinalIgnoreCase) >= 0 ? 2 : 1)
                    .FirstOrDefault();

                if (!string.IsNullOrEmpty(match)) return match;
            }

            // 4. Fallback to alternative framework (e.g. net48 if net8 is missing)
            string altFramework = isNet8 ? "net48" : "net8.0-windows";
            string pAlt = Path.Combine(_baseDir, "Core", "bin", "Release", altFramework, "NDLCore.dll");
            if (File.Exists(pAlt)) return pAlt;

            // 5. Search anywhere in baseDir for any NDLCore.dll
            if (Directory.Exists(_baseDir))
            {
                var anyMatch = Directory.GetFiles(_baseDir, "NDLCore.dll", SearchOption.AllDirectories)
                    .Where(f => f.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) < 0 &&
                                f.IndexOf("\\ref\\", StringComparison.OrdinalIgnoreCase) < 0)
                    .OrderByDescending(f => f.IndexOf("\\Release\\", StringComparison.OrdinalIgnoreCase) >= 0 ? 2 : 1)
                    .FirstOrDefault();

                if (!string.IsNullOrEmpty(anyMatch)) return anyMatch;
            }

            return pRelease;
        }

        private void ScanNDLTools()
        {
            if (!Directory.Exists(_baseDir))
            {
                txtToolList.Text = $"⚠️ Không tìm thấy thư mục cài đặt: {_baseDir}";
                return;
            }

            string[] subFolders = Directory.GetDirectories(_baseDir);
            List<string> toolNames = new List<string>();

            foreach (string folder in subFolders)
            {
                string name = Path.GetFileName(folder);
                if (name.Equals("Core", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("NDL_Installer", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith(".", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                toolNames.Add(FormatToolName(name));
            }

            if (toolNames.Count > 0)
            {
                txtToolList.Text = string.Join("  •  ", toolNames);
            }
            else
            {
                txtToolList.Text = $"Không tìm thấy tool con trong: {_baseDir}";
            }
        }

        private string FormatToolName(string raw)
        {
            if (raw.Equals("AutoSleeveTool", StringComparison.OrdinalIgnoreCase)) return "Auto Sleeve (Dầm/Tường)";
            if (raw.Equals("AutoDimDuctTool", StringComparison.OrdinalIgnoreCase)) return "AutoDim Ducts (2D)";
            if (raw.Equals("AlignTagTool", StringComparison.OrdinalIgnoreCase)) return "Align Tags (Căn lề Tag)";
            if (raw.Equals("AlignBranchTool", StringComparison.OrdinalIgnoreCase)) return "Align Branch";
            if (raw.Equals("RevitAutoConnectTool", StringComparison.OrdinalIgnoreCase)) return "Revit AutoConnect";
            if (raw.Equals("PendentSprinklerOptimizer", StringComparison.OrdinalIgnoreCase)) return "Sprinkler Optimizer";
            if (raw.Equals("AlignMepToCeiling", StringComparison.OrdinalIgnoreCase)) return "Align MEP to Ceiling";
            if (raw.Equals("CreateTeeTool", StringComparison.OrdinalIgnoreCase)) return "Create Pipe Tee";
            if (raw.Equals("MakeArmTool", StringComparison.OrdinalIgnoreCase)) return "Make Arm Sprinkler";
            if (raw.Equals("ViewRenameTool", StringComparison.OrdinalIgnoreCase)) return "View Rename";
            if (raw.Equals("OffsetPipeTool", StringComparison.OrdinalIgnoreCase)) return "Offset Pipe";
            if (raw.Equals("PipePlaceholderTool", StringComparison.OrdinalIgnoreCase)) return "Pipe Placeholder";
            if (raw.Equals("PlaceFamilyByLayerTool", StringComparison.OrdinalIgnoreCase)) return "Place Family By Layer";
            return raw;
        }

        private List<string> GetRevitAddinFolders()
        {
            List<string> list = new List<string>();
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "Addins");
            string progData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Autodesk", "Revit", "Addins");

            HashSet<string> detectedYears = new HashSet<string>();

            if (Directory.Exists(appData))
            {
                foreach (string dir in Directory.GetDirectories(appData))
                {
                    string leaf = Path.GetFileName(dir);
                    if (int.TryParse(leaf, out int yr) && yr >= 2018 && yr <= 2030)
                    {
                        detectedYears.Add(leaf);
                    }
                }
            }

            if (Directory.Exists(progData))
            {
                foreach (string dir in Directory.GetDirectories(progData))
                {
                    string leaf = Path.GetFileName(dir);
                    if (int.TryParse(leaf, out int yr) && yr >= 2018 && yr <= 2030)
                    {
                        detectedYears.Add(leaf);
                    }
                }
            }

            // Ensure all common Revit versions (2020 - 2026) are covered
            for (int y = 2020; y <= 2026; y++)
            {
                detectedYears.Add(y.ToString());
            }

            // Always write to user APPDATA (never throws Access Denied)
            foreach (string year in detectedYears.OrderBy(y => y))
            {
                string userFolder = Path.Combine(appData, year);
                if (!Directory.Exists(userFolder))
                {
                    try { Directory.CreateDirectory(userFolder); } catch { }
                }
                if (Directory.Exists(userFolder) && !list.Contains(userFolder))
                {
                    list.Add(userFolder);
                }
            }

            return list;
        }

        private void ScanRevitVersions()
        {
            var folders = GetRevitAddinFolders();
            var versions = folders.Select(f => Path.GetFileName(f)).Distinct().OrderBy(v => v).ToList();

            if (versions.Count > 0)
            {
                txtRevitVersions.Text = "Hỗ trợ các phiên bản Revit: " + string.Join(", ", versions);
            }
            else
            {
                txtRevitVersions.Text = "Chưa phát hiện phiên bản Revit nào trên máy.";
            }
        }

        private void Log(string msg)
        {
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            txtLog.ScrollToEnd();
        }

        private void UnblockDirectoryFiles(string path)
        {
            try
            {
                if (!Directory.Exists(path)) return;
                var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    try
                    {
                        DeleteFile(file + ":Zone.Identifier");
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            txtLog.Clear();
            Log("Bắt đầu cài đặt bộ công cụ NDL Addin...");
            Log($"Thư mục nguồn NDL: {_baseDir}");

            // Unblock all DLL files to prevent Windows Defender / SmartScreen blocking
            UnblockDirectoryFiles(_baseDir);

            var targetFolders = GetRevitAddinFolders();
            if (targetFolders.Count == 0)
            {
                Log("⚠️ Lỗi: Không tìm thấy thư mục Revit Addins nào trên hệ thống.");
                return;
            }

            int count = 0;
            foreach (string folder in targetFolders)
            {
                string version = Path.GetFileName(folder);
                int.TryParse(version, out int year);

                bool isNet8 = year >= 2025;
                string targetDll = GetCoreDllPath(isNet8);

                if (!File.Exists(targetDll))
                {
                    Log($"⚠️ Cảnh báo: Chưa tìm thấy file NDLCore.dll tại '{targetDll}'");
                }

                string manifest = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<RevitAddIns>
  <AddIn Type=""Application"">
    <Name>NDL Tools Loader</Name>
    <Assembly>{targetDll}</Assembly>
    <FullClassName>NDL.NDLApp</FullClassName>
    <ClientId>11223344-5566-7788-9900-AABBCCDDEEFF</ClientId>
    <VendorId>NDL</VendorId>
    <VendorDescription>NDL Revit Addin Suite</VendorDescription>
  </AddIn>
</RevitAddIns>";

                string manifestPath = Path.Combine(folder, "NDL.addin");
                try
                {
                    File.WriteAllText(manifestPath, manifest, Encoding.UTF8);
                    Log($"✅ Đã đăng ký NDL cho Revit {version}: {manifestPath}");
                    count++;
                }
                catch (Exception ex)
                {
                    Log($"❌ Lỗi đăng ký Revit {version}: {ex.Message}");
                }
            }

            Log("================================================");
            Log($"🎉 THÀNH CÔNG! Đã cài đặt NDL Addin cho {count} thư mục Revit.");
            Log("Vui lòng mở Revit để trải nghiệm Tab 'NDL' trên thanh Ribbon!");
        }

        private void BtnUninstall_Click(object sender, RoutedEventArgs e)
        {
            txtLog.Clear();
            Log("Bắt đầu gỡ bỏ NDL Addin...");

            var targetFolders = GetRevitAddinFolders();
            int removed = 0;

            foreach (string folder in targetFolders)
            {
                string manifestPath = Path.Combine(folder, "NDL.addin");
                if (File.Exists(manifestPath))
                {
                    try
                    {
                        File.Delete(manifestPath);
                        Log($"🗑️ Đã xóa: {manifestPath}");
                        removed++;
                    }
                    catch (Exception ex)
                    {
                        Log($"❌ Không thể xóa {manifestPath}: {ex.Message}");
                    }
                }
            }

            Log("================================================");
            Log($"THÀNH CÔNG! Đã gỡ bỏ NDL Addin khỏi {removed} thư mục Revit.");
        }
    }
}
