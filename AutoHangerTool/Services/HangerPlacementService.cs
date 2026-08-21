using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Structure;

namespace NDL.AutoHangerTool.Services
{
    public class HangerSettings
    {
        public double SpacingMm { get; set; } = 2000.0;
        public double FittingOffsetMm { get; set; } = 300.0;
        public bool PlaceNearFittings { get; set; } = true;
        public string RodSize { get; set; } = "M10";
        public double DefaultSlabHeightMm { get; set; } = 3500.0;
    }

    public static class HangerPlacementService
    {
        public static int PlaceHangersOnPipes(Document doc, List<Element> pipes, HangerSettings settings, FamilySymbol hangerSymbol)
        {
            if (doc == null || pipes == null || pipes.Count == 0 || hangerSymbol == null)
                return 0;

            int count = 0;
            double spacingFt = settings.SpacingMm / 304.8;
            double fittingOffsetFt = settings.FittingOffsetMm / 304.8;

            // Prepare 3D View for Ray Projection (ReferenceIntersector)
            View3D view3D = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate);

            ReferenceIntersector intersector = null;
            if (view3D != null)
            {
                ElementMulticategoryFilter filter = new ElementMulticategoryFilter(new List<BuiltInCategory>
                {
                    BuiltInCategory.OST_Floors,
                    BuiltInCategory.OST_StructuralFraming,
                    BuiltInCategory.OST_Ceilings,
                    BuiltInCategory.OST_Roofs
                });
                intersector = new ReferenceIntersector(filter, FindReferenceTarget.Face, view3D)
                {
                    FindReferencesInRevitLinks = true
                };
            }

            using (Transaction t = new Transaction(doc, "NDL Auto Hanger & Insert Placement"))
            {
                t.Start();

                foreach (Element elem in pipes)
                {
                    LocationCurve lc = elem.Location as LocationCurve;
                    if (lc == null || lc.Curve == null) continue;

                    Curve curve = lc.Curve;
                    double length = curve.Length;
                    if (length < 1.0) continue; // Skip very short segments (< 300mm)

                    List<double> paramList = new List<double>();

                    // 1. Add points near fittings if enabled
                    if (settings.PlaceNearFittings && length > fittingOffsetFt * 2)
                    {
                        paramList.Add(fittingOffsetFt / length);
                        paramList.Add(1.0 - (fittingOffsetFt / length));
                    }

                    // 2. Add intermediate points by spacing
                    int numDivisions = (int)Math.Floor(length / spacingFt);
                    if (numDivisions > 0)
                    {
                        double step = 1.0 / (numDivisions + 1);
                        for (int i = 1; i <= numDivisions; i++)
                        {
                            double p = i * step;
                            if (!paramList.Any(existing => Math.Abs(existing - p) < 0.15))
                            {
                                paramList.Add(p);
                            }
                        }
                    }

                    // Ensure at least 1 midpoint if no points were added
                    if (paramList.Count == 0)
                    {
                        paramList.Add(0.5);
                    }

                    Level level = doc.GetElement(elem.LevelId) as Level;
                    double levelElev = level != null ? level.Elevation : 0.0;

                    foreach (double param in paramList.OrderBy(p => p))
                    {
                        XYZ pt = curve.Evaluate(param, true);

                        // Find slab/beam elevation above pipe
                        double topZ = levelElev + (settings.DefaultSlabHeightMm / 304.8);
                        if (intersector != null)
                        {
                            try
                            {
                                var rayResult = intersector.FindNearest(pt, XYZ.BasisZ);
                                if (rayResult != null)
                                {
                                    XYZ hitPt = rayResult.GetReference().GlobalPoint;
                                    if (hitPt != null && hitPt.Z > pt.Z)
                                    {
                                        topZ = hitPt.Z;
                                    }
                                }
                            }
                            catch { }
                        }

                        double rodLengthFt = Math.Max(0.5, topZ - pt.Z);

                        // Place Hanger Instance
                        FamilyInstance fi = doc.Create.NewFamilyInstance(pt, hangerSymbol, level, StructuralType.NonStructural);
                        if (fi != null)
                        {
                            // Set Rod Length parameter
                            Parameter pLen = fi.LookupParameter("Rod_Length") ??
                                             fi.LookupParameter("Rod Length") ??
                                             fi.LookupParameter("Length") ??
                                             fi.LookupParameter("Height");
                            if (pLen != null && !pLen.IsReadOnly)
                            {
                                pLen.Set(rodLengthFt);
                            }

                            // Set Rod Size text parameter
                            Parameter pSize = fi.LookupParameter("Rod_Size") ??
                                              fi.LookupParameter("Rod Size") ??
                                              fi.LookupParameter("Comments");
                            if (pSize != null && !pSize.IsReadOnly)
                            {
                                pSize.Set(settings.RodSize);
                            }

                            count++;
                        }
                    }
                }

                t.Commit();
            }

            return count;
        }
    }
}
