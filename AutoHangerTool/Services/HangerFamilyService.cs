using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.ApplicationServices;

namespace NDL.AutoHangerTool.Services
{
    public static class HangerFamilyService
    {
        public const string FamilyName = "NDL_Hanger_Insert";

        public static string GetFamiliesFolderPath()
        {
            string baseDir = Path.GetDirectoryName(typeof(HangerFamilyService).Assembly.Location);
            string folder = Path.Combine(baseDir, "Families");
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            return folder;
        }

        public static string GetFamilyRfaPath()
        {
            return Path.Combine(GetFamiliesFolderPath(), $"{FamilyName}.rfa");
        }

        private static string FindTemplatePath(string templateName)
        {
            string[] searchRoots = new[]
            {
                @"C:\ProgramData\Autodesk\RVT 2025\Family Templates\English",
                @"C:\ProgramData\Autodesk\RVT 2024\Family Templates\English",
                @"C:\ProgramData\Autodesk\RVT 2023\Family Templates\English",
                @"C:\ProgramData\Autodesk\RVT 2022\Family Templates\English",
                @"C:\ProgramData\Autodesk\RVT 2021\Family Templates\English",
                @"C:\ProgramData\Autodesk\RVT 2020\Family Templates\English",
                @"C:\ProgramData\Autodesk\RVT 2025\Family Templates\English-Imperial",
                @"C:\ProgramData\Autodesk\RVT 2024\Family Templates\English-Imperial"
            };

            foreach (var root in searchRoots)
            {
                if (Directory.Exists(root))
                {
                    var files = Directory.GetFiles(root, templateName, SearchOption.AllDirectories);
                    if (files.Length > 0) return files[0];
                }
            }

            return null;
        }

        public static List<FamilySymbol> GetAvailableHangerSymbols(Document doc)
        {
            var list = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(s => s.Category != null && 
                           (s.Category.Id.IntegerValue == (int)BuiltInCategory.OST_GenericModel ||
                            s.Category.Id.IntegerValue == (int)BuiltInCategory.OST_PipeAccessory ||
                            s.Category.Id.IntegerValue == (int)BuiltInCategory.OST_MechanicalEquipment))
                .OrderBy(s => s.FamilyName)
                .ThenBy(s => s.Name)
                .ToList();

            return list;
        }

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

            // 2. Build RFA via Revit API
            string rfaPath = GetFamilyRfaPath();
            CreateStandardHangerFamily(doc.Application);

            if (File.Exists(rfaPath))
            {
                try
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
                catch { }
            }

            // 3. Fallback: Any Generic Model FamilySymbol
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

        public static bool CreateStandardHangerFamily(Application app)
        {
            string rfaPath = GetFamilyRfaPath();
            string templatePath = FindTemplatePath("Metric Generic Model.rft");
            if (string.IsNullOrEmpty(templatePath))
            {
                templatePath = FindTemplatePath("Generic Model.rft");
            }

            if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
            {
                return false;
            }

            try
            {
                Document famDoc = app.NewFamilyDocument(templatePath);
                if (famDoc == null) return false;

                using (Transaction t = new Transaction(famDoc, "Build Hanger Family"))
                {
                    t.Start();

                    // Create 3D Cylindrical Rod (Extrusion)
                    double radiusFt = 0.0164; // ~5mm radius
                    double heightFt = 3.28084; // 1000mm default rod height

                    Plane groundPlane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero);
                    SketchPlane sketchPlane = SketchPlane.Create(famDoc, groundPlane);

                    XYZ p1 = new XYZ(-radiusFt, 0, 0);
                    XYZ p2 = new XYZ(radiusFt, 0, 0);
                    XYZ mid1 = new XYZ(0, radiusFt, 0);
                    XYZ mid2 = new XYZ(0, -radiusFt, 0);

                    Arc arc1 = Arc.Create(p1, p2, mid1);
                    Arc arc2 = Arc.Create(p2, p1, mid2);

                    CurveArrArray curveArrArray = new CurveArrArray();
                    CurveArray curveArray = new CurveArray();
                    curveArray.Append(arc1);
                    curveArray.Append(arc2);
                    curveArrArray.Append(curveArray);

                    Extrusion rod = famDoc.FamilyCreate.NewExtrusion(true, curveArrArray, sketchPlane, heightFt);

                    // Set 2D/3D Visibility overrides: Hide in Plan views so it doesn't clutter 2D
                    FamilyElementVisibility vis = new FamilyElementVisibility(FamilyElementVisibilityType.Model)
                    {
                        IsShownInPlanRCPCut = false,
                        IsShownInTopBottom = false
                    };
                    rod.SetVisibility(vis);

                    // Add 2D Cross lines
                    try
                    {
                        XYZ crossA1 = new XYZ(-0.15, 0, 0);
                        XYZ crossA2 = new XYZ(0.15, 0, 0);
                        XYZ crossB1 = new XYZ(0, -0.15, 0);
                        XYZ crossB2 = new XYZ(0, 0.15, 0);
                        famDoc.FamilyCreate.NewModelCurve(Line.CreateBound(crossA1, crossA2), sketchPlane);
                        famDoc.FamilyCreate.NewModelCurve(Line.CreateBound(crossB1, crossB2), sketchPlane);
                    }
                    catch { }

                    t.Commit();
                }

                SaveAsOptions saveOptions = new SaveAsOptions { OverwriteExistingFile = true };
                famDoc.SaveAs(rfaPath, saveOptions);
                famDoc.Close(false);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
