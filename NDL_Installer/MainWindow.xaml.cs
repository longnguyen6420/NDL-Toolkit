using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace NDL_Installer
{
    public partial class MainWindow : Window
    {
        private const string BaseDir = @"D:\NDL";
        private const string Net48CoreDll = @"D:\NDL\Core\bin\Release\net48\NDLCore.dll";
        private const string Net80CoreDll = @"D:\NDL\Core\bin\Release\net8.0-windows\NDLCore.dll";

        public MainWindow()
        {
            InitializeComponent();
            ScanNDLTools();
            ScanRevitVersions();
        }

        private void ScanNDLTools()
        {
            if (!Directory.Exists(BaseDir))
            {
                txtToolList.Text = "⚠️ Không tìm thấy thư mục D:\\NDL!";
                return;
            }

            string[] subFolders = Directory.GetDirectories(BaseDir);
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
                txtToolList.Text = "Không có tool nào trong D:\\NDL";
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
            return raw;
        }

        private List<string> GetRevitAddinFolders()
        {
            List<string> list = new List<string>();
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "Addins");
            string progData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Autodesk", "Revit", "Addins");

            if (Directory.Exists(appData))
            {
                foreach (string dir in Directory.GetDirectories(appData))
                {
                    if (!list.Contains(dir)) list.Add(dir);
                }
            }

            if (Directory.Exists(progData))
            {
                foreach (string dir in Directory.GetDirectories(progData))
                {
                    if (!list.Contains(dir)) list.Add(dir);
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
                txtRevitVersions.Text = "Phát hiện " + versions.Count + " phiên bản Revit: " + string.Join(", ", versions);
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

        private void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            txtLog.Clear();
            Log("Bắt đầu cài đặt bộ công cụ NDL Addin...");

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

                string targetDll = Net48CoreDll;
                if (year >= 2025 && File.Exists(Net80CoreDll))
                {
                    targetDll = Net80CoreDll;
                }

                string manifest = $@"<?xml=""1.0"" encoding=""utf-8""?>
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
