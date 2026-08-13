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
                if (Directory.Exists(@"D:\NDL")) return @"D:\NDL";
                try
                {
                    string asmDir = Path.GetDirectoryName(AssemblyPath);
                    DirectoryInfo current = new DirectoryInfo(asmDir);
                    while (current != null)
                    {
                        if (current.Name.Equals("NDL", StringComparison.OrdinalIgnoreCase))
                        {
                            return current.FullName;
                        }
                        current = current.Parent;
                    }
                }
                catch { }
                return @"D:\NDL";
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
