using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace OffsetPipeTool.Services
{
    public class OffsetPipeService
    {
        /// <summary>
        /// Dịch chuyển tất cả đối tượng đường ống/ống nhánh/MEP Curve được chọn một khoảng distanceFeet vuông góc với phương của ống
        /// </summary>
        public static int ShiftPipesPerpendicularly(Document doc, List<Element> mepElements, double distanceFeet)
        {
            if (mepElements == null || mepElements.Count == 0 || Math.Abs(distanceFeet) < 0.0001) return 0;

            int movedCount = 0;

            using (Transaction tx = new Transaction(doc, "Shift Pipes Perpendicularly"))
            {
                tx.Start();

                foreach (Element elem in mepElements)
                {
                    try
                    {
                        LocationCurve locCurve = elem.Location as LocationCurve;
                        if (locCurve == null || locCurve.Curve == null) continue;

                        Curve curve = locCurve.Curve;
                        XYZ p1 = curve.GetEndPoint(0);
                        XYZ p2 = curve.GetEndPoint(1);

                        XYZ dir = (p2 - p1);
                        double len = dir.GetLength();
                        if (len < 0.001) continue;

                        dir = dir.Normalize();

                        // Vector pháp tuyến vuông góc trong mặt phẳng XY (-Y, X, 0)
                        XYZ perpNormal = new XYZ(-dir.Y, dir.X, 0);
                        if (perpNormal.GetLength() < 0.001)
                        {
                            // Đối với ống đứng thẳng Z, dùng pháp tuyến theo trục X (1, 0, 0)
                            perpNormal = XYZ.BasisX;
                        }
                        else
                        {
                            perpNormal = perpNormal.Normalize();
                        }

                        // Vector tịnh tiến tịnh tiến vuông góc
                        XYZ moveVector = perpNormal * distanceFeet;

                        // Dịch chuyển đối tượng trong Revit
                        ElementTransformUtils.MoveElement(doc, elem.Id, moveVector);
                        movedCount++;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("Lỗi dịch ống: " + ex.Message);
                    }
                }

                tx.Commit();
            }

            return movedCount;
        }

        /// <summary>
        /// Bộ phân tích đơn vị đồng bộ (Feet Space Inch "9 6", Inch "4", Phân số "1 1/2", mm "100")
        /// </summary>
        public static double ParseLengthToFeet(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return 0.0;
            input = input.Trim().Replace("mm", "").Replace("DN", "").Replace("\"", "").Trim();

            string[] parts = input.Split(new char[] { ' ', '\'', '-' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 2)
            {
                // Cú pháp phân số inch e.g. "1 1/2"
                if (parts[1].Contains("/"))
                {
                    string[] frac = parts[1].Split('/');
                    if (double.TryParse(parts[0], out double whole) &&
                        double.TryParse(frac[0], out double num) &&
                        double.TryParse(frac[1], out double den) && den != 0)
                    {
                        double totalInches = whole + (num / den);
                        return totalInches / 12.0;
                    }
                }

                // Cú pháp Feet Space Inch e.g. "9 6" (9 feet 6 inches = 9.5 feet)
                if (double.TryParse(parts[0], out double feet) && double.TryParse(parts[1], out double inches))
                {
                    return feet + (inches / 12.0);
                }
            }
            else if (parts.Length == 1)
            {
                // Cú pháp phân số e.g. "1/2" hoặc "3/4"
                if (parts[0].Contains("/"))
                {
                    string[] frac = parts[0].Split('/');
                    if (double.TryParse(frac[0], out double num) &&
                        double.TryParse(frac[1], out double den) && den != 0)
                    {
                        double inches = num / den;
                        return inches / 12.0;
                    }
                }

                if (double.TryParse(parts[0], out double val))
                {
                    // Nếu giá trị > 50 (e.g. 100, 200, 300) -> Coi là mm
                    // Nếu giá trị <= 50 (e.g. 9, 4, 2) -> Coi là feet/inch (ví dụ 4" = 0.333ft, 9ft = 9ft)
                    if (val > 50) return val / 304.8;
                    return val; // feet
                }
            }

            return 0.0;
        }
    }
}
