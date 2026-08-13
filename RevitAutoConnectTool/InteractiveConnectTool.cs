using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace RevitAutoConnect
{
    [Transaction(TransactionMode.Manual)]
    public class InteractiveConnectCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            int successCount = 0;

            try
            {
                // Vòng lặp chọn đối tượng liên tục (Bấm ESC để dừng)
                while (true)
                {
                    // 1. Pick đối tượng thứ 1 (Cố định vị trí)
                    Reference refA = null;
                    try
                    {
                        refA = uidoc.Selection.PickObject(ObjectType.Element, "Click chọn đối tượng thứ 1 (Cố định) [Bấm ESC để dừng]");
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        break; // Bấm ESC -> Thoát
                    }

                    if (refA == null) break;
                    Element elemA = doc.GetElement(refA);

                    // 2. Pick đối tượng thứ 2 (Sẽ di chuyển & xoay để căn thẳng hàng với đối tượng 1)
                    Reference refB = null;
                    try
                    {
                        refB = uidoc.Selection.PickObject(ObjectType.Element, "Click chọn đối tượng thứ 2 (Sẽ di chuyển thẳng hàng & kết nối) [Bấm ESC để dừng]");
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        break; // Bấm ESC -> Thoát
                    }

                    if (refB == null) break;
                    Element elemB = doc.GetElement(refB);

                    if (elemA.Id == elemB.Id)
                    {
                        TaskDialog.Show("Thông báo", "Bạn vừa chọn trùng 1 đối tượng. Vui lòng chọn 2 đối tượng khác nhau!");
                        continue;
                    }

                    // 3. Lấy danh sách connector tự do của cả 2 đối tượng
                    List<Connector> connsA = GetUnconnectedConnectors(elemA);
                    List<Connector> connsB = GetUnconnectedConnectors(elemB);

                    if (connsA.Count == 0)
                    {
                        TaskDialog.Show("Thông báo", $"Đối tượng 1 ({elemA.Name}) không còn Connector trống nào!");
                        continue;
                    }

                    if (connsB.Count == 0)
                    {
                        TaskDialog.Show("Thông báo", $"Đối tượng 2 ({elemB.Name}) không còn Connector trống nào!");
                        continue;
                    }

                    // 4. Tìm cặp Connector tốt nhất giữa elemA và elemB
                    Connector bestConnA = null;
                    Connector bestConnB = null;
                    double minDist = double.MaxValue;

                    foreach (Connector cA in connsA)
                    {
                        foreach (Connector cB in connsB)
                        {
                            if (cA.Domain != cB.Domain) continue;

                            double dist = cA.Origin.DistanceTo(cB.Origin);

                            // Ưu tiên cặp connector có hướng ngược chiều / song song nhau
                            XYZ dirA = cA.CoordinateSystem.BasisZ;
                            XYZ dirB = cB.CoordinateSystem.BasisZ;
                            double dot = Math.Abs(dirA.DotProduct(dirB));

                            if (dot > 0.8) dist /= 2.0;

                            if (dist < minDist)
                            {
                                minDist = dist;
                                bestConnA = cA;
                                bestConnB = cB;
                            }
                        }
                    }

                    // 5. Thực hiện Căn thẳng hàng & Kết nối
                    if (bestConnA != null && bestConnB != null)
                    {
                        using (Transaction trans = new Transaction(doc, "Interactive Align & Connect"))
                        {
                            trans.Start();

                            XYZ posA = bestConnA.Origin;
                            XYZ dirA = bestConnA.CoordinateSystem.BasisZ;

                            XYZ posB = bestConnB.Origin;
                            XYZ dirB = bestConnB.CoordinateSystem.BasisZ;

                            // Hướng mục tiêu của connector B phải ngược chiều với hướng connector A
                            XYZ targetDirB = -dirA;

                            // A. Xoay elemB nếu hướng connector B chưa khớp với targetDirB
                            double dotDir = dirB.DotProduct(targetDirB);
                            if (dotDir < 0.999)
                            {
                                XYZ rotAxis = dirB.CrossProduct(targetDirB);
                                double rotAngle = dirB.AngleTo(targetDirB);

                                if (rotAxis.GetLength() > 1e-5 && Math.Abs(rotAngle) > 1e-4)
                                {
                                    Line rotLine = Line.CreateBound(posB, posB + rotAxis.Normalize());
                                    ElementTransformUtils.RotateElement(doc, elemB.Id, rotLine, rotAngle);

                                    // Lấy lại vị trí posB mới sau khi xoay
                                    posB = bestConnB.Origin;
                                }
                            }

                            // B. Di chuyển elemB sao cho connector B trùng khớp với vị trí connector A
                            XYZ moveVec = posA - posB;
                            if (moveVec.GetLength() > 1e-5)
                            {
                                ElementTransformUtils.MoveElement(doc, elemB.Id, moveVec);
                            }

                            // C. Thực hiện kết nối chính thức trong Revit
                            bestConnA.ConnectTo(bestConnB);

                            trans.Commit();
                        }
                        successCount++;
                    }
                    else
                    {
                        TaskDialog.Show(
                            "Thông báo kết nối",
                            "Không tìm thấy cặp Connector cùng hệ thống MEP giữa 2 đối tượng này!"
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Lỗi", "Đã xảy ra lỗi khi kết nối: " + ex.Message);
            }

            return Result.Succeeded;
        }

        private List<Connector> GetUnconnectedConnectors(Element elem)
        {
            List<Connector> list = new List<Connector>();
            ConnectorSet connectorSet = null;

            if (elem is MEPCurve mepCurve && mepCurve.ConnectorManager != null)
            {
                connectorSet = mepCurve.ConnectorManager.Connectors;
            }
            else if (elem is FamilyInstance fi && fi.MEPModel != null && fi.MEPModel.ConnectorManager != null)
            {
                connectorSet = fi.MEPModel.ConnectorManager.Connectors;
            }

            if (connectorSet != null)
            {
                foreach (Connector conn in connectorSet)
                {
                    if (conn.ConnectorType == ConnectorType.End || conn.ConnectorType == ConnectorType.Curve)
                    {
                        try
                        {
                            if (!conn.IsConnected)
                            {
                                list.Add(conn);
                            }
                        }
                        catch { }
                    }
                }
            }

            return list;
        }
    }
}
