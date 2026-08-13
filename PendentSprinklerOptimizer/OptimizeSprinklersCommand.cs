using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;

namespace PendentSprinklerOptimizer
{
    /// <summary>
    /// Lệnh Revit ngoài: Công cụ tối ưu hóa đầu phun Sprinkler hướng xuống (Pendent Sprinkler).
    /// Chỉ thực hiện cho các đối tượng nằm trong Section Box của góc nhìn 3D hiện tại.
    /// Thực hiện ba bước trong một thao tác duy nhất:
    /// 1. Căn chỉnh đầu phun hướng xuống (Pendent) theo mặt dưới của trần kiến trúc từ file link.
    /// 2. Xóa ống nhánh/co nối lên đến 3.0 feet (hoặc xóa đến khớp nối chữ T chính nếu chiều dài ống nhánh ngắn hơn 3.5 feet).
    ///    - Đồng thời xóa đoạn ống còn lại và Khớp nối (Coupling) nếu nó cách co nối ít hơn 6 inches.
    /// 3. Tự động kết nối đầu phun với đoạn ống còn lại (thông qua Khớp nối) hoặc khớp nối chữ T (Tee)/Co (Elbow) bằng ống mềm (Flex Pipe) 1".
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class OptimizeSprinklersCommand : IExternalCommand
    {
        private const double MAX_DELETE_LENGTH_FEET = 3.0; // 3 feet
        private const double MIN_REMAINING_PIPE = 0.5; // 6 inches
        private const double PIPE_DIAMETER_INCH = 1.0;
        private const double INCH_TO_FEET = 1.0 / 12.0;
        private const double PIPE_DIAMETER_FEET = PIPE_DIAMETER_INCH * INCH_TO_FEET;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            try
            {
                // Kiểm tra góc nhìn 3D hiện tại và Section Box
                View3D view3D = uiDoc.ActiveView as View3D;
                if (view3D == null || view3D.IsTemplate || !view3D.IsSectionBoxActive)
                {
                    TaskDialog.Show("Lỗi Section Box", "Công cụ chỉ thực hiện cho các đối tượng nằm trong Section Box.\nVui lòng mở một góc nhìn 3D, bật Section Box khoanh vùng khu vực cần xử lý và thử lại.");
                    return Result.Failed;
                }

                BoundingBoxXYZ sectionBox = view3D.GetSectionBox();
                if (sectionBox == null)
                {
                    TaskDialog.Show("Lỗi", "Không thể lấy thông tin Section Box từ góc nhìn 3D hiện tại.");
                    return Result.Failed;
                }

                // Lấy kiểu ống mềm (Flex Pipe Type) và kiểu hệ thống đường ống (Piping System Type)
                FlexPipeType flexPipeType = FindOrGetFlexPipeType(doc);
                if (flexPipeType == null)
                {
                    TaskDialog.Show("Lỗi", "Không tìm thấy kiểu ống mềm (Flex Pipe Type) trong dự án. Vui lòng tải family ống mềm trước.");
                    return Result.Failed;
                }

                PipingSystemType systemType = FindPipingSystemType(doc);
                if (systemType == null)
                {
                    TaskDialog.Show("Lỗi", "Không tìm thấy kiểu hệ thống chữa cháy hoặc kiểu hệ thống đường ống phù hợp trong dự án.");
                    return Result.Failed;
                }

                // Tìm các đầu phun hướng xuống (Pendent) nằm trong Section Box
                List<FamilyInstance> pendentSprinklers = FindPendentSprinklers(doc, view3D, sectionBox);
                if (pendentSprinklers.Count == 0)
                {
                    TaskDialog.Show("Thông tin", "Không tìm thấy đầu phun chữa cháy hướng xuống (Pendent) nào nằm trong Section Box.");
                    return Result.Succeeded;
                }

                int processedCount = 0;
                List<ElementId> elementsToDelete = new List<ElementId>();
                List<FlexConnectionTask> connectionTasks = new List<FlexConnectionTask>();

                using (Transaction trans = new Transaction(doc, "Tối ưu hóa đầu phun hướng xuống trong Section Box"))
                {
                    trans.Start();

                    // ============================================================
                    // BƯỚC 1: Căn chỉnh đầu phun hướng xuống theo trần liên kết (Linked Ceilings)
                    // ============================================================
                    ElementCategoryFilter ceilingFilter = new ElementCategoryFilter(BuiltInCategory.OST_Ceilings);
                    ReferenceIntersector intersector = new ReferenceIntersector(ceilingFilter, FindReferenceTarget.Face, view3D);
                    intersector.FindReferencesInRevitLinks = true;

                    foreach (FamilyInstance sprinkler in pendentSprinklers)
                    {
                        LocationPoint lp = sprinkler.Location as LocationPoint;
                        if (lp == null) continue;

                        XYZ origin = lp.Point;
                        XYZ direction = XYZ.BasisZ;

                        ReferenceWithContext refContext = null;
                        try { refContext = intersector.FindNearest(origin, direction); } catch { }

                        if (refContext != null)
                        {
                            Reference reference = refContext.GetReference();
                            if (reference != null)
                            {
                                XYZ intersectPoint = reference.GlobalPoint;
                                double deltaZ = intersectPoint.Z - origin.Z;

                                if (Math.Abs(deltaZ) > 0.001)
                                {
                                    XYZ translation = new XYZ(0, 0, deltaZ);
                                    ElementTransformUtils.MoveElement(doc, sprinkler.Id, translation);
                                }
                            }
                        }
                    }

                    // Tái tạo (Regenerate) tài liệu để các đường ống tự điều chỉnh theo vị trí mới của đầu phun
                    doc.Regenerate();

                    // ============================================================
                    // BƯỚC 2: Truy vết và Rút ngắn/Xóa ống nhánh
                    // ============================================================
                    foreach (FamilyInstance sprinkler in pendentSprinklers)
                    {
                        Connector sprinklerConnector = GetSprinklerConnector(sprinkler);
                        if (sprinklerConnector == null || !sprinklerConnector.IsConnected)
                            continue;

                        List<MEPCurve> pipes;
                        List<FamilyInstance> fittings;
                        List<Element> pathInOrder;
                        double totalLength;

                        TraceBranch(sprinklerConnector, out pipes, out fittings, out pathInOrder, out totalLength);
                        if (pipes.Count == 0) continue;

                        processedCount++;

                        double teeThreshold = MAX_DELETE_LENGTH_FEET + MIN_REMAINING_PIPE; // 3.5 feet

                        if (totalLength < teeThreshold)
                        {
                            // TRƯỜNG HỢP 1: Tổng chiều dài nhánh < 3.5ft -> Xóa toàn bộ nhánh tới vị trí tê (Tee)
                            foreach (MEPCurve p in pipes) elementsToDelete.Add(p.Id);
                            foreach (FamilyInstance f in fittings) elementsToDelete.Add(f.Id);

                            // Tìm đầu kết nối Tê còn trống để kết nối ống mềm trực tiếp vào Tê
                            Connector teeConnector = FindTeeConnector(sprinklerConnector);
                            if (teeConnector != null)
                            {
                                connectionTasks.Add(new FlexConnectionTask
                                {
                                    Sprinkler = sprinkler,
                                    NeedsCoupling = false,
                                    TargetElementId = teeConnector.Owner.Id,
                                    ConnectionPoint = teeConnector.Origin
                                });
                            }
                        }
                        else
                        {
                            // TRƯỜNG HỢP 2: Tổng chiều dài nhánh >= 3.5ft -> Xóa đúng 3.0ft, đặt Khớp nối (Coupling) chỉ khi đoạn còn lại >= 6 inches
                            double remainingBudget = MAX_DELETE_LENGTH_FEET;
                            XYZ connPoint = sprinklerConnector.Origin;

                            for (int i = 0; i < pathInOrder.Count; i++)
                            {
                                if (remainingBudget <= 0.001) break;
                                Element elem = pathInOrder[i];

                                if (elem is Pipe pipe)
                                {
                                    LocationCurve locCurve = pipe.Location as LocationCurve;
                                    Line line = locCurve?.Curve as Line;
                                    if (line == null) continue;

                                    double pipeLen = line.Length;

                                    if (pipeLen >= remainingBudget)
                                    {
                                        double remainingLen = pipeLen - remainingBudget;

                                        XYZ start = line.GetEndPoint(0);
                                        XYZ end = line.GetEndPoint(1);
                                        XYZ dir = (end - start).Normalize();

                                        int shortenEndIndex = start.DistanceTo(connPoint) < end.DistanceTo(connPoint) ? 0 : 1;
                                        XYZ upVector = shortenEndIndex == 0 ? dir : -dir;
                                        XYZ splitPoint = connPoint + upVector * remainingBudget;

                                        if (remainingLen < MIN_REMAINING_PIPE)
                                        {
                                            // Ống còn lại < 6 inches -> Xóa toàn bộ ống này
                                            elementsToDelete.Add(pipe.Id);

                                            // Xác định phần tử kết nối tiếp theo (có thể là phụ kiện tiếp theo hoặc khớp chữ T)
                                            if (i + 1 < pathInOrder.Count)
                                            {
                                                Element nextElem = pathInOrder[i + 1];
                                                XYZ nextConnPoint = GetOtherConnector(pipe, connPoint)?.Origin ?? connPoint;
                                                connectionTasks.Add(new FlexConnectionTask
                                                {
                                                    Sprinkler = sprinkler,
                                                    NeedsCoupling = false,
                                                    TargetElementId = nextElem.Id,
                                                    ConnectionPoint = nextConnPoint
                                                });
                                            }
                                            else
                                            {
                                                // Đã chạm tới Tê (Tee)
                                                Connector teeConnector = FindTeeConnector(sprinklerConnector);
                                                if (teeConnector != null)
                                                {
                                                    connectionTasks.Add(new FlexConnectionTask
                                                    {
                                                        Sprinkler = sprinkler,
                                                        NeedsCoupling = false,
                                                        TargetElementId = teeConnector.Owner.Id,
                                                        ConnectionPoint = teeConnector.Origin
                                                    });
                                                }
                                            }
                                        }
                                        else
                                        {
                                            // Ống còn lại >= 6 inches -> Rút ngắn ống và đặt Măng sông / Khớp nối (Coupling)
                                            if (IsPointOnSegment(splitPoint, start, end))
                                            {
                                                ShortenPipeFromEnd(pipe, connPoint, remainingBudget);

                                                connectionTasks.Add(new FlexConnectionTask
                                                {
                                                    Sprinkler = sprinkler,
                                                    NeedsCoupling = true,
                                                    PipeToKeepId = pipe.Id,
                                                    SplitPoint = splitPoint
                                                });
                                            }
                                        }

                                        remainingBudget = 0;
                                    }
                                    else
                                    {
                                        elementsToDelete.Add(pipe.Id);
                                        remainingBudget -= pipeLen;
                                        connPoint = GetOtherConnector(pipe, connPoint)?.Origin ?? connPoint;
                                    }
                                }
                                else if (elem is FamilyInstance fitting)
                                {
                                    elementsToDelete.Add(fitting.Id);
                                    connPoint = GetOtherConnector(fitting, connPoint)?.Origin ?? connPoint;
                                }
                            }
                        }
                    }

                    // Ngắt kết nối tất cả các đối tượng đã được đánh dấu để xóa
                    foreach (ElementId id in elementsToDelete)
                    {
                        Element elem = doc.GetElement(id);
                        if (elem == null) continue;
                        if (elem is Pipe pipeElem) DisconnectAll(pipeElem);
                        else if (elem is FamilyInstance fiElem) DisconnectAll(fiElem);
                    }

                    // Thực hiện xóa các đối tượng
                    HashSet<ElementId> uniqueDeleteIds = new HashSet<ElementId>(elementsToDelete);
                    foreach (ElementId id in uniqueDeleteIds)
                    {
                        try { doc.Delete(id); } catch { }
                    }

                    // Tái tạo tài liệu để dọn sạch các kết nối đã xóa
                    doc.Regenerate();

                    // ============================================================
                    // BƯỚC 3: Đặt Khớp nối (Coupling) và Kết nối Ống mềm (Flex Pipe)
                    // ============================================================
                    foreach (FlexConnectionTask task in connectionTasks)
                    {
                        Connector sprinklerConnector = GetSprinklerConnector(task.Sprinkler);
                        if (sprinklerConnector == null) continue;

                        Connector targetRigidConnector = null;

                        if (!task.NeedsCoupling)
                        {
                            // Kết nối trực tiếp với Tê hoặc Co/Phụ kiện
                            Element targetElem = doc.GetElement(task.TargetElementId);
                            if (targetElem != null)
                            {
                                targetRigidConnector = GetConnectorNearPoint(targetElem, task.ConnectionPoint);
                            }
                        }
                        else
                        {
                            // Lấy đoạn ống đã rút ngắn và đặt một Măng sông (Coupling) ở đầu ống mở
                            Pipe pipeToKeep = doc.GetElement(task.PipeToKeepId) as Pipe;
                            if (pipeToKeep != null)
                            {
                                Connector openPipeConn = GetConnectorNearPoint(pipeToKeep, task.SplitPoint);
                                if (openPipeConn != null)
                                {
                                    FamilyInstance coupling = PlaceDefaultCouplingAtConnector(doc, openPipeConn);
                                    if (coupling != null)
                                    {
                                        targetRigidConnector = GetOpenConnector(coupling);
                                    }
                                }
                            }
                        }

                        if (targetRigidConnector != null)
                        {
                            // Tạo ống mềm (Flex Pipe) để kết nối đầu ống cứng với đầu phun hướng xuống
                            FlexPipe flex = CreateFlexPipeBetween(
                                doc,
                                flexPipeType,
                                systemType,
                                targetRigidConnector,
                                sprinklerConnector
                            );

                            if (flex != null)
                            {
                                SetPipeDiameter(flex, PIPE_DIAMETER_FEET);
                            }
                        }
                    }

                    // Tái tạo tài liệu để hoàn tất kết nối, sau đó dịch chuyển nhỏ các phụ kiện ống < 2" trong Section Box
                    doc.Regenerate();
                    //ShiftFittingsCommand.ShiftUnder2InchFittings(doc, view3D, sectionBox);

                    trans.Commit();
                }

                TaskDialog.Show("Kết quả", $"HOÀN THÀNH\nĐã xử lý thành công {processedCount} đầu phun Pendent trong Section Box.");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        #region Helpers Section Box

        /// <summary>
        /// Kiểm tra một điểm có nằm trong Section Box hay không.
        /// </summary>
        public static bool IsPointInSectionBox(XYZ point, BoundingBoxXYZ sectionBox, double tolerance = 0.01)
        {
            if (sectionBox == null) return true;
            Transform transform = sectionBox.Transform;
            XYZ localP = transform.Inverse.OfPoint(point);

            return localP.X >= sectionBox.Min.X - tolerance && localP.X <= sectionBox.Max.X + tolerance &&
                   localP.Y >= sectionBox.Min.Y - tolerance && localP.Y <= sectionBox.Max.Y + tolerance &&
                   localP.Z >= sectionBox.Min.Z - tolerance && localP.Z <= sectionBox.Max.Z + tolerance;
        }

        /// <summary>
        /// Kiểm tra một Element có nằm trong Section Box hay không.
        /// </summary>
        public static bool IsElementInSectionBox(Element element, BoundingBoxXYZ sectionBox, double tolerance = 0.01)
        {
            if (element == null) return false;
            if (sectionBox == null) return true;

            if (element.Location is LocationPoint lp)
            {
                return IsPointInSectionBox(lp.Point, sectionBox, tolerance);
            }

            if (element.Location is LocationCurve lc && lc.Curve != null)
            {
                XYZ p0 = lc.Curve.GetEndPoint(0);
                XYZ p1 = lc.Curve.GetEndPoint(1);
                XYZ mid = (p0 + p1) * 0.5;
                return IsPointInSectionBox(p0, sectionBox, tolerance) ||
                       IsPointInSectionBox(p1, sectionBox, tolerance) ||
                       IsPointInSectionBox(mid, sectionBox, tolerance);
            }

            BoundingBoxXYZ bbox = element.get_BoundingBox(null);
            if (bbox != null)
            {
                XYZ center = (bbox.Min + bbox.Max) * 0.5;
                return IsPointInSectionBox(center, sectionBox, tolerance);
            }

            return false;
        }

        #endregion

        #region Truy vết nhánh ống ngược về thượng nguồn (Tee)

        private void TraceBranch(
            Connector startConnector,
            out List<MEPCurve> pipes,
            out List<FamilyInstance> fittings,
            out List<Element> pathInOrder,
            out double totalLength)
        {
            pipes = new List<MEPCurve>();
            fittings = new List<FamilyInstance>();
            pathInOrder = new List<Element>();
            totalLength = 0;

            Connector currentConnector = startConnector;
            int safety = 0;

            while (safety < 30)
            {
                safety++;
                if (!currentConnector.IsConnected) break;

                Connector connectedTo = null;
                foreach (Connector c in currentConnector.AllRefs)
                {
                    if (c.Owner.Id != currentConnector.Owner.Id && c.Domain == Domain.DomainPiping)
                    {
                        connectedTo = c;
                        break;
                    }
                }

                if (connectedTo == null) break;

                Element owner = connectedTo.Owner;
                pathInOrder.Add(owner);

                if (owner is Pipe pipe)
                {
                    pipes.Add(pipe);
                    LocationCurve locCurve = pipe.Location as LocationCurve;
                    if (locCurve != null)
                    {
                        totalLength += locCurve.Curve.Length;
                    }

                    Connector other = GetOtherConnector(pipe, connectedTo.Origin);
                    if (other == null) break;
                    currentConnector = other;
                }
                else if (owner is FamilyInstance fi && fi.Category.Id.Value == (int)BuiltInCategory.OST_PipeFitting)
                {
                    int count = 0;
                    foreach (Connector conn in fi.MEPModel.ConnectorManager.Connectors)
                    {
                        if (conn.Domain == Domain.DomainPiping) count++;
                    }

                    // Dừng lại nếu gặp khớp nối chữ T (Tee), Tap hoặc chữ thập (Cross) (>= 3 đầu kết nối)
                    if (count >= 3)
                    {
                        break;
                    }

                    fittings.Add(fi);

                    Connector other = GetOtherConnector(fi, connectedTo.Origin);
                    if (other == null) break;
                    currentConnector = other;
                }
                else
                {
                    break;
                }
            }
        }

        #endregion

        #region Rút ngắn ống

        private void ShortenPipeFromEnd(Pipe pipe, XYZ connPoint, double lengthToShorten)
        {
            LocationCurve locCurve = pipe.Location as LocationCurve;
            Line line = locCurve?.Curve as Line;
            if (line == null) return;

            XYZ start = line.GetEndPoint(0);
            XYZ end = line.GetEndPoint(1);
            XYZ dir = (end - start).Normalize();

            int shortenEndIndex = start.DistanceTo(connPoint) < end.DistanceTo(connPoint) ? 0 : 1;

            DisconnectConnectorAt(pipe, connPoint);

            XYZ newStart = shortenEndIndex == 0 ? start + dir * lengthToShorten : start;
            XYZ newEnd = shortenEndIndex == 1 ? end - dir * lengthToShorten : end;

            try
            {
                locCurve.Curve = Line.CreateBound(newStart, newEnd);
            }
            catch { }
        }

        #endregion

        #region Đặt Măng sông / Khớp nối mặc định (Union)

        private FamilyInstance PlaceDefaultCouplingAtConnector(Document doc, Connector openConnector)
        {
            if (openConnector == null) return null;

            try
            {
                Pipe pipe = openConnector.Owner as Pipe;
                if (pipe == null) return null;

                XYZ outDir = openConnector.CoordinateSystem.BasisZ.Normalize();
                XYZ tempEndPoint = openConnector.Origin + outDir * 0.5;

                ElementId systemTypeId = pipe.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM).AsElementId();
                ElementId pipeTypeId = pipe.GetTypeId();
                ElementId levelId = pipe.LevelId;

                Pipe dummyPipe = Pipe.Create(doc, systemTypeId, pipeTypeId, levelId, openConnector.Origin, tempEndPoint);
                if (dummyPipe == null) return null;

                double diameter = openConnector.Radius * 2.0;
                Parameter sizeParam = dummyPipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                if (sizeParam != null)
                {
                    sizeParam.Set(diameter);
                }

                Connector dummyStartConnector = null;
                foreach (Connector c in dummyPipe.ConnectorManager.Connectors)
                {
                    if (c.Origin.DistanceTo(openConnector.Origin) < 0.01)
                    {
                        dummyStartConnector = c;
                        break;
                    }
                }

                if (dummyStartConnector != null)
                {
                    FamilyInstance unionFitting = doc.Create.NewUnionFitting(openConnector, dummyStartConnector);
                    doc.Delete(dummyPipe.Id);
                    return unionFitting;
                }
            }
            catch { }
            return null;
        }

        #endregion

        #region Tạo Ống mềm (Flex Pipe)

        private FlexPipe CreateFlexPipeBetween(
            Document doc,
            FlexPipeType flexPipeType,
            PipingSystemType systemType,
            Connector startConnector,
            Connector endConnector)
        {
            List<XYZ> points = GenerateFlexPipePoints(startConnector, endConnector);

            ElementId levelId = ElementId.InvalidElementId;
            if (doc.ActiveView.GenLevel != null)
            {
                levelId = doc.ActiveView.GenLevel.Id;
            }
            else
            {
                if (startConnector.Owner != null)
                    levelId = startConnector.Owner.LevelId;

                if (levelId == ElementId.InvalidElementId && endConnector.Owner != null)
                    levelId = endConnector.Owner.LevelId;

                if (levelId == ElementId.InvalidElementId)
                {
                    Level firstLevel = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .FirstOrDefault();
                    if (firstLevel != null)
                    {
                        levelId = firstLevel.Id;
                    }
                }
            }

            FlexPipe flexPipe = FlexPipe.Create(
                doc,
                systemType.Id,
                flexPipeType.Id,
                levelId,
                points);

            if (flexPipe != null)
            {
                TryConnectFlexPipe(flexPipe, startConnector, endConnector);
            }

            return flexPipe;
        }

        private List<XYZ> GenerateFlexPipePoints(Connector startConn, Connector endConn)
        {
            XYZ start = startConn.Origin;
            XYZ end = endConn.Origin;

            XYZ startDir = GetOutwardDirection(startConn);
            double distance = start.DistanceTo(end);

            double leadLength = Math.Max(0.6, distance * 0.4);
            leadLength = Math.Min(leadLength, distance * 0.75);

            List<XYZ> points = new List<XYZ>();
            points.Add(start);

            XYZ midPoint = start + startDir * leadLength;
            points.Add(midPoint);

            points.Add(end);

            return points;
        }

        private XYZ GetOutwardDirection(Connector connector)
        {
            XYZ zDir = connector.CoordinateSystem.BasisZ.Normalize();
            Element owner = connector.Owner;

            if (owner is FamilyInstance fi)
            {
                LocationPoint lp = fi.Location as LocationPoint;
                if (lp != null)
                {
                    XYZ familyOrigin = lp.Point;
                    XYZ outwardDir = (connector.Origin - familyOrigin).Normalize();
                    if (zDir.DotProduct(outwardDir) < 0)
                    {
                        zDir = zDir.Negate();
                    }
                }
            }

            return zDir;
        }

        private void TryConnectFlexPipe(FlexPipe flexPipe, Connector startConn, Connector endConn)
        {
            try
            {
                ConnectorSet flexConnectors = flexPipe.ConnectorManager.Connectors;

                Connector flexStart = null;
                Connector flexEnd = null;
                double minStartDist = double.MaxValue;
                double minEndDist = double.MaxValue;

                foreach (Connector fc in flexConnectors)
                {
                    if (fc.ConnectorType == ConnectorType.End)
                    {
                        double dStart = fc.Origin.DistanceTo(startConn.Origin);
                        double dEnd = fc.Origin.DistanceTo(endConn.Origin);

                        if (dStart < minStartDist)
                        {
                            minStartDist = dStart;
                            flexStart = fc;
                        }
                        if (dEnd < minEndDist)
                        {
                            minEndDist = dEnd;
                            flexEnd = fc;
                        }
                    }
                }

                if (flexStart != null && !startConn.IsConnected) flexStart.ConnectTo(startConn);
                if (flexEnd != null && !endConn.IsConnected) flexEnd.ConnectTo(endConn);
            }
            catch { }
        }

        #endregion

        #region Các phương thức trợ giúp kết nối và hình học

        private List<FamilyInstance> FindPendentSprinklers(Document doc, View3D view3D, BoundingBoxXYZ sectionBox)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc, view3D.Id);
            List<FamilyInstance> sprinklers = collector
                .OfCategory(BuiltInCategory.OST_Sprinklers)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(s => IsPendentSprinkler(s) && IsElementInSectionBox(s, sectionBox))
                .ToList();

            return sprinklers;
        }

        private bool IsPendentSprinkler(FamilyInstance sprinkler)
        {
            string familyName = sprinkler.Symbol.Family.Name.ToLower();
            string typeName = sprinkler.Symbol.Name.ToLower();

            if (familyName.Contains("pendent") || familyName.Contains("quay xuống") || familyName.Contains("quay xuong") || familyName.Contains("down") ||
                typeName.Contains("pendent") || typeName.Contains("quay xuống") || typeName.Contains("quay xuong") || typeName.Contains("down"))
            {
                return true;
            }

            Connector conn = GetSprinklerConnector(sprinkler);
            if (conn != null)
            {
                XYZ zDir = conn.CoordinateSystem.BasisZ;
                if (zDir.Z > 0.5) return true;
            }

            return false;
        }

        private Connector GetSprinklerConnector(FamilyInstance sprinkler)
        {
            MEPModel mepModel = sprinkler.MEPModel;
            if (mepModel?.ConnectorManager != null)
            {
                foreach (Connector c in mepModel.ConnectorManager.Connectors)
                {
                    if (c.Domain == Domain.DomainPiping) return c;
                }
            }
            return null;
        }

        private Connector GetOtherConnector(Pipe pipe, XYZ connPoint)
        {
            foreach (Connector c in pipe.ConnectorManager.Connectors)
            {
                if (c.Origin.DistanceTo(connPoint) > 0.01) return c;
            }
            return null;
        }

        private Connector GetOtherConnector(FamilyInstance fi, XYZ connPoint)
        {
            if (fi.MEPModel?.ConnectorManager == null) return null;
            foreach (Connector c in fi.MEPModel.ConnectorManager.Connectors)
            {
                if (c.Domain == Domain.DomainPiping && c.Origin.DistanceTo(connPoint) > 0.01) return c;
            }
            return null;
        }

        private Connector GetConnectorNearPoint(Element elem, XYZ point)
        {
            if (elem is Pipe pipe)
            {
                foreach (Connector c in pipe.ConnectorManager.Connectors)
                {
                    if (c.Origin.DistanceTo(point) < 0.1) return c;
                }
            }
            else if (elem is FamilyInstance fi)
            {
                if (fi.MEPModel?.ConnectorManager != null)
                {
                    foreach (Connector c in fi.MEPModel.ConnectorManager.Connectors)
                    {
                        if (c.Domain == Domain.DomainPiping && c.Origin.DistanceTo(point) < 0.1) return c;
                    }
                }
            }
            return null;
        }

        private Connector GetOpenConnector(FamilyInstance fitting)
        {
            if (fitting.MEPModel?.ConnectorManager == null) return null;
            foreach (Connector c in fitting.MEPModel.ConnectorManager.Connectors)
            {
                if (c.Domain == Domain.DomainPiping && !c.IsConnected) return c;
            }
            return null;
        }

        private bool IsPointOnSegment(XYZ point, XYZ start, XYZ end)
        {
            double d1 = start.DistanceTo(point);
            double d2 = point.DistanceTo(end);
            double length = start.DistanceTo(end);
            return Math.Abs(d1 + d2 - length) < 0.01;
        }

        private FlexPipeType FindOrGetFlexPipeType(Document doc)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            List<FlexPipeType> flexTypes = collector
                .OfClass(typeof(FlexPipeType))
                .Cast<FlexPipeType>()
                .ToList();

            if (flexTypes.Count == 0) return null;
            FlexPipeType preferred = flexTypes.FirstOrDefault(t => t.Name.IndexOf("flex", StringComparison.OrdinalIgnoreCase) >= 0);
            return preferred ?? flexTypes.First();
        }

        private PipingSystemType FindPipingSystemType(Document doc)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            List<PipingSystemType> systemTypes = collector
                .OfClass(typeof(PipingSystemType))
                .Cast<PipingSystemType>()
                .ToList();

            if (systemTypes.Count == 0) return null;

            string[] priorityNames = { "Fire Protection", "Wet", "Sprinkler", "Fire Sprinkler", "PCCC" };
            foreach (string name in priorityNames)
            {
                PipingSystemType found = systemTypes.FirstOrDefault(s => s.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                if (found != null) return found;
            }
            return systemTypes.First();
        }

        private void SetPipeDiameter(FlexPipe flexPipe, double diameterFeet)
        {
            try
            {
                Parameter diamParam = flexPipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                if (diamParam != null && !diamParam.IsReadOnly) diamParam.Set(diameterFeet);
            }
            catch { }
        }

        private Connector FindTeeConnector(Connector startConnector)
        {
            Connector currentConnector = startConnector;
            int safety = 0;

            while (safety < 30)
            {
                safety++;
                if (!currentConnector.IsConnected) break;

                Connector connectedTo = null;
                foreach (Connector c in currentConnector.AllRefs)
                {
                    if (c.Owner.Id != currentConnector.Owner.Id && c.Domain == Domain.DomainPiping)
                    {
                        connectedTo = c;
                        break;
                    }
                }

                if (connectedTo == null) break;
                Element owner = connectedTo.Owner;

                if (owner is FamilyInstance fi && fi.Category.Id.Value == (int)BuiltInCategory.OST_PipeFitting)
                {
                    int count = 0;
                    foreach (Connector conn in fi.MEPModel.ConnectorManager.Connectors)
                    {
                        if (conn.Domain == Domain.DomainPiping) count++;
                    }

                    if (count >= 3) return connectedTo;

                    Connector other = GetOtherConnector(fi, connectedTo.Origin);
                    if (other == null) break;
                    currentConnector = other;
                }
                else if (owner is Pipe pipe)
                {
                    Connector other = GetOtherConnector(pipe, connectedTo.Origin);
                    if (other == null) break;
                    currentConnector = other;
                }
                else
                {
                    break;
                }
            }
            return null;
        }

        #endregion

        #region Ngắt kết nối MEP

        private void DisconnectConnectorAt(Pipe pipe, XYZ point)
        {
            foreach (Connector c in pipe.ConnectorManager.Connectors)
            {
                if (c.Origin.DistanceTo(point) < 0.1 && c.IsConnected)
                {
                    foreach (Connector refConn in c.AllRefs)
                    {
                        try { c.DisconnectFrom(refConn); } catch { }
                    }
                }
            }
        }

        private void DisconnectAll(Pipe pipe)
        {
            foreach (Connector c in pipe.ConnectorManager.Connectors)
            {
                if (c.IsConnected)
                {
                    foreach (Connector refConn in c.AllRefs)
                    {
                        try { c.DisconnectFrom(refConn); } catch { }
                    }
                }
            }
        }

        private void DisconnectAll(FamilyInstance fi)
        {
            if (fi.MEPModel?.ConnectorManager != null)
            {
                foreach (Connector c in fi.MEPModel.ConnectorManager.Connectors)
                {
                    if (c.IsConnected)
                    {
                        foreach (Connector refConn in c.AllRefs)
                        {
                            try { c.DisconnectFrom(refConn); } catch { }
                        }
                    }
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// Lớp bổ trợ: Nhiệm vụ kết nối ống mềm
    /// </summary>
    internal class FlexConnectionTask
    {
        public FamilyInstance Sprinkler { get; set; }
        public bool NeedsCoupling { get; set; }
        public ElementId TargetElementId { get; set; }
        public XYZ ConnectionPoint { get; set; }
        public ElementId PipeToKeepId { get; set; }
        public XYZ SplitPoint { get; set; }
    }
}
