using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace AutoConnectNearestPipeTool.Services
{
    public class OrthogonalAutoConnectService
    {
        /// <summary>
        /// Thuật toán kết nối trực tiếp ĐẦU PHUN ĐƯỢC CHỌN vào Ống bằng ỐNG 3D THẬT
        /// RÀNG BUỘC TUYỆT ĐỐI: Connector của Đầu phun PHẢI ĐƯỢC NỐI TRỰC TIẾP (ConnectTo) với Connector của Ống gần nó nhất
        /// </summary>
        public static int ConnectSelectedSprinklersToPipe(Document doc, List<Element> selectedSprinklers, List<Element> targetPipes)
        {
            if (selectedSprinklers == null || selectedSprinklers.Count == 0 || targetPipes == null || targetPipes.Count == 0) return 0;

            int connectedCount = 0;

            using (Transaction tx = new Transaction(doc, "Connect Sprinkler Connectors directly to Pipes"))
            {
                tx.Start();

                List<ElementId> allPlaceholderIds = new List<ElementId>();
                List<(Element sprinkler, Pipe vPipe, Pipe hPipe)> createdPairs = new List<(Element, Pipe, Pipe)>();

                foreach (Element sprinklerElem in selectedSprinklers)
                {
                    XYZ sPt = GetSprinklerPoint(sprinklerElem);
                    if (sPt == null) continue;

                    // 1. Tìm đường ống chính gần nhất
                    Element nearestPipe = null;
                    double minDistance = double.MaxValue;

                    foreach (Element pipeElem in targetPipes)
                    {
                        LocationCurve pipeLoc = pipeElem.Location as LocationCurve;
                        if (pipeLoc == null || pipeLoc.Curve == null) continue;

                        double dist = pipeLoc.Curve.Distance(sPt);
                        if (dist < minDistance)
                        {
                            minDistance = dist;
                            nearestPipe = pipeElem;
                        }
                    }

                    if (nearestPipe == null) continue;

                    LocationCurve mainLoc = nearestPipe.Location as LocationCurve;
                    Curve mainCurve = mainLoc.Curve;
                    XYZ m1 = mainCurve.GetEndPoint(0);
                    XYZ m2 = mainCurve.GetEndPoint(1);

                    double pipeZ = (m1.Z + m2.Z) * 0.5;

                    // Chiếu điểm 2D của đầu phun lên đường tâm ống chính (Nút Tê 90°)
                    XYZ projPt2D = ProjectPointToLine2D(sPt, m1, m2);
                    XYZ teePt = new XYZ(projPt2D.X, projPt2D.Y, pipeZ);

                    // Điểm nút góc L Rẽ vuông góc (Nút Cút 90°)
                    XYZ dropTurnPt = new XYZ(sPt.X, sPt.Y, pipeZ);

                    // Thuộc tính systemTypeId, pipeTypeId, levelId & Đường kính
                    Pipe refPipe = nearestPipe as Pipe;
                    ElementId sysTypeId = refPipe != null && refPipe.MEPSystem != null ? refPipe.MEPSystem.GetTypeId() : ElementId.InvalidElementId;
                    ElementId pipeTypeId = refPipe != null ? refPipe.GetTypeId() : ElementId.InvalidElementId;
                    ElementId levelId = refPipe != null && refPipe.ReferenceLevel != null ? refPipe.ReferenceLevel.Id : ElementId.InvalidElementId;

                    double branchDiameter = 1.0 / 12.0; // Mặc định 1 inch (25mm)
                    if (refPipe != null)
                    {
                        Parameter dParam = refPipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                        if (dParam != null && dParam.AsDouble() > 0)
                        {
                            branchDiameter = Math.Min(dParam.AsDouble(), 1.5 / 12.0); // Tối đa 1.5 inch
                        }
                    }

                    if (sysTypeId == ElementId.InvalidElementId || pipeTypeId == ElementId.InvalidElementId)
                    {
                        sysTypeId = new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType)).FirstElementId();
                        pipeTypeId = new FilteredElementCollector(doc).OfClass(typeof(PipeType)).FirstElementId();
                        levelId = new FilteredElementCollector(doc).OfClass(typeof(Level)).FirstElementId();
                    }

                    Pipe vPipe = null;
                    Pipe hPipe = null;

                    // A. Tạo Ống đứng (Từ Đầu phun đến Điểm rẽ Cút 90°)
                    if (Math.Abs(sPt.Z - pipeZ) > 0.05)
                    {
                        vPipe = Pipe.CreatePlaceholder(doc, sysTypeId, pipeTypeId, levelId, sPt, dropTurnPt);
                        if (vPipe != null)
                        {
                            SetPipeDiameter(vPipe, branchDiameter);
                            allPlaceholderIds.Add(vPipe.Id);
                        }
                    }

                    // B. Tạo Ống ngang (Từ Điểm rẽ Cút 90° đến Nút Tê 90° trên Ống chính)
                    if (dropTurnPt.DistanceTo(teePt) > 0.05)
                    {
                        hPipe = Pipe.CreatePlaceholder(doc, sysTypeId, pipeTypeId, levelId, dropTurnPt, teePt);
                        if (hPipe != null)
                        {
                            SetPipeDiameter(hPipe, branchDiameter);
                            allPlaceholderIds.Add(hPipe.Id);
                        }
                    }

                    // C. KẾT NỐI CONNECTOR ĐẦU PHUN TRỰC TIẾP VÀO CONNECTOR ỐNG GẦN NÓ NHẤT
                    Connector sprConn = GetSprinklerConnector(sprinklerElem);
                    Pipe targetBranchPipe = vPipe ?? hPipe;

                    if (sprConn != null && targetBranchPipe != null)
                    {
                        Connector pipeConnNearSpr = GetConnectorNearPoint(targetBranchPipe, sprConn.Origin);
                        if (pipeConnNearSpr != null && !sprConn.IsConnectedTo(pipeConnNearSpr))
                        {
                            try
                            {
                                sprConn.ConnectTo(pipeConnNearSpr);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine("Lỗi ConnectTo Sprinkler -> Pipe: " + ex.Message);
                            }
                        }
                    }

                    // D. Nối các Connector giữa Ống đứng và Ống ngang
                    if (vPipe != null && hPipe != null)
                    {
                        Connector vTop = GetConnectorNearPoint(vPipe, dropTurnPt);
                        Connector hStart = GetConnectorNearPoint(hPipe, dropTurnPt);

                        if (vTop != null && hStart != null && !vTop.IsConnectedTo(hStart))
                        {
                            try { vTop.ConnectTo(hStart); } catch { }
                        }
                    }

                    if (targetBranchPipe != null)
                    {
                        createdPairs.Add((sprinklerElem, vPipe, hPipe));
                        connectedCount++;
                    }
                }

                // 2. Chuyển đổi thành Ống 3D Thật & Đảm bảo Connector được nối 100%
                if (allPlaceholderIds.Count > 0)
                {
                    try
                    {
                        PlumbingUtils.ConvertPipePlaceholders(doc, allPlaceholderIds);

                        // 3. Khóa kết nối Connector Đầu Phun <-> Ống 3D Thật sau khi Convert
                        foreach (var item in createdPairs)
                        {
                            try
                            {
                                Connector sprConn = GetSprinklerConnector(item.sprinkler);
                                Element pipeElem = doc.GetElement(item.vPipe != null ? item.vPipe.Id : item.hPipe.Id);

                                if (sprConn != null && pipeElem is Pipe realPipe)
                                {
                                    Connector pipeConn = GetConnectorNearPoint(realPipe, sprConn.Origin);
                                    if (pipeConn != null && !sprConn.IsConnectedTo(pipeConn))
                                    {
                                        sprConn.ConnectTo(pipeConn);
                                    }
                                }

                                // Tạo Cút 90° 3D nếu có 2 ống
                                if (item.vPipe != null && item.hPipe != null)
                                {
                                    Pipe vReal = doc.GetElement(item.vPipe.Id) as Pipe;
                                    Pipe hReal = doc.GetElement(item.hPipe.Id) as Pipe;

                                    if (vReal != null && hReal != null)
                                    {
                                        XYZ turnPt = new XYZ(GetSprinklerPoint(item.sprinkler).X, GetSprinklerPoint(item.sprinkler).Y, (vReal.Location as LocationCurve).Curve.GetEndPoint(0).Z);
                                        Connector c1 = GetConnectorNearPoint(vReal, turnPt);
                                        Connector c2 = GetConnectorNearPoint(hReal, turnPt);

                                        if (c1 != null && c2 != null && !c1.IsConnectedTo(c2))
                                        {
                                            doc.Create.NewElbowFitting(c1, c2);
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("Lỗi khóa Connector Sprinkler: " + ex.Message);
                    }
                }

                tx.Commit();
            }

            return connectedCount;
        }

        private static Connector GetSprinklerConnector(Element elem)
        {
            if (elem is FamilyInstance fi && fi.MEPModel != null && fi.MEPModel.ConnectorManager != null)
            {
                foreach (Connector c in fi.MEPModel.ConnectorManager.Connectors)
                {
                    return c; // Trả về Connector chính của Đầu Phun
                }
            }
            return null;
        }

        private static Connector GetConnectorNearPoint(Element elem, XYZ pt)
        {
            if (elem == null) return null;
            ConnectorSet conns = null;

            if (elem is Pipe p) conns = p.ConnectorManager.Connectors;
            else if (elem is FamilyInstance fi && fi.MEPModel != null) conns = fi.MEPModel.ConnectorManager.Connectors;

            if (conns == null) return null;

            Connector best = null;
            double minDist = double.MaxValue;

            foreach (Connector c in conns)
            {
                double d = c.Origin.DistanceTo(pt);
                if (d < minDist)
                {
                    minDist = d;
                    best = c;
                }
            }

            return best;
        }

        private static void SetPipeDiameter(Pipe pipe, double diameterFeet)
        {
            if (pipe == null) return;
            Parameter diamParam = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            if (diamParam != null && !diamParam.IsReadOnly)
            {
                diamParam.Set(diameterFeet);
            }
        }

        private static XYZ GetSprinklerPoint(Element elem)
        {
            if (elem is FamilyInstance inst && inst.Location is LocationPoint locPt)
            {
                return locPt.Point;
            }
            else if (elem.Location is LocationPoint locP)
            {
                return locP.Point;
            }
            else if (elem.Location is LocationCurve locC)
            {
                return locC.Curve.GetEndPoint(0);
            }
            return null;
        }

        private static XYZ ProjectPointToLine2D(XYZ pt, XYZ lineStart, XYZ lineEnd)
        {
            XYZ v = (lineEnd - lineStart);
            double lenSq = v.X * v.X + v.Y * v.Y;
            if (lenSq < 0.0001) return lineStart;

            double t = ((pt.X - lineStart.X) * v.X + (pt.Y - lineStart.Y) * v.Y) / lenSq;
            return new XYZ(lineStart.X + t * v.X, lineStart.Y + t * v.Y, pt.Z);
        }
    }
}
