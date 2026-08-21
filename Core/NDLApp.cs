using System;
using System.IO;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

namespace NDL
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class NDLApp : IExternalApplication
    {
        public static string TabName = "NDL";
        public static UIControlledApplication UIApp;
        public static string AssemblyPath => Assembly.GetExecutingAssembly().Location;
        public static string CoreDir => Path.GetDirectoryName(AssemblyPath);

        public static string BaseDir
        {
            get
            {
                try
                {
                    string asmPath = AssemblyPath;
                    if (!string.IsNullOrEmpty(asmPath) && File.Exists(asmPath))
                    {
                        string asmDir = Path.GetDirectoryName(asmPath);
                        DirectoryInfo current = new DirectoryInfo(asmDir);
                        while (current != null)
                        {
                            if (Directory.Exists(Path.Combine(current.FullName, "Core")) ||
                                Directory.Exists(Path.Combine(current.FullName, "AutoSleeveTool")) ||
                                Directory.Exists(Path.Combine(current.FullName, "AlignTagTool")) ||
                                Directory.Exists(Path.Combine(current.FullName, "MakeArmTool")) ||
                                Directory.Exists(Path.Combine(current.FullName, "CreateTeeTool")))
                            {
                                return current.FullName;
                            }
                            current = current.Parent;
                        }
                    }
                }
                catch { }

                // Fallbacks
                if (Directory.Exists(@"D:\NDL")) return @"D:\NDL";

                string progDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Autodesk", "Revit", "NDL_Toolkit");
                if (Directory.Exists(progDataDir)) return progDataDir;

                string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "NDL_Toolkit");
                if (Directory.Exists(appDataDir)) return appDataDir;

                return Path.GetDirectoryName(AssemblyPath);
            }
        }

        public Result OnStartup(UIControlledApplication application)
        {
            UIApp = application;

            // Create Ribbon Tab 'NDL'
            try
            {
                application.CreateRibbonTab(TabName);
            }
            catch { }

            // Dynamically load all plugins
            try
            {
                PluginLoader.LoadAllPlugins(application);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("NDL Loader Error", "Lỗi nạp Plugin NDL: " + ex.Message);
            }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        public static RibbonPanel GetOrCreatePanel(UIControlledApplication app, string tabName, string panelName)
        {
            foreach (RibbonPanel p in app.GetRibbonPanels(tabName))
            {
                if (p.Name.Equals(panelName, StringComparison.OrdinalIgnoreCase))
                    return p;
            }
            return app.CreateRibbonPanel(tabName, panelName);
        }
    }
}
