using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace PipePlaceholderTool.Services
{
    public class CadPipeSegment
    {
        public XYZ Start { get; set; }
        public XYZ End { get; set; }
        public double Length => Start.DistanceTo(End);
    }

    public class CadPipeLineService
    {
        public static List<CadPipeSegment> GetLineSegmentsByLayer(Document doc, ImportInstance cadLink, string layerName)
        {
            List<CadPipeSegment> segments = new List<CadPipeSegment>();
            if (cadLink == null || string.IsNullOrWhiteSpace(layerName)) return segments;

            Options opt = new Options { ComputeReferences = false, IncludeNonVisibleObjects = true };
            GeometryElement geomElem = cadLink.get_Geometry(opt);
            if (geomElem == null) return segments;

            Transform transform = cadLink.GetTotalTransform();

            foreach (GeometryObject obj in geomElem)
            {
                if (obj is GeometryInstance inst)
                {
                    ProcessGeometryElement(doc, inst.GetInstanceGeometry(), layerName, transform, segments);
                    ProcessGeometryElement(doc, inst.GetSymbolGeometry(), layerName, transform.Multiply(inst.Transform), segments);
                }
                else
                {
                    ProcessSingleObject(doc, obj, layerName, transform, segments);
                }
            }

            return DeduplicateSegments(segments);
        }

        private static void ProcessGeometryElement(Document doc, GeometryElement geomElem, string layerName, Transform transform, List<CadPipeSegment> segments)
        {
            if (geomElem == null) return;
            foreach (GeometryObject obj in geomElem)
            {
                if (obj is GeometryInstance subInst)
                {
                    Transform subTransform = transform.Multiply(subInst.Transform);
                    ProcessGeometryElement(doc, subInst.GetInstanceGeometry(), layerName, subTransform, segments);
                }
                else
                {
                    ProcessSingleObject(doc, obj, layerName, transform, segments);
                }
            }
        }

        private static void ProcessSingleObject(Document doc, GeometryObject obj, string layerName, Transform transform, List<CadPipeSegment> segments)
        {
            if (obj == null) return;

            string objLayer = GetObjectLayerName(doc, obj);
            if (string.IsNullOrEmpty(objLayer)) return;

            if (objLayer.Equals(layerName, StringComparison.OrdinalIgnoreCase) ||
                objLayer.EndsWith(layerName, StringComparison.OrdinalIgnoreCase))
            {
                if (obj is Line line)
                {
                    XYZ p1 = transform.OfPoint(line.GetEndPoint(0));
                    XYZ p2 = transform.OfPoint(line.GetEndPoint(1));
                    if (p1.DistanceTo(p2) > 0.1) // > 30mm
                    {
                        segments.Add(new CadPipeSegment { Start = p1, End = p2 });
                    }
                }
                else if (obj is PolyLine poly)
                {
                    var coords = poly.GetCoordinates();
                    for (int i = 0; i < coords.Count - 1; i++)
                    {
                        XYZ p1 = transform.OfPoint(coords[i]);
                        XYZ p2 = transform.OfPoint(coords[i + 1]);
                        if (p1.DistanceTo(p2) > 0.1)
                        {
                            segments.Add(new CadPipeSegment { Start = p1, End = p2 });
                        }
                    }
                }
            }
        }

        private static string GetObjectLayerName(Document doc, GeometryObject obj)
        {
            if (obj.GraphicsStyleId != ElementId.InvalidElementId)
            {
                GraphicsStyle gs = doc.GetElement(obj.GraphicsStyleId) as GraphicsStyle;
                if (gs != null && gs.GraphicsStyleCategory != null)
                {
                    return gs.GraphicsStyleCategory.Name;
                }
            }
            return null;
        }

        public static List<string> GetAllLayerNames(Document doc, ImportInstance cadLink)
        {
            HashSet<string> layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (cadLink == null) return layers.ToList();

            Options opt = new Options { ComputeReferences = false, IncludeNonVisibleObjects = true };
            GeometryElement geomElem = cadLink.get_Geometry(opt);
            if (geomElem != null)
            {
                foreach (GeometryObject obj in geomElem)
                {
                    if (obj is GeometryInstance inst)
                    {
                        CollectLayersFromGeom(doc, inst.GetInstanceGeometry(), layers);
                    }
                    else
                    {
                        string layer = GetObjectLayerName(doc, obj);
                        if (!string.IsNullOrEmpty(layer)) layers.Add(layer);
                    }
                }
            }
            return layers.OrderBy(l => l).ToList();
        }

        private static void CollectLayersFromGeom(Document doc, GeometryElement geomElem, HashSet<string> layers)
        {
            if (geomElem == null) return;
            foreach (GeometryObject obj in geomElem)
            {
                if (obj is GeometryInstance inst)
                {
                    CollectLayersFromGeom(doc, inst.GetInstanceGeometry(), layers);
                }
                else
                {
                    string layer = GetObjectLayerName(doc, obj);
                    if (!string.IsNullOrEmpty(layer)) layers.Add(layer);
                }
            }
        }

        private static List<CadPipeSegment> DeduplicateSegments(List<CadPipeSegment> list)
        {
            List<CadPipeSegment> result = new List<CadPipeSegment>();
            foreach (var seg in list)
            {
                bool duplicate = false;
                foreach (var r in result)
                {
                    if ((r.Start.DistanceTo(seg.Start) < 0.1 && r.End.DistanceTo(seg.End) < 0.1) ||
                        (r.Start.DistanceTo(seg.End) < 0.1 && r.End.DistanceTo(seg.Start) < 0.1))
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate) result.Add(seg);
            }
            return result;
        }

        public static double ParseSizeToFeet(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return 0.328;
            input = input.Trim().Replace("mm", "").Replace("DN", "").Replace("\"", "").Trim();

            if (double.TryParse(input, out double val))
            {
                if (val > 10) return val / 304.8;
                return val / 12.0;
            }
            return 0.328;
        }

        public static double ParseElevationToFeet(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return 0.0;
            input = input.Trim();

            string[] parts = input.Split(new char[] { ' ', '\'', '"', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && double.TryParse(parts[0], out double feet) && double.TryParse(parts[1], out double inches))
            {
                return feet + (inches / 12.0);
            }
            else if (parts.Length == 1 && double.TryParse(parts[0], out double val))
            {
                if (val > 100) return val / 304.8;
                return val;
            }
            return 0.0;
        }
    }
}
