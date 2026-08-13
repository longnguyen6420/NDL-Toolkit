using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;

namespace AutoSleeveTool.Services
{
    public class SleevePlacementService
    {
        private readonly Document _doc;

        public SleevePlacementService(Document doc)
        {
            _doc = doc;
        }

        public int PlaceSleeves(List<LinkPenetrationInfo> penetrations, double roundClearanceFeet, double rectClearanceFeet)
        {
            if (penetrations == null || penetrations.Count == 0) return 0;

            DuctType roundSleeveDuctType = GetOrCreateRoundSleeveDuctType();
            DuctType rectSleeveDuctType = GetOrCreateRectSleeveDuctType();
            ElementId systemTypeId = GetDefaultMechanicalSystemTypeId();
            Level level = GetDefaultLevel();

            if (systemTypeId == ElementId.InvalidElementId || level == null)
            {
                return 0;
            }

            // Collect all existing Sleeve Ducts in current document to prevent duplicates
            List<Duct> existingSleeves = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_DuctCurves)
                .WhereElementIsNotElementType()
                .Cast<Duct>()
                .Where(d => d.DuctType != null && d.DuctType.Name.IndexOf("sleeve", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            int count = 0;

            using (Transaction t = new Transaction(_doc, "Auto Sleeve Placement - Create Duct Sleeves"))
            {
                t.Start();

                foreach (var info in penetrations)
                {
                    try
                    {
                        // Check if a Sleeve already exists at this intersection point (within 6 inches)
                        bool isAlreadyPlaced = false;
                        foreach (Duct existingSleeve in existingSleeves)
                        {
                            LocationCurve lc = existingSleeve.Location as LocationCurve;
                            if (lc != null && lc.Curve != null)
                            {
                                double dist = lc.Curve.Distance(info.CenterPoint);
                                if (dist < 0.5) // 6 inches tolerance
                                {
                                    isAlreadyPlaced = true;
                                    break;
                                }
                            }
                        }

                        if (isAlreadyPlaced)
                        {
                            continue; // Skip creating duplicate sleeve at this intersection!
                        }

                        // Select correct DuctType: Round Sleeve for Pipe ONLY, Rectangular Sleeve for ALL Ducts (both Round Duct & Rect Duct)
                        DuctType targetDuctType = info.IsPipe ? roundSleeveDuctType : rectSleeveDuctType;
                        if (targetDuctType == null) targetDuctType = roundSleeveDuctType ?? rectSleeveDuctType;

                        // Calculate start and end points for sleeve duct segment (exact obstacle thickness & centered)
                        double halfLength = info.IntersectLength / 2.0;
                        XYZ pStart = info.CenterPoint - info.Direction * halfLength;
                        XYZ pEnd = info.CenterPoint + info.Direction * halfLength;

                        if (pStart.DistanceTo(pEnd) < 0.01) continue;

                        Duct sleeveDuct = Duct.Create(_doc, systemTypeId, targetDuctType.Id, level.Id, pStart, pEnd);

                        if (sleeveDuct != null)
                        {
                            if (info.IsPipe)
                            {
                                Parameter diaParam = sleeveDuct.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
                                if (diaParam != null && !diaParam.IsReadOnly)
                                {
                                    diaParam.Set(info.MepWidthOrDiameter + roundClearanceFeet);
                                }
                            }
                            else
                            {
                                Parameter wParam = sleeveDuct.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
                                Parameter hParam = sleeveDuct.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);
                                if (wParam != null && !wParam.IsReadOnly)
                                {
                                    wParam.Set(info.MepWidthOrDiameter + rectClearanceFeet);
                                }
                                if (hParam != null && !hParam.IsReadOnly)
                                {
                                    hParam.Set(info.MepHeight + rectClearanceFeet);
                                }
                            }

                            // Add newly created sleeve to existingSleeves list to prevent duplicates in current run
                            existingSleeves.Add(sleeveDuct);

                            count++;
                        }
                    }
                    catch
                    {
                        // Ignore individual placement errors
                    }
                }

                t.Commit();
            }

            return count;
        }

        private DuctType GetOrCreateRoundSleeveDuctType()
        {
            var collector = new FilteredElementCollector(_doc)
                .OfClass(typeof(DuctType))
                .Cast<DuctType>();

            DuctType existing = collector.FirstOrDefault(dt =>
                (dt.Name.Equals("sleeve_round", StringComparison.OrdinalIgnoreCase) || dt.Name.Equals("sleeve", StringComparison.OrdinalIgnoreCase)) &&
                dt.Shape == ConnectorProfileType.Round);

            if (existing != null) return existing;

            DuctType roundType = collector.FirstOrDefault(dt => dt.Shape == ConnectorProfileType.Round);
            if (roundType == null) roundType = collector.FirstOrDefault();

            if (roundType != null)
            {
                using (Transaction t = new Transaction(_doc, "Create 'sleeve_round' DuctType"))
                {
                    t.Start();
                    DuctType newType = (DuctType)roundType.Duplicate("sleeve_round");
                    t.Commit();
                    return newType;
                }
            }

            return null;
        }

        private DuctType GetOrCreateRectSleeveDuctType()
        {
            var collector = new FilteredElementCollector(_doc)
                .OfClass(typeof(DuctType))
                .Cast<DuctType>();

            DuctType existing = collector.FirstOrDefault(dt =>
                (dt.Name.Equals("sleeve_rect", StringComparison.OrdinalIgnoreCase) || dt.Name.Equals("sleeve", StringComparison.OrdinalIgnoreCase)) &&
                dt.Shape == ConnectorProfileType.Rectangular);

            if (existing != null) return existing;

            DuctType rectType = collector.FirstOrDefault(dt => dt.Shape == ConnectorProfileType.Rectangular);
            if (rectType == null) rectType = collector.FirstOrDefault();

            if (rectType != null)
            {
                using (Transaction t = new Transaction(_doc, "Create 'sleeve_rect' DuctType"))
                {
                    t.Start();
                    DuctType newType = (DuctType)rectType.Duplicate("sleeve_rect");
                    t.Commit();
                    return newType;
                }
            }

            return null;
        }

        private ElementId GetDefaultMechanicalSystemTypeId()
        {
            var systemType = new FilteredElementCollector(_doc)
                .OfClass(typeof(MechanicalSystemType))
                .FirstOrDefault();

            return systemType != null ? systemType.Id : ElementId.InvalidElementId;
        }

        private Level GetDefaultLevel()
        {
            Level level = _doc.ActiveView != null ? _doc.ActiveView.GenLevel : null;
            if (level == null)
            {
                level = new FilteredElementCollector(_doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .FirstOrDefault();
            }
            return level;
        }
    }
}
