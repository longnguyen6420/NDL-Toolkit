using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Structure;

namespace NDL.AutoHangerTool.Services
{
    public enum HangerMode
    {
        AutoBySize,
        SingleRodAlways,
        DualRodAlways
    }

    public class HangerSettings
    {
        public double SpacingInches { get; set; } = 96.0; // 8 ft default spacing
        public double FittingOffsetInches { get; set; } = 12.0; // 1 ft from fittings
        public bool PlaceNearFittings { get; set; } = true;
        public string RodSize { get; set; } = "1/2\"";
        public double DefaultSlabHeightInches { get; set; } = 144.0; // 12 ft

        // Dual Rod Settings for Large Pipes
        public HangerMode Mode { get; set; } = HangerMode.AutoBySize;
        public double DualRodThresholdInches { get; set; } = 6.0; // Pipes >= 6" use 2 rods
        public double RodSideClearanceInches { get; set; } = 2.5; // Distance from pipe outer edge to rod
    }

    public static class HangerPlacementService
    {
        public static double GetElementOuterDiameterInches(Element elem)
        {
            if (elem == null) return 4.0;

            Parameter pDiam = elem.get_Parameter(BuiltInParameter.RBS_PIPE_OUTER_DIAMETER) ??
                             elem.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM) ??
                             elem.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM) ??
                             elem.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);

            if (pDiam != null && pDiam.HasValue)
            {
                return pDiam.AsDouble() * 12.0; // Convert feet to inches
            }

            return 4.0; // default 4 inches
        }

        public static int PlaceHangersOnPipes(Document doc, List<Element> pipes, HangerSettings settings, FamilySymbol hangerSymbol)
        {
            if (doc == null || pipes == null || pipes.Count == 0 || hangerSymbol == null)
                return 0;

            int count = 0;
            double spacingFt = settings.SpacingInches / 12.0;
            double fittingOffsetFt = settings.FittingOffsetInches / 12.0;

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
                    if (length < 1.0) continue; // Skip very short segments (< 1 foot)

                    double pipeDiamInches = GetElementOuterDiameterInches(elem);
                    double pipeDiamFt = pipeDiamInches / 12.0;

                    // Determine if this pipe uses 2 rods (Dual Inserts) or 1 rod (Single Insert)
                    bool useDualRods = false;
                    if (settings.Mode == HangerMode.DualRodAlways)
                    {
                        useDualRods = true;
                    }
                    else if (settings.Mode == HangerMode.AutoBySize)
                    {
                        useDualRods = pipeDiamInches >= settings.DualRodThresholdInches;
                    }

                    // Calculate direction and horizontal perpendicular vector
                    XYZ p0 = curve.GetEndPoint(0);
                    XYZ p1 = curve.GetEndPoint(1);
                    XYZ dir = (p1 - p0).Normalize();
                    XYZ perp = new XYZ(-dir.Y, dir.X, 0);
                    if (perp.GetLength() < 0.001)
                    {
                        perp = XYZ.BasisX;
                    }
                    else
                    {
                        perp = perp.Normalize();
                    }

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
                    double defaultTopZ = levelElev + (settings.DefaultSlabHeightInches / 12.0);

                    foreach (double param in paramList.OrderBy(p => p))
                    {
                        XYZ centerPt = curve.Evaluate(param, true);

                        List<XYZ> rodPoints = new List<XYZ>();
                        if (useDualRods)
                        {
                            // 2 Rods: Left and Right of Pipe
                            double halfSpanFt = (pipeDiamFt / 2.0) + (settings.RodSideClearanceInches / 12.0);
                            rodPoints.Add(centerPt + perp * halfSpanFt);
                            rodPoints.Add(centerPt - perp * halfSpanFt);
                        }
                        else
                        {
                            // 1 Rod: Center of Pipe
                            rodPoints.Add(centerPt);
                        }

                        foreach (XYZ pt in rodPoints)
                        {
                            // Detect slab / beam elevation above rod point
                            double topZ = defaultTopZ;
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
                }

                t.Commit();
            }

            return count;
        }
    }
}
