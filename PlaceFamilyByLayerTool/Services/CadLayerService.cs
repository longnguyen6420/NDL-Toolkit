using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace PlaceFamilyByLayerTool.Services
{
    public class CadLayerService
    {
        /// <summary>
        /// Lấy các điểm chèn đã gộp cụm (Clustering Deduplication) theo Layer Name và Dung sai khoảng cách
        /// </summary>
        public static List<XYZ> GetClusteredInsertionPoints(Document doc, ImportInstance cadLink, string layerName, double toleranceMm)
        {
            List<XYZ> rawPoints = GetRawPointsByLayer(doc, cadLink, layerName);
            if (rawPoints == null || rawPoints.Count == 0) return new List<XYZ>();

            double toleranceFeet = toleranceMm / 304.8;
            if (toleranceFeet <= 0) toleranceFeet = 0.5; // Mặc định 150mm

            // Thuật toán Gom cụm điểm (Clustering)
            List<List<XYZ>> clusters = new List<List<XYZ>>();

            foreach (XYZ pt in rawPoints)
            {
                bool addedToCluster = false;
                foreach (var cluster in clusters)
                {
                    XYZ centroid = GetCentroid(cluster);
                    if (centroid.DistanceTo(pt) <= toleranceFeet)
                    {
                        cluster.Add(pt);
                        addedToCluster = true;
                        break;
                    }
                }

                if (!addedToCluster)
                {
                    clusters.Add(new List<XYZ> { pt });
                }
            }

            // Trả về Trọng tâm (Centroid) của từng cụm (Mỗi cụm chèn đúng 1 Family)
            List<XYZ> resultPoints = new List<XYZ>();
            foreach (var cluster in clusters)
            {
                resultPoints.Add(GetCentroid(cluster));
            }

            return resultPoints;
        }

        private static List<XYZ> GetRawPointsByLayer(Document doc, ImportInstance cadLink, string layerName)
        {
            List<XYZ> points = new List<XYZ>();
            if (cadLink == null || string.IsNullOrWhiteSpace(layerName)) return points;

            Options opt = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = true
            };

            GeometryElement geomElem = cadLink.get_Geometry(opt);
            if (geomElem == null) return points;

            Transform transform = cadLink.GetTotalTransform();

            foreach (GeometryObject obj in geomElem)
            {
                if (obj is GeometryInstance inst)
                {
                    // Lấy điểm từ Block Reference Instance
                    ProcessGeometryElement(doc, inst.GetInstanceGeometry(), layerName, transform, points);
                    ProcessGeometryElement(doc, inst.GetSymbolGeometry(), layerName, transform.Multiply(inst.Transform), points);
                }
                else
                {
                    ProcessSingleGeometryObject(doc, obj, layerName, transform, points);
                }
            }

            return points;
        }

        private static void ProcessGeometryElement(Document doc, GeometryElement geomElem, string layerName, Transform transform, List<XYZ> points)
        {
            if (geomElem == null) return;

            foreach (GeometryObject obj in geomElem)
            {
                if (obj is GeometryInstance subInst)
                {
                    Transform subTransform = transform.Multiply(subInst.Transform);
                    ProcessGeometryElement(doc, subInst.GetInstanceGeometry(), layerName, subTransform, points);
                }
                else
                {
                    ProcessSingleGeometryObject(doc, obj, layerName, transform, points);
                }
            }
        }

        private static void ProcessSingleGeometryObject(Document doc, GeometryObject obj, string layerName, Transform transform, List<XYZ> points)
        {
            if (obj == null) return;

            string objLayer = GetObjectLayerName(doc, obj);
            if (string.IsNullOrEmpty(objLayer)) return;

            if (objLayer.Equals(layerName, StringComparison.OrdinalIgnoreCase) ||
                objLayer.EndsWith(layerName, StringComparison.OrdinalIgnoreCase))
            {
                XYZ pt = GetObjectInsertionPoint(obj);
                if (pt != null)
                {
                    XYZ transformedPt = transform.OfPoint(pt);
                    points.Add(transformedPt);
                }
            }
        }

        public static string GetObjectLayerName(Document doc, GeometryObject obj)
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

        private static XYZ GetObjectInsertionPoint(GeometryObject obj)
        {
            if (obj is Point pt) return pt.Coord;
            if (obj is Arc arc) return arc.Center;
            if (obj is Ellipse ellipse) return ellipse.Center;
            if (obj is Line line) return (line.GetEndPoint(0) + line.GetEndPoint(1)) * 0.5;
            if (obj is PolyLine poly) return poly.GetCoordinate(0);
            if (obj is Mesh mesh && mesh.Vertices.Count > 0) return mesh.Vertices[0];
            return null;
        }

        private static XYZ GetCentroid(List<XYZ> points)
        {
            if (points == null || points.Count == 0) return XYZ.Zero;
            double sumX = points.Sum(p => p.X);
            double sumY = points.Sum(p => p.Y);
            double sumZ = points.Sum(p => p.Z);
            return new XYZ(sumX / points.Count, sumY / points.Count, sumZ / points.Count);
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
                if (val > 100) return val / 304.8; // mm -> feet
                return val; // feet
            }
            return 0.0;
        }
    }
}
