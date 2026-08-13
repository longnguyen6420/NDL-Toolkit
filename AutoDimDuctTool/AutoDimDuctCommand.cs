using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace AutoDimDuctTool
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoDimDuctCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            // 1. Kiểm tra loại View (Phải là View 2D)
            ViewType vt = activeView.ViewType;
            if (vt != ViewType.FloorPlan && vt != ViewType.CeilingPlan &&
                vt != ViewType.EngineeringPlan && vt != ViewType.Section && vt != ViewType.Elevation)
            {
                TaskDialog.Show("AutoDim Duct 2D", "Chức năng này chỉ hỗ trợ trên View 2D (Mặt bằng, Mặt cắt, Mặt đứng).");
                return Result.Cancelled;
            }

            // 2. Ưu tiên lấy các Duct đã chọn trước trên mặt bằng
            ICollection<ElementId> selectedIds = uidoc.Selection.GetElementIds();
            List<MEPCurve> ducts = new List<MEPCurve>();

            if (selectedIds != null && selectedIds.Count > 0)
            {
                foreach (ElementId id in selectedIds)
                {
                    Element el = doc.GetElement(id);
                    if (el == null) continue;

                    if (el is Duct || el is MEPCurve)
                    {
                        ducts.Add(el as MEPCurve);
                    }
                    else if (el is DuctInsulation insulation)
                    {
                        Element host = doc.GetElement(insulation.HostElementId);
                        if (host is MEPCurve hostDuct && !ducts.Contains(hostDuct))
                        {
                            ducts.Add(hostDuct);
                        }
                    }
                }
            }

            // Nếu chưa chọn trước -> Cho phép quét chọn tương tác
            if (ducts.Count == 0)
            {
                try
                {
                    IList<Reference> pickedRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        new DuctSelectionFilter(),
                        "Quét chọn các đoạn ống gió (Duct) trên mặt bằng cần dim 2 chiều:");

                    foreach (Reference r in pickedRefs)
                    {
                        MEPCurve d = doc.GetElement(r) as MEPCurve;
                        if (d != null && !ducts.Contains(d))
                        {
                            ducts.Add(d);
                        }
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }
            }

            if (ducts.Count == 0)
            {
                TaskDialog.Show("AutoDim Duct 2D", "Chưa chọn đoạn ống gió nào.");
                return Result.Cancelled;
            }

            // 3. LẤY TẤT CẢ GRID TỪ CẢ HOST DOCUMENT VÀ REVIT LINK
            List<GridData> allGridData = new List<GridData>();
            int hostGridCount = 0;
            int linkedGridCount = 0;

            // 3a. Host Grids
            List<Grid> hostGrids = new FilteredElementCollector(doc, activeView.Id)
                .OfClass(typeof(Grid))
                .WhereElementIsNotElementType()
                .Cast<Grid>()
                .ToList();

            foreach (Grid g in hostGrids)
            {
                Line gLine = g.Curve as Line;
                if (gLine != null)
                {
                    XYZ dir = (gLine.GetEndPoint(1) - gLine.GetEndPoint(0)).Normalize();
                    Reference gRef = GetGridReference(g, gLine);
                    if (gRef != null)
                    {
                        allGridData.Add(new GridData { Line = gLine, Direction = dir, Reference = gRef });
                        hostGridCount++;
                    }
                }
            }

            // 3b. Linked Grids
            List<RevitLinkInstance> linkInstances = new FilteredElementCollector(doc, activeView.Id)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            foreach (RevitLinkInstance linkInst in linkInstances)
            {
                Document linkDoc = linkInst.GetLinkDocument();
                if (linkDoc == null) continue;

                Transform linkTransform = linkInst.GetTotalTransform();

                List<Grid> linkedGrids = new FilteredElementCollector(linkDoc)
                    .OfClass(typeof(Grid))
                    .WhereElementIsNotElementType()
                    .Cast<Grid>()
                    .ToList();

                foreach (Grid g in linkedGrids)
                {
                    Line gLine = g.Curve as Line;
                    if (gLine != null)
                    {
                        Line transformedLine = gLine.CreateTransformed(linkTransform) as Line;
                        if (transformedLine != null)
                        {
                            XYZ dir = (transformedLine.GetEndPoint(1) - transformedLine.GetEndPoint(0)).Normalize();
                            Reference gRefLocal = GetGridReference(g, gLine);
                            if (gRefLocal != null)
                            {
                                try
                                {
                                    Reference gRefLinked = gRefLocal.CreateLinkReference(linkInst);
                                    if (gRefLinked != null)
                                    {
                                        allGridData.Add(new GridData { Line = transformedLine, Direction = dir, Reference = gRefLinked });
                                        linkedGridCount++;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine("Lỗi Link Grid Ref: " + ex.Message);
                                }
                            }
                        }
                    }
                }
            }

            if (allGridData.Count == 0)
            {
                TaskDialog.Show("AutoDim Duct 2D", "Không tìm thấy Lưới trục (Grid) nào trong cả File Host và File Revit Link!");
                return Result.Cancelled;
            }

            // 4. PHÂN LOẠI GRID THEO 2 PHƯƠNG: PHƯƠNG X (DỌC) VÀ PHƯƠNG Y (NGANG)
            XYZ rightDir = activeView.RightDirection.Normalize(); // Vector phương X trong View
            XYZ upDir = activeView.UpDirection.Normalize();       // Vector phương Y trong View

            List<GridData> xGrids = new List<GridData>(); // Lưới trục đứng (Vuông góc X) -> Dim phương X
            List<GridData> yGrids = new List<GridData>(); // Lưới trục ngang (Vuông góc Y) -> Dim phương Y

            foreach (var gData in allGridData)
            {
                if (IsParallel(gData.Direction, upDir))
                {
                    xGrids.Add(gData);
                }
                else if (IsParallel(gData.Direction, rightDir))
                {
                    yGrids.Add(gData);
                }
                else
                {
                    double dotUp = Math.Abs(gData.Direction.DotProduct(upDir));
                    if (dotUp > 0.707) xGrids.Add(gData);
                    else yGrids.Add(gData);
                }
            }

            int dimXSuccess = 0;
            int dimYSuccess = 0;

            // 5. THỰC HIỆN DIM 2 CHIỀU TRONG TRANSACTION
            using (Transaction tx = new Transaction(doc, "AutoDim Ducts 2 Dimensions (X & Y)"))
            {
                tx.Start();

                foreach (MEPCurve duct in ducts)
                {
                    LocationCurve locCurve = duct.Location as LocationCurve;
                    if (locCurve == null) continue;

                    Line ductLine = locCurve.Curve as Line;
                    if (ductLine == null) continue;

                    XYZ p1 = ductLine.GetEndPoint(0);
                    XYZ p2 = ductLine.GetEndPoint(1);
                    XYZ midPoint = (p1 + p2) * 0.5;

                    // Lấy các Reference của Duct
                    Reference ductCenterlineRef = GetDuctCenterlineReference(duct, activeView);
                    Reference startPointRef = GetCurveEndPointRef(ductLine, 0);
                    Reference endPointRef = GetCurveEndPointRef(ductLine, 1);

                    // -------------------------------------------------------------
                    // 5A. CHIỀU 1: DIM PHƯƠNG X (Từ Duct đến Lưới trục Đứng gần nhất)
                    // -------------------------------------------------------------
                    if (xGrids.Count > 0)
                    {
                        var nearestXGrid = xGrids
                            .Select(g => new {
                                GridData = g,
                                Dist2D = Math.Abs((midPoint - g.Line.GetEndPoint(0)).DotProduct(rightDir))
                            })
                            .OrderBy(g => g.Dist2D)
                            .FirstOrDefault();

                        if (nearestXGrid != null)
                        {
                            XYZ dimStart = midPoint - rightDir * 10.0;
                            XYZ dimEnd = midPoint + rightDir * 10.0;
                            Line dimLineX = Line.CreateBound(dimStart, dimEnd);

                            bool okX = TryCreateDimension(doc, activeView, dimLineX, nearestXGrid.GridData.Reference, ductCenterlineRef, startPointRef, endPointRef);
                            if (okX) dimXSuccess++;
                        }
                    }

                    // -------------------------------------------------------------
                    // 5B. CHIỀU 2: DIM PHƯƠNG Y (Từ Duct đến Lưới trục Ngang gần nhất)
                    // -------------------------------------------------------------
                    if (yGrids.Count > 0)
                    {
                        var nearestYGrid = yGrids
                            .Select(g => new {
                                GridData = g,
                                Dist2D = Math.Abs((midPoint - g.Line.GetEndPoint(0)).DotProduct(upDir))
                            })
                            .OrderBy(g => g.Dist2D)
                            .FirstOrDefault();

                        if (nearestYGrid != null)
                        {
                            XYZ dimStart = midPoint - upDir * 10.0;
                            XYZ dimEnd = midPoint + upDir * 10.0;
                            Line dimLineY = Line.CreateBound(dimStart, dimEnd);

                            bool okY = TryCreateDimension(doc, activeView, dimLineY, nearestYGrid.GridData.Reference, ductCenterlineRef, startPointRef, endPointRef);
                            if (okY) dimYSuccess++;
                        }
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("AutoDim Duct 2D (NDL)",
                $"Hoàn thành AutoDim 2 Chiều (X & Y)!\n" +
                $"- Số lượng ống gió xử lý: {ducts.Count}\n" +
                $"- Đã tạo thành công: {dimXSuccess} dim phương X và {dimYSuccess} dim phương Y.\n" +
                $"- Thống kê Grid: {hostGridCount} Host Grids, {linkedGridCount} Revit Link Grids.");

            return Result.Succeeded;
        }

        private Reference GetCurveEndPointRef(Curve curve, int index)
        {
            try
            {
                return curve.GetEndPointReference(index);
            }
            catch { }
            return null;
        }

        private bool TryCreateDimension(Document doc, View view, Line dimLine, Reference gridRef, Reference centerlineRef, Reference startPointRef, Reference endPointRef)
        {
            if (centerlineRef != null)
            {
                try
                {
                    ReferenceArray refArray = new ReferenceArray();
                    refArray.Append(gridRef);
                    refArray.Append(centerlineRef);
                    doc.Create.NewDimension(view, dimLine, refArray);
                    return true;
                }
                catch { }
            }

            if (startPointRef != null)
            {
                try
                {
                    ReferenceArray refArray = new ReferenceArray();
                    refArray.Append(gridRef);
                    refArray.Append(startPointRef);
                    doc.Create.NewDimension(view, dimLine, refArray);
                    return true;
                }
                catch { }
            }

            if (endPointRef != null)
            {
                try
                {
                    ReferenceArray refArray = new ReferenceArray();
                    refArray.Append(gridRef);
                    refArray.Append(endPointRef);
                    doc.Create.NewDimension(view, dimLine, refArray);
                    return true;
                }
                catch { }
            }

            return false;
        }

        private Reference GetGridReference(Grid grid, Line gridLine)
        {
            try { return new Reference(grid); }
            catch { return gridLine.Reference; }
        }

        private Reference GetDuctCenterlineReference(MEPCurve duct, View view)
        {
            Options opt = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = false,
                View = view
            };

            GeometryElement geomElem = duct.get_Geometry(opt);
            if (geomElem != null)
            {
                foreach (GeometryObject obj in geomElem)
                {
                    if (obj is Line line && line.Reference != null)
                    {
                        return line.Reference;
                    }
                    else if (obj is GeometryInstance inst)
                    {
                        foreach (GeometryObject instObj in inst.GetInstanceGeometry())
                        {
                            if (instObj is Line instLine && instLine.Reference != null)
                            {
                                return instLine.Reference;
                            }
                        }
                    }
                }
            }

            try { return new Reference(duct); }
            catch { return null; }
        }

        private bool IsParallel(XYZ v1, XYZ v2, double tolerance = 0.01)
        {
            return v1.CrossProduct(v2).GetLength() < tolerance;
        }

        private class GridData
        {
            public Line Line { get; set; }
            public XYZ Direction { get; set; }
            public Reference Reference { get; set; }
        }

        private class DuctSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem is Duct || elem is MEPCurve;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }
    }
}
