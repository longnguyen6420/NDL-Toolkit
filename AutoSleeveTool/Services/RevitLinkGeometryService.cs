using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;

namespace AutoSleeveTool.Services
{
    public class LinkPenetrationInfo
    {
        public Element MepElement { get; set; }
        public Element ObstacleElement { get; set; }
        public XYZ StartPoint { get; set; }
        public XYZ EndPoint { get; set; }
        public XYZ CenterPoint { get; set; }
        public double IntersectLength { get; set; }
        public XYZ Direction { get; set; }
        public double MepWidthOrDiameter { get; set; }
        public double MepHeight { get; set; }
        public bool IsPipe { get; set; }
        public bool IsRound { get; set; }
    }

    public class RevitLinkGeometryService
    {
        private readonly Document _doc;

        public RevitLinkGeometryService(Document doc)
        {
            _doc = doc;
        }

        public List<LinkPenetrationInfo> FindPenetrations(
            bool includePipes,
            bool includeDucts,
            bool checkWalls,
            bool checkColumns,
            bool checkBeams,
            bool checkFloors)
        {
            List<LinkPenetrationInfo> results = new List<LinkPenetrationInfo>();

            // 1. Collect MEP elements (Pipes & Ducts) from current document
            List<Element> mepElements = new List<Element>();
            if (includePipes)
            {
                var pipes = new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_PipeCurves)
                    .WhereElementIsNotElementType()
                    .ToElements();
                mepElements.AddRange(pipes);
            }
            if (includeDucts)
            {
                var ducts = new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_DuctCurves)
                    .WhereElementIsNotElementType()
                    .ToElements();
                mepElements.AddRange(ducts);
            }

            if (mepElements.Count == 0) return results;

            // Geometry options
            Options opt = new Options
            {
                DetailLevel = ViewDetailLevel.Fine,
                ComputeReferences = false
            };

            // Collect Revit Link Instances
            var linkInstances = new FilteredElementCollector(_doc)
                .OfClass(typeof(RevitLinkInstance))
                .WhereElementIsNotElementType()
                .Cast<RevitLinkInstance>();

            foreach (Element mep in mepElements)
            {
                LocationCurve locCurve = mep.Location as LocationCurve;
                if (locCurve == null) continue;

                Curve mepCurve = locCurve.Curve;
                XYZ p0 = mepCurve.GetEndPoint(0);
                XYZ p1 = mepCurve.GetEndPoint(1);
                XYZ dir = (p1 - p0).Normalize();

                // Extend line slightly (1.5 ft) to reliably catch full wall/beam thickness
                Line extendedLine = Line.CreateBound(p0 - dir * 1.5, p1 + dir * 1.5);

                // Extract MEP Dimensions
                double widthOrDia = 0;
                double height = 0;
                bool isPipe = true;
                bool isRound = true;
                GetMepDimensions(mep, out widthOrDia, out height, out isPipe, out isRound);

                // --- A) Check Local Document Obstacles ---
                List<Element> localObstacles = GetLocalClashingObstacles(_doc, mep, checkWalls, checkColumns, checkBeams, checkFloors);
                foreach (Element obstacle in localObstacles)
                {
                    List<Solid> solids = GetElementSolids(obstacle, opt, Transform.Identity);
                    ProcessSolidsIntersection(mep, obstacle, mepCurve, extendedLine, p0, dir, widthOrDia, height, isPipe, isRound, solids, results);
                }

                // --- B) Check Revit Link Documents Obstacles ---
                foreach (RevitLinkInstance linkInst in linkInstances)
                {
                    Document linkDoc = linkInst.GetLinkDocument();
                    if (linkDoc == null) continue;

                    Transform totalTransform = linkInst.GetTotalTransform();
                    List<Element> linkedObstacles = GetObstacleElements(linkDoc, checkWalls, checkColumns, checkBeams, checkFloors);

                    foreach (Element obstacle in linkedObstacles)
                    {
                        List<Solid> solids = GetElementSolids(obstacle, opt, totalTransform);
                        ProcessSolidsIntersection(mep, obstacle, mepCurve, extendedLine, p0, dir, widthOrDia, height, isPipe, isRound, solids, results);
                    }
                }
            }

            return results;
        }

        private List<Element> GetLocalClashingObstacles(Document doc, Element mepElement, bool checkWalls, bool checkColumns, bool checkBeams, bool checkFloors)
        {
            List<Element> result = new List<Element>();
            ElementIntersectsElementFilter filter = new ElementIntersectsElementFilter(mepElement);

            if (checkWalls)
            {
                result.AddRange(new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Walls)
                    .WhereElementIsNotElementType()
                    .WherePasses(filter)
                    .ToElements());
            }
            if (checkColumns)
            {
                result.AddRange(new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Columns)
                    .WhereElementIsNotElementType()
                    .WherePasses(filter)
                    .ToElements());
                result.AddRange(new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralColumns)
                    .WhereElementIsNotElementType()
                    .WherePasses(filter)
                    .ToElements());
            }
            if (checkBeams)
            {
                result.AddRange(new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsNotElementType()
                    .WherePasses(filter)
                    .ToElements());
            }
            if (checkFloors)
            {
                result.AddRange(new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Floors)
                    .WhereElementIsNotElementType()
                    .WherePasses(filter)
                    .ToElements());
            }

            return result;
        }

        private List<Element> GetObstacleElements(Document doc, bool checkWalls, bool checkColumns, bool checkBeams, bool checkFloors)
        {
            List<Element> list = new List<Element>();

            if (checkWalls)
            {
                list.AddRange(new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Walls)
                    .WhereElementIsNotElementType()
                    .ToElements());
            }
            if (checkColumns)
            {
                list.AddRange(new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Columns)
                    .WhereElementIsNotElementType()
                    .ToElements());
                list.AddRange(new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralColumns)
                    .WhereElementIsNotElementType()
                    .ToElements());
            }
            if (checkBeams)
            {
                list.AddRange(new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsNotElementType()
                    .ToElements());
            }
            if (checkFloors)
            {
                list.AddRange(new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Floors)
                    .WhereElementIsNotElementType()
                    .ToElements());
            }

            return list;
        }

        private void ProcessSolidsIntersection(
            Element mep, Element obstacle, Curve mepCurve, Line extendedLine, XYZ p0, XYZ dir,
            double widthOrDia, double height, bool isPipe, bool isRound, List<Solid> solids, List<LinkPenetrationInfo> results)
        {
            foreach (Solid solid in solids)
            {
                if (solid == null || solid.Volume < 1e-5) continue;

                List<XYZ> pts = new List<XYZ>();
                foreach (Face face in solid.Faces)
                {
                    IntersectionResultArray ira;
                    SetComparisonResult res = face.Intersect(extendedLine, out ira);
                    if (res != SetComparisonResult.Disjoint && ira != null && !ira.IsEmpty)
                    {
                        foreach (IntersectionResult ir in ira)
                        {
                            pts.Add(ir.XYZPoint);
                        }
                    }
                }

                // Filter points along curve direction
                List<XYZ> uniquePts = new List<XYZ>();
                foreach (XYZ pt in pts)
                {
                    if (!uniquePts.Exists(u => u.DistanceTo(pt) < 0.01))
                    {
                        uniquePts.Add(pt);
                    }
                }

                if (uniquePts.Count >= 2)
                {
                    uniquePts.Sort((a, b) => a.DistanceTo(p0).CompareTo(b.DistanceTo(p0)));
                    XYZ pEnter = uniquePts[0];
                    XYZ pExit = uniquePts[uniquePts.Count - 1];
                    double thickness = pEnter.DistanceTo(pExit);

                    if (thickness > 0.005) // Minimum 1.5mm wall/beam thickness
                    {
                        XYZ center = (pEnter + pExit) * 0.5;

                        results.Add(new LinkPenetrationInfo
                        {
                            MepElement = mep,
                            ObstacleElement = obstacle,
                            StartPoint = pEnter,
                            EndPoint = pExit,
                            CenterPoint = center,
                            IntersectLength = thickness,
                            Direction = dir,
                            MepWidthOrDiameter = widthOrDia,
                            MepHeight = height,
                            IsPipe = isPipe,
                            IsRound = isRound
                        });
                    }
                }
            }
        }

        private List<Solid> GetElementSolids(Element elem, Options opt, Transform transform)
        {
            List<Solid> solids = new List<Solid>();
            GeometryElement geomElem = elem.get_Geometry(opt);
            if (geomElem == null) return solids;

            bool hasTransform = transform != null && !transform.IsIdentity;

            foreach (GeometryObject obj in geomElem)
            {
                Solid s = obj as Solid;
                if (s != null && s.Volume > 1e-5)
                {
                    if (hasTransform) s = SolidUtils.CreateTransformed(s, transform);
                    solids.Add(s);
                }
                else
                {
                    GeometryInstance gInst = obj as GeometryInstance;
                    if (gInst != null)
                    {
                        GeometryElement instGeom = hasTransform ? gInst.GetInstanceGeometry(transform) : gInst.GetInstanceGeometry();
                        if (instGeom != null)
                        {
                            foreach (GeometryObject instObj in instGeom)
                            {
                                Solid instSolid = instObj as Solid;
                                if (instSolid != null && instSolid.Volume > 1e-5)
                                {
                                    solids.Add(instSolid);
                                }
                            }
                        }
                    }
                }
            }

            return solids;
        }

        private void GetMepDimensions(Element mep, out double widthOrDia, out double height, out bool isPipe, out bool isRound)
        {
            widthOrDia = 0.5; // default 6 inches
            height = 0.5;
            isPipe = mep is Pipe;
            isRound = isPipe;

            Duct duct = mep as Duct;
            if (duct != null)
            {
                isPipe = false;
                Parameter diaParam = duct.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
                if (diaParam != null && diaParam.HasValue)
                {
                    widthOrDia = diaParam.AsDouble();
                    height = widthOrDia; // For round duct, rect sleeve width & height equal diameter
                    isRound = true;
                    return;
                }

                Parameter wParam = duct.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
                Parameter hParam = duct.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);
                if (wParam != null && wParam.HasValue && hParam != null && hParam.HasValue)
                {
                    widthOrDia = wParam.AsDouble();
                    height = hParam.AsDouble();
                    isRound = false;
                    return;
                }
            }

            Pipe pipe = mep as Pipe;
            if (pipe != null)
            {
                isPipe = true;
                isRound = true;
                Parameter diaParam = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_OUTER_DIAMETER);
                if (diaParam == null || !diaParam.HasValue || diaParam.AsDouble() < 1e-4)
                {
                    diaParam = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                }

                if (diaParam != null && diaParam.HasValue)
                {
                    widthOrDia = diaParam.AsDouble();
                    height = widthOrDia;
                }
            }
        }
    }
}
