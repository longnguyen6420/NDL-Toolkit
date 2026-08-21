using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Electrical;

namespace NDL.WalkerTool.Services
{
    public class WalkResult
    {
        public List<ElementId> ElementIds { get; set; } = new List<ElementId>();
        public Dictionary<string, int> CategoryCounts { get; set; } = new Dictionary<string, int>();
        public int TotalCount => ElementIds.Count;
    }

    public static class MepWalkerService
    {
        /// <summary>
        /// Traverses the entire physically connected network starting from one or more root elements.
        /// </summary>
        public static WalkResult TraverseConnectedNetwork(IEnumerable<Element> rootElements)
        {
            var result = new WalkResult();
            if (rootElements == null) return result;

            HashSet<ElementId> visited = new HashSet<ElementId>();
            Queue<Element> queue = new Queue<Element>();

            foreach (var elem in rootElements)
            {
                if (elem != null && !visited.Contains(elem.Id))
                {
                    visited.Add(elem.Id);
                    queue.Enqueue(elem);
                }
            }

            while (queue.Count > 0)
            {
                Element current = queue.Dequeue();

                // Track category counts
                string catName = current.Category != null ? current.Category.Name : "Khác";
                if (!result.CategoryCounts.ContainsKey(catName))
                {
                    result.CategoryCounts[catName] = 0;
                }
                result.CategoryCounts[catName]++;

                // Find all physical connectors
                var connectors = GetPhysicalConnectors(current);
                foreach (Connector conn in connectors)
                {
                    if (!conn.IsConnected) continue;

                    // Traverse all connected connector references
                    foreach (Connector refConn in conn.AllRefs)
                    {
                        Element owner = refConn.Owner;
                        if (owner == null) continue;

                        // Skip self
                        if (owner.Id == current.Id) continue;

                        // Skip logical system containers
                        if (owner is PipingSystem || owner is MechanicalSystem || owner is ElectricalSystem)
                            continue;

                        // Skip non-physical connector types
                        if (refConn.ConnectorType == ConnectorType.Logical)
                            continue;

                        if (!visited.Contains(owner.Id))
                        {
                            visited.Add(owner.Id);
                            queue.Enqueue(owner);
                        }
                    }
                }
            }

            result.ElementIds = visited.ToList();
            return result;
        }

        /// <summary>
        /// Retrieves all physical connectors associated with the element.
        /// </summary>
        public static IEnumerable<Connector> GetPhysicalConnectors(Element elem)
        {
            if (elem == null) yield break;

            ConnectorSet connSet = null;

            if (elem is MEPCurve mepCurve)
            {
                connSet = mepCurve.ConnectorManager?.Connectors;
            }
            else if (elem is FamilyInstance fi)
            {
                connSet = fi.MEPModel?.ConnectorManager?.Connectors;
            }
            else if (elem is FabricationPart fp)
            {
                connSet = fp.ConnectorManager?.Connectors;
            }

            if (connSet != null)
            {
                foreach (Connector c in connSet)
                {
                    if (c != null && c.ConnectorType != ConnectorType.Logical)
                    {
                        yield return c;
                    }
                }
            }
        }

        /// <summary>
        /// Checks whether an element is an MEP element having connectors.
        /// </summary>
        public static bool IsMepConnectable(Element elem)
        {
            if (elem == null) return false;

            if (elem is MEPCurve || elem is FabricationPart) return true;

            if (elem is FamilyInstance fi)
            {
                return fi.MEPModel?.ConnectorManager?.Connectors?.Size > 0;
            }

            return false;
        }
    }
}
