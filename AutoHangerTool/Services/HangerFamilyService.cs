using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace NDL.AutoHangerTool.Services
{
    public static class HangerFamilyService
    {
        public const string FamilyName = "NDL_Hanger_Insert";

        public static FamilySymbol GetOrLoadHangerFamilySymbol(Document doc)
        {
            // 1. Check if already loaded in project
            FamilySymbol symbol = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => s.FamilyName.Equals(FamilyName, StringComparison.OrdinalIgnoreCase));

            if (symbol != null)
            {
                if (!symbol.IsActive)
                {
                    using (Transaction t = new Transaction(doc, "Activate Hanger Symbol"))
                    {
                        t.Start();
                        symbol.Activate();
                        t.Commit();
                    }
                }
                return symbol;
            }

            // 2. Try loading from file
            string baseDir = Path.GetDirectoryName(typeof(HangerFamilyService).Assembly.Location);
            string rfaPath = Path.Combine(baseDir, "Families", $"{FamilyName}.rfa");
            if (!File.Exists(rfaPath))
            {
                // Look in project ancestor directories
                DirectoryInfo current = new DirectoryInfo(baseDir);
                while (current != null)
                {
                    string candidate = Path.Combine(current.FullName, "AutoHangerTool", "Families", $"{FamilyName}.rfa");
                    if (File.Exists(candidate))
                    {
                        rfaPath = candidate;
                        break;
                    }
                    candidate = Path.Combine(current.FullName, "Families", $"{FamilyName}.rfa");
                    if (File.Exists(candidate))
                    {
                        rfaPath = candidate;
                        break;
                    }
                    current = current.Parent;
                }
            }

            if (File.Exists(rfaPath))
            {
                using (Transaction t = new Transaction(doc, "Load Hanger Family"))
                {
                    t.Start();
                    if (doc.LoadFamily(rfaPath, out Family family))
                    {
                        symbol = family.GetFamilySymbolIds()
                            .Select(id => doc.GetElement(id) as FamilySymbol)
                            .FirstOrDefault();
                        if (symbol != null && !symbol.IsActive)
                        {
                            symbol.Activate();
                        }
                    }
                    t.Commit();
                }
            }

            // Fallback: If still not loaded, find any Generic Model FamilySymbol
            if (symbol == null)
            {
                symbol = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_GenericModel)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault();
            }

            return symbol;
        }
    }
}
