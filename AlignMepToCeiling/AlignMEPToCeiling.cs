using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AlignMEPToCeiling
{
    [Transaction(TransactionMode.Manual)]
    public class AlignMEPToCeilingCommand : IExternalCommand
    {
        private const double TOLERANCE = 1.0 / 304.8;
        private const double MAX_SEARCH_HEIGHT = 30.0;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View3D view3D = doc.ActiveView as View3D;

            if (view3D == null)
            {
                TaskDialog.Show("Align to Ceiling", "Please run this command from a 3D View.");
                return Result.Failed;
            }

            if (!view3D.IsSectionBoxActive)
            {
                TaskDialog.Show("Align to Ceiling", "The active 3D View does not have a Section Box.\n\nPlease enable Section Box and try again.");
                return Result.Failed;
            }

            BoundingBoxXYZ sectionBox = view3D.GetSectionBox();
            if (sectionBox == null)
            {
                TaskDialog.Show("Align to Ceiling", "Cannot read the Section Box.");
                return Result.Failed;
            }

            List<Element> targets = new List<Element>();
            targets.AddRange(new FilteredElementCollector(doc, view3D.Id)
                .OfCategory(BuiltInCategory.OST_Sprinklers)
                .WhereElementIsNotElementType());
            targets.AddRange(new FilteredElementCollector(doc, view3D.Id)
                .OfCategory(BuiltInCategory.OST_DuctTerminal)
                .WhereElementIsNotElementType());

            List<Element> filteredTargets = new List<Element>();
            foreach (Element element in targets)
            {
                XYZ point = GetElementPoint(element);
                if (point != null && IsInsideSectionBox(point, sectionBox))
                    filteredTargets.Add(element);
            }

            if (filteredTargets.Count == 0)
            {
                TaskDialog.Show("Align to Ceiling", "No sprinkler heads or air terminals were found inside the current Section Box.");
                return Result.Cancelled;
            }

            ElementCategoryFilter ceilingFilter =
                new ElementCategoryFilter(BuiltInCategory.OST_Ceilings);

            ReferenceIntersector intersector =
                new ReferenceIntersector(ceilingFilter, FindReferenceTarget.Face, view3D)
                {
                    FindReferencesInRevitLinks = true
                };

            int alignedCount = 0;
            int skippedCount = 0;
            List<string> skippedReasons = new List<string>();

            using (Transaction tx = new Transaction(doc, "Align Sprinklers and Air Terminals to Ceiling"))
            {
                tx.Start();

                foreach (Element element in filteredTargets)
                {
                    try
                    {
                        XYZ currentPoint = GetElementPoint(element);
                        if (currentPoint == null)
                        {
                            skippedCount++;
                            skippedReasons.Add($"{element.Id}: Cannot determine location.");
                            continue;
                        }

                        ReferenceWithContext nearest = intersector.FindNearest(currentPoint, XYZ.BasisZ);
                        if (nearest == null)
                        {
                            skippedCount++;
                            skippedReasons.Add($"{element.Id}: No ceiling found above.");
                            continue;
                        }

                        XYZ ceilingPoint = nearest.GetReference().GlobalPoint;
                        if (ceilingPoint == null)
                        {
                            skippedCount++;
                            skippedReasons.Add($"{element.Id}: Ceiling point unavailable.");
                            continue;
                        }

                        double deltaZ = ceilingPoint.Z - currentPoint.Z;
                        if (deltaZ < -TOLERANCE)
                        {
                            skippedCount++;
                            skippedReasons.Add($"{element.Id}: Ceiling is below element.");
                            continue;
                        }

                        if (deltaZ > MAX_SEARCH_HEIGHT)
                        {
                            skippedCount++;
                            skippedReasons.Add($"{element.Id}: Ceiling is too far away.");
                            continue;
                        }

                        if (Math.Abs(deltaZ) <= TOLERANCE)
                            continue;

                        ElementTransformUtils.MoveElement(doc, element.Id, new XYZ(0, 0, deltaZ));
                        alignedCount++;
                    }
                    catch (Exception ex)
                    {
                        skippedCount++;
                        skippedReasons.Add($"{element.Id}: {ex.Message}");
                    }
                }

                tx.Commit();
            }

            string result = $"Align completed.\n\nFound: {filteredTargets.Count}\nAligned: {alignedCount}\nSkipped: {skippedCount}";
            if (skippedReasons.Count > 0)
            {
                result += "\n\nSkipped details:\n";
                int maxMessages = Math.Min(skippedReasons.Count, 10);
                for (int i = 0; i < maxMessages; i++)
                    result += "\n• " + skippedReasons[i];
                if (skippedReasons.Count > maxMessages)
                    result += $"\n\n... and {skippedReasons.Count - maxMessages} more.";
            }

            TaskDialog.Show("Align to Ceiling", result);
            return Result.Succeeded;
        }

        private XYZ GetElementPoint(Element element)
        {
            Location location = element.Location;
            if (location is LocationPoint locationPoint)
                return locationPoint.Point;

            if (location is LocationCurve locationCurve && locationCurve.Curve != null)
                return locationCurve.Curve.Evaluate(0.5, true);

            BoundingBoxXYZ bbox = element.get_BoundingBox(null);
            if (bbox != null)
                return (bbox.Min + bbox.Max) / 2.0;

            return null;
        }

        private bool IsInsideSectionBox(XYZ point, BoundingBoxXYZ sectionBox)
        {
            if (point == null || sectionBox == null)
                return false;

            XYZ localPoint = sectionBox.Transform.Inverse.OfPoint(point);
            XYZ min = sectionBox.Min;
            XYZ max = sectionBox.Max;

            return localPoint.X >= min.X - TOLERANCE && localPoint.X <= max.X + TOLERANCE &&
                   localPoint.Y >= min.Y - TOLERANCE && localPoint.Y <= max.Y + TOLERANCE &&
                   localPoint.Z >= min.Z - TOLERANCE && localPoint.Z <= max.Z + TOLERANCE;
        }
    }
}
