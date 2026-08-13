using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace RevitAlignTools
{
    [Transaction(TransactionMode.Manual)]
    public class AlignBranchCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // Vòng lặp 1: Chọn Ống CHÍNH làm chuẩn (Main Header Pipe / Duct)
                while (true)
                {
                    Reference refMain = null;
                    try
                    {
                        refMain = uidoc.Selection.PickObject(
                            ObjectType.Element,
                            "Click chọn ỐNG CHÍNH (Main Header) làm CHUẨN [Bấm ESC để thoát hoàn toàn]"
                        );
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        break; // Bấm ESC -> Thoát hoàn toàn
                    }

                    if (refMain == null) break;
                    Element elemMain = doc.GetElement(refMain);
                    Line lineMain = GetElementCenterline(elemMain);

                    if (lineMain == null)
                    {
                        TaskDialog.Show("Thông báo", "Vui lòng chọn 1 đường ống (Pipe/Duct) hợp lệ!");
                        continue;
                    }

                    // Vòng lặp 2: Cho phép click chọn LIÊN TỤC nhiều Ống Nhánh (Align 1 phương duy nhất)
                    while (true)
                    {
                        Reference refBranch = null;
                        try
                        {
                            refBranch = uidoc.Selection.PickObject(
                                ObjectType.Element,
                                $"[Đang giữ Ống Chính: {elemMain.Name}] Click chọn ỐNG NHÁNH để dịch chuyển theo 1 phương duy nhất [Bấm ESC để đổi Ống Chính khác]"
                            );
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                        {
                            break; // Bấm ESC 1 lần -> Đổi sang chọn Ống Chính khác
                        }

                        if (refBranch == null) break;
                        Element elemBranch = doc.GetElement(refBranch);

                        if (elemBranch.Id == elemMain.Id)
                        {
                            TaskDialog.Show("Thông báo", "Bạn vừa chọn trùng Ống Chính! Vui lòng chọn Ống Nhánh khác.");
                            continue;
                        }

                        Line lineBranch = GetElementCenterline(elemBranch);
                        if (lineBranch == null) continue;

                        XYZ moveVector = CalculateSingleDirectionAlignVector(lineMain, lineBranch);

                        if (moveVector.GetLength() < 1e-6)
                        {
                            continue;
                        }

                        using (Transaction trans = new Transaction(doc, "Align Branch Single Direction"))
                        {
                            trans.Start();

                            ElementTransformUtils.MoveElement(doc, elemBranch.Id, moveVector);

                            trans.Commit();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Lỗi", "Đã xảy ra lỗi Align Branch: " + ex.Message);
            }

            return Result.Succeeded;
        }

        private Line GetElementCenterline(Element elem)
        {
            if (elem.Location is LocationCurve locCurve && locCurve.Curve is Line line)
            {
                return line;
            }
            else if (elem.Location is LocationPoint locPoint)
            {
                XYZ p = locPoint.Point;
                return Line.CreateBound(p, p + XYZ.BasisX);
            }
            return null;
        }

        private XYZ CalculateSingleDirectionAlignVector(Line lineA, Line lineB)
        {
            XYZ pA = lineA.GetEndPoint(0);
            XYZ u = lineA.Direction.Normalize();

            XYZ pB = lineB.GetEndPoint(0);
            XYZ v = lineB.Direction.Normalize();

            XYZ w0 = pA - pB;
            double a = u.DotProduct(u);
            double b = u.DotProduct(v);
            double c = v.DotProduct(v);
            double d = u.DotProduct(w0);
            double e = v.DotProduct(w0);

            double denom = a * c - b * b;

            XYZ closestA, closestB;

            if (denom < 1e-6)
            {
                double t = d / a;
                closestA = pA + t * u;
                closestB = pB;
            }
            else
            {
                double t = (b * e - c * d) / denom;
                double s = (a * e - b * d) / denom;

                closestA = pA + t * u;
                closestB = pB + s * v;
            }

            return closestA - closestB;
        }
    }
}
