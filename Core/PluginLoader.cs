using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

namespace NDL
{
    public static class PluginLoader
    {
        public static void LoadAllPlugins(UIControlledApplication app)
        {
            string baseDir = NDLApp.BaseDir;
            if (!Directory.Exists(baseDir)) return;

            bool isNet8OrHigher = false;
            try
            {
                if (int.TryParse(app.ControlledApplication.VersionNumber, out int versionYear))
                {
                    isNet8OrHigher = versionYear >= 2025;
                }
                else
                {
                    isNet8OrHigher = System.Environment.Version.Major >= 8;
                }
            }
            catch
            {
                isNet8OrHigher = System.Environment.Version.Major >= 8;
            }

            string targetFrameworkFilter = isNet8OrHigher ? "\\net8" : "\\net48";

            // AssemblyResolve to resolve plugin dependencies
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                try
                {
                    string assemblyName = new AssemblyName(args.Name).Name + ".dll";

                    var matches = Directory.GetFiles(baseDir, assemblyName, SearchOption.AllDirectories)
                        .Where(f => f.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) < 0 &&
                                    f.IndexOf("\\.vs\\", StringComparison.OrdinalIgnoreCase) < 0 &&
                                    f.IndexOf("\\ref\\", StringComparison.OrdinalIgnoreCase) < 0 &&
                                    f.IndexOf("\\refint\\", StringComparison.OrdinalIgnoreCase) < 0);

                    string pluginMatch = matches.FirstOrDefault(f => f.IndexOf(targetFrameworkFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                        ?? matches.FirstOrDefault();

                    if (!string.IsNullOrEmpty(pluginMatch) && File.Exists(pluginMatch))
                    {
                        return Assembly.LoadFrom(pluginMatch);
                    }

                    string revitDir = Path.GetDirectoryName(typeof(Autodesk.Revit.UI.Result).Assembly.Location);
                    string revitMatch = Path.Combine(revitDir, assemblyName);
                    if (File.Exists(revitMatch))
                    {
                        return Assembly.LoadFrom(revitMatch);
                    }
                }
                catch { }
                return null;
            };

            string[] subFolders = Directory.GetDirectories(baseDir);
            List<string> errorLogs = new List<string>();

            foreach (string folderPath in subFolders)
            {
                string folderName = Path.GetFileName(folderPath);
                if (folderName.Equals("Core", StringComparison.OrdinalIgnoreCase) ||
                    folderName.Equals("NDL_Installer", StringComparison.OrdinalIgnoreCase) ||
                    folderName.StartsWith(".", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var candidateDlls = Directory.GetFiles(folderPath, "*.dll", SearchOption.AllDirectories)
                    .Where(f => (f.IndexOf("\\bin\\Release\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 f.IndexOf("\\bin\\Debug\\", StringComparison.OrdinalIgnoreCase) >= 0) &&
                                f.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) < 0 &&
                                f.IndexOf("\\ref\\", StringComparison.OrdinalIgnoreCase) < 0 &&
                                f.IndexOf("\\refint\\", StringComparison.OrdinalIgnoreCase) < 0)
                    .ToList();

                // Select target DLL matching current Revit CLR framework
                string targetDll = candidateDlls
                    .Where(f => f.IndexOf(targetFrameworkFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Where(f => !f.EndsWith("BatchRenameViewsTool.dll", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .FirstOrDefault();

                if (string.IsNullOrEmpty(targetDll))
                {
                    targetDll = candidateDlls
                        .Where(f => !f.EndsWith("BatchRenameViewsTool.dll", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(f => File.GetLastWriteTime(f))
                        .FirstOrDefault();
                }

                if (string.IsNullOrEmpty(targetDll) || !File.Exists(targetDll))
                {
                    continue;
                }

                try
                {
                    LoadPluginDll(app, targetDll, folderName);
                }
                catch (Exception ex)
                {
                    errorLogs.Add($"Plugin '{folderName}': {ex.Message}");
                }
            }

            if (errorLogs.Count > 0)
            {
                TaskDialog.Show("NDL Loader Warning", "Lỗi nạp một số plugin:\n\n" + string.Join("\n", errorLogs));
            }
        }

        private static void LoadPluginDll(UIControlledApplication app, string dllPath, string folderName)
        {
            Assembly asm = Assembly.LoadFrom(dllPath);
            if (asm == null) return;

            string panelName = FormatPanelName(folderName);

            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray();
            }

            foreach (Type type in types)
            {
                if (type == null || !type.IsClass || type.IsAbstract || !type.IsPublic)
                    continue;

                if (typeof(IExternalCommand).IsAssignableFrom(type))
                {
                    RegisterCommandButton(app, panelName, dllPath, type, folderName);
                }
            }
        }

        private static void RegisterCommandButton(UIControlledApplication app, string panelName, string dllPath, Type commandType, string folderName)
        {
            RibbonPanel panel = NDLApp.GetOrCreatePanel(app, NDLApp.TabName, panelName);

            string buttonId = $"btn_NDL_{dllPath.GetHashCode()}_{commandType.Name}";
            string buttonTitle = FormatButtonTitle(commandType.Name);

            foreach (RibbonItem item in panel.GetItems())
            {
                if (item.Name.Equals(buttonId, StringComparison.OrdinalIgnoreCase) ||
                    item.ItemText.Equals(buttonTitle, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            PushButtonData btnData = new PushButtonData(
                buttonId,
                buttonTitle,
                dllPath,
                commandType.FullName
            )
            {
                ToolTip = $"Công cụ NDL '{buttonTitle}'.\nAssembly: {dllPath}"
            };

            // 1) Try Embedded Resource in Assembly
            try
            {
                string resName = commandType.Assembly.GetManifestResourceNames().FirstOrDefault(r =>
                    r.EndsWith("icon32.png", StringComparison.OrdinalIgnoreCase) ||
                    r.EndsWith("icon.png", StringComparison.OrdinalIgnoreCase) ||
                    r.EndsWith("rotatevertical.png", StringComparison.OrdinalIgnoreCase) ||
                    r.EndsWith("offsetpipe.png", StringComparison.OrdinalIgnoreCase) ||
                    r.EndsWith("pipeplaceholder.png", StringComparison.OrdinalIgnoreCase) ||
                    r.EndsWith("placefamilybylayer.png", StringComparison.OrdinalIgnoreCase) ||
                    r.EndsWith("aligntag.png", StringComparison.OrdinalIgnoreCase) ||
                    r.EndsWith("autodimduct.png", StringComparison.OrdinalIgnoreCase) ||
                    r.EndsWith("autodim.png", StringComparison.OrdinalIgnoreCase) ||
                    r.EndsWith("autosleeve.png", StringComparison.OrdinalIgnoreCase) ||
                    r.EndsWith("alignbranch.png", StringComparison.OrdinalIgnoreCase) ||
                    r.EndsWith("autoconnect.png", StringComparison.OrdinalIgnoreCase) ||
                    r.EndsWith("viewrename.png", StringComparison.OrdinalIgnoreCase) ||
                    r.EndsWith("makearm.png", StringComparison.OrdinalIgnoreCase) ||
                    r.EndsWith("sprinkler.png", StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrEmpty(resName))
                {
                    using (Stream s = commandType.Assembly.GetManifestResourceStream(resName))
                    {
                        BitmapImage bmp = LoadBitmapFromStream(s);
                        if (bmp != null)
                        {
                            btnData.LargeImage = bmp;
                            btnData.Image = bmp;
                        }
                    }
                }
            }
            catch { }

            // 2) Fallback to File Paths if LargeImage is still null
            if (btnData.LargeImage == null)
            {
                string pluginDir = Path.GetDirectoryName(dllPath);
                string topPluginDir = Path.Combine(NDLApp.BaseDir, folderName);

                string[] candidateIcons = new string[] {
                    Path.Combine(topPluginDir, "icon32.png"),
                    Path.Combine(topPluginDir, "icon.png"),
                    Path.Combine(topPluginDir, "rotatevertical.png"),
                    Path.Combine(topPluginDir, "offsetpipe.png"),
                    Path.Combine(topPluginDir, "pipeplaceholder.png"),
                    Path.Combine(topPluginDir, "placefamilybylayer.png"),
                    Path.Combine(topPluginDir, "aligntag.png"),
                    Path.Combine(topPluginDir, "autodimduct.png"),
                    Path.Combine(topPluginDir, "autodim.png"),
                    Path.Combine(topPluginDir, "autosleeve.png"),
                    Path.Combine(topPluginDir, "alignbranch.png"),
                    Path.Combine(topPluginDir, "autoconnect.png"),
                    Path.Combine(topPluginDir, "viewrename.png"),
                    Path.Combine(topPluginDir, "sprinkler.png"),
                    Path.Combine(pluginDir, "icon32.png"),
                    Path.Combine(pluginDir, "icon.png"),
                    Path.Combine(pluginDir, "rotatevertical.png"),
                    Path.Combine(pluginDir, "offsetpipe.png"),
                    Path.Combine(pluginDir, "pipeplaceholder.png"),
                    Path.Combine(pluginDir, "placefamilybylayer.png"),
                    Path.Combine(pluginDir, "aligntag.png"),
                    Path.Combine(pluginDir, "autodimduct.png"),
                    Path.Combine(pluginDir, "autodim.png"),
                    Path.Combine(pluginDir, "autosleeve.png"),
                    Path.Combine(pluginDir, "alignbranch.png"),
                    Path.Combine(pluginDir, "autoconnect.png"),
                    Path.Combine(pluginDir, "viewrename.png"),
                    Path.Combine(pluginDir, "sprinkler.png")
                };

                string foundIconPath = candidateIcons.FirstOrDefault(File.Exists);
                if (!string.IsNullOrEmpty(foundIconPath))
                {
                    BitmapImage iconImg = LoadBitmapImage(foundIconPath);
                    if (iconImg != null)
                    {
                        btnData.LargeImage = iconImg;
                        btnData.Image = iconImg;
                    }
                }
            }

            panel.AddItem(btnData);
        }

        private static BitmapImage LoadBitmapImage(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath)) return null;

            try
            {
                byte[] buffer = File.ReadAllBytes(imagePath);
                using (MemoryStream ms = new MemoryStream(buffer))
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = ms;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
            }
            catch
            {
                return null;
            }
        }

        private static BitmapImage LoadBitmapFromStream(Stream stream)
        {
            if (stream == null) return null;
            try
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    ms.Position = 0;

                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = ms;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string FormatPanelName(string folderName)
        {
            if (folderName.Equals("RotateVerticalTool", StringComparison.OrdinalIgnoreCase)) return "AUTO TOOL";
            if (folderName.Equals("OffsetPipeTool", StringComparison.OrdinalIgnoreCase)) return "MEP / PIPE";
            if (folderName.Equals("PipePlaceholderTool", StringComparison.OrdinalIgnoreCase)) return "MEP / PIPE";
            if (folderName.Equals("PlaceFamilyByLayerTool", StringComparison.OrdinalIgnoreCase)) return "CAD / LINK";
            if (folderName.Equals("AlignTagTool", StringComparison.OrdinalIgnoreCase)) return "TAG";
            if (folderName.Equals("AlignBranchTool", StringComparison.OrdinalIgnoreCase)) return "AUTO TOOL";
            if (folderName.Equals("RevitAutoConnectTool", StringComparison.OrdinalIgnoreCase)) return "AUTO TOOL";
            if (folderName.Equals("PendentSprinklerOptimizer", StringComparison.OrdinalIgnoreCase)) return "FIRE";
            if (folderName.Equals("AutoSleeveTool", StringComparison.OrdinalIgnoreCase)) return "SLEEVE";
            if (folderName.Equals("AutoDimDuctTool", StringComparison.OrdinalIgnoreCase)) return "SLEEVE";
            if (folderName.Equals("AlignMepToCeiling", StringComparison.OrdinalIgnoreCase)) return "AUTO TOOL";
            if (folderName.Equals("CreateTeeTool", StringComparison.OrdinalIgnoreCase)) return "MEP / PIPE";
            if (folderName.Equals("MakeArmTool", StringComparison.OrdinalIgnoreCase)) return "FIRE";
            if (folderName.Equals("ViewRenameTool", StringComparison.OrdinalIgnoreCase) || folderName.Equals("BatchRenameViewsTool", StringComparison.OrdinalIgnoreCase)) return "VIEWS";

            return FormatName(folderName.Replace("Tool", "").Replace("Plugin", ""));
        }

        private static string FormatButtonTitle(string className)
        {
            string clean = className.Replace("Command", "").Replace("Cmd", "");
            if (clean.Equals("MakeArm", StringComparison.OrdinalIgnoreCase))
                return "Make\nArm";
            if (clean.Equals("ViewRename", StringComparison.OrdinalIgnoreCase) || clean.Equals("BatchRenameViews", StringComparison.OrdinalIgnoreCase))
                return "View\nRename";
            if (clean.Equals("RotateVertical", StringComparison.OrdinalIgnoreCase))
                return "Rotate\nVertical";
            if (clean.Equals("OffsetPipe", StringComparison.OrdinalIgnoreCase))
                return "Offset\nPipes";
            if (clean.Equals("PipePlaceholder", StringComparison.OrdinalIgnoreCase))
                return "Pipe\nPlaceholder";
            if (clean.Equals("PlaceFamilyByLayer", StringComparison.OrdinalIgnoreCase))
                return "Place Family\nBy Layer";
            if (clean.Equals("AlignTag", StringComparison.OrdinalIgnoreCase))
                return "Align\nTags";
            if (clean.Equals("InteractiveConnectTool", StringComparison.OrdinalIgnoreCase) || clean.Equals("InteractiveConnect", StringComparison.OrdinalIgnoreCase))
                return "Interactive\nConnect";
            if (clean.Equals("AlignBranch", StringComparison.OrdinalIgnoreCase))
                return "Align Branch";
            if (clean.Equals("OptimizeSprinklers", StringComparison.OrdinalIgnoreCase))
                return "Optimize\nSprinklers";
            if (clean.Equals("AutoSleeve", StringComparison.OrdinalIgnoreCase))
                return "Auto\nSleeve";
            if (clean.Equals("AutoDimDuct", StringComparison.OrdinalIgnoreCase))
                return "AutoDim\nDucts";
            if (clean.Equals("AlignMEPToCeiling", StringComparison.OrdinalIgnoreCase))
                return "Align MEP\nto Ceiling";
            if (clean.Equals("CreateTee", StringComparison.OrdinalIgnoreCase))
                return "Create\nPipe Tee";

            return FormatName(clean);
        }

        private static string FormatName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return rawName;

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < rawName.Length; i++)
            {
                char c = rawName[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(rawName[i - 1]))
                {
                    sb.Append(' ');
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
