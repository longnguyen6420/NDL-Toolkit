using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RotateVerticalTool.Services
{
    public class RotateVerticalService
    {
        /// <summary>
        /// Xoay toàn bộ cụm đối tượng được chọn xung quanh trục tâm của ống tim xoay
        /// </summary>
        public static bool RotateGroupAroundPipeAxis(Document doc, ICollection<ElementId> elementIds, Element axisPipe, double angleDegrees)
        {
            if (doc == null || elementIds == null || elementIds.Count == 0 || axisPipe == null) return false;

            LocationCurve locCurve = axisPipe.Location as LocationCurve;
            if (locCurve == null || locCurve.Curve == null) return false;

            XYZ p1 = locCurve.Curve.GetEndPoint(0);
            XYZ p2 = locCurve.Curve.GetEndPoint(1);
            XYZ dir = (p2 - p1);

            if (dir.GetLength() < 0.001) return false;
            dir = dir.Normalize();

            // Đường thẳng trục xoay 3D đi qua đường tâm ống xoay
            Line axisLine = Line.CreateUnbound(p1, dir);
            double angleRad = angleDegrees * (Math.PI / 180.0);

            using (Transaction tx = new Transaction(doc, "Rotate Group Around Pipe Axis"))
            {
                tx.Start();
                try
                {
                    ElementTransformUtils.RotateElements(doc, elementIds, axisLine, angleRad);
                    tx.Commit();
                    return true;
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    System.Diagnostics.Debug.WriteLine("Lỗi xoay 3D: " + ex.Message);
                    return false;
                }
            }
        }
    }
}
