using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace AutoSleeveTool
{
    public class AutoSleeveApp : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                string tabName = "Auto Sleeve";
                try
                {
                    application.CreateRibbonTab(tabName);
                }
                catch { }

                RibbonPanel panel = application.CreateRibbonPanel(tabName, "Sleeve Tools");
                string assemblyPath = Assembly.GetExecutingAssembly().Location;

                PushButtonData btnData = new PushButtonData(
                    "AutoSleeveCmd",
                    "Auto Sleeve\nPlacement",
                    assemblyPath,
                    "AutoSleeveTool.Commands.AutoSleeveCommand")
                {
                    ToolTip = "Tự động đặt Sleeve (DuctType 'sleeve') cho Duct & Pipe xuyên qua các đối tượng trong Revit Link."
                };

                // 1) Try Embedded Resource
                try
                {
                    Assembly currentAsm = Assembly.GetExecutingAssembly();
                    string resName = currentAsm.GetManifestResourceNames().FirstOrDefault(r =>
                        r.EndsWith("icon32.png", StringComparison.OrdinalIgnoreCase) ||
                        r.EndsWith("icon.png", StringComparison.OrdinalIgnoreCase) ||
                        r.EndsWith("autosleeve.png", StringComparison.OrdinalIgnoreCase));

                    if (!string.IsNullOrEmpty(resName))
                    {
                        using (Stream s = currentAsm.GetManifestResourceStream(resName))
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

                // 2) Fallback to file system
                if (btnData.LargeImage == null)
                {
                    string pluginDir = Path.GetDirectoryName(assemblyPath);
                    string topPluginDir = Path.GetFullPath(Path.Combine(pluginDir, @"..\..\.."));

                    string[] candidates = new string[]
                    {
                        Path.Combine(pluginDir, "icon32.png"),
                        Path.Combine(pluginDir, "icon.png"),
                        Path.Combine(pluginDir, "autosleeve.png"),
                        Path.Combine(topPluginDir, "AutoSleeveTool", "icon32.png"),
                        Path.Combine(topPluginDir, "AutoSleeveTool", "icon.png"),
                        @"D:\NDL\AutoSleeveTool\icon32.png",
                        @"D:\NDL\AutoSleeveTool\icon.png"
                    };

                    foreach (string iconPath in candidates)
                    {
                        if (File.Exists(iconPath))
                        {
                            try
                            {
                                byte[] buffer = File.ReadAllBytes(iconPath);
                                using (MemoryStream ms = new MemoryStream(buffer))
                                {
                                    BitmapImage bmp = new BitmapImage();
                                    bmp.BeginInit();
                                    bmp.StreamSource = ms;
                                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                                    bmp.EndInit();
                                    bmp.Freeze();
                                    btnData.LargeImage = bmp;
                                    btnData.Image = bmp;
                                    break;
                                }
                            }
                            catch { }
                        }
                    }
                }

                panel.AddItem(btnData);

                return Result.Succeeded;
            }
            catch
            {
                return Result.Failed;
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

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
