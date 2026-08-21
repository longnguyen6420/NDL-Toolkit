using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace CreateRevitTeeTool.Services
{
    public class PipeTeeService
    {
        private const double DefaultBranchLengthFeet = 1.0; // 300 mm
        private const double MinDistanceFromEndpointFeet = 0.2; // ~60 mm

        /// <summary>
        /// Tạo một Tê fitting tại vị trí pickedPoint trên ống pipe
        /// </summary>
        public static FamilyInstance CreateTeeAtPoint(Document doc, Pipe pipe, XYZ pickedPoint, string orientationMode = "horizontal")
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (pipe == null) throw new ArgumentNullException(nameof(pipe));

            LocationCurve locCurve = pipe.Location as LocationCurve;
            if (locCurve == null) throw new InvalidOperationException("Đối tượng chọn không phải là ống có LocationCurve!");

            Curve curve = locCurve.Curve;
            XYZ p0 = curve.GetEndPoint(0);
            XYZ p1 = curve.GetEndPoint(1);

            // Project picked point onto pipe center line
            IntersectionResult proj = curve.Project(pickedPoint);
            XYZ splitPoint = proj.XYZPoint;

            // Check distance from endpoints
            double d0 = p0.DistanceTo(splitPoint);
            double d1 = p1.DistanceTo(splitPoint);

            if (d0 < MinDistanceFromEndpointFeet || d1 < MinDistanceFromEndpointFeet)
            {
                throw new InvalidOperationException("Vị trí chọn quá gần đầu/cuối đường ống (yêu cầu tối thiểu 60mm)!");
            }

            XYZ pipeDir = (p1 - p0).Normalize();

            // 1. Break the curve into 2 pipes
            ElementId newPipeId = PlumbingUtils.BreakCurve(doc, pipe.Id, splitPoint);
            doc.Regenerate();

            Pipe pipe1 = pipe;
            Pipe pipe2 = doc.GetElement(newPipeId) as Pipe;

            if (pipe2 == null)
            {
                throw new InvalidOperationException("Không thể chia ống tại vị trí này!");
            }

            // 2. Find connectors at splitPoint
            Connector conn1 = GetConnectorAtPoint(pipe1, splitPoint, 0.1);
            Connector conn2 = GetConnectorAtPoint(pipe2, splitPoint, 0.1);

            if (conn1 == null || conn2 == null)
            {
                throw new InvalidOperationException("Không tìm thấy connector sau khi chia ống!");
            }

            // 3. Compute branch direction
            XYZ branchDir = CalculateBranchDirection(pipeDir, orientationMode);
            XYZ branchEndPoint = splitPoint + (branchDir * DefaultBranchLengthFeet);

            // Retrieve parameters from main pipe
            ElementId systemTypeId = pipe1.MEPSystem != null 
                ? pipe1.MEPSystem.GetTypeId() 
                : pipe1.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM).AsElementId();

            ElementId pipeTypeId = pipe1.PipeType.Id;
            ElementId levelId = pipe1.LevelId;
            double diameter = pipe1.Diameter;

            // 4. Create branch stub pipe
            Pipe branchPipe = Pipe.Create(doc, systemTypeId, pipeTypeId, levelId, splitPoint, branchEndPoint);
            Parameter diamParam = branchPipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            if (diamParam != null && !diamParam.IsReadOnly)
            {
                diamParam.Set(diameter);
            }
            doc.Regenerate();

            // 5. Get branch connector at splitPoint
            Connector connBranch = GetConnectorAtPoint(branchPipe, splitPoint, 0.1);

            if (connBranch == null)
            {
                throw new InvalidOperationException("Không tìm thấy connector của đoạn ống nhánh!");
            }

            // 6. Create Tee fitting
            FamilyInstance teeFitting = doc.Create.NewTeeFitting(conn1, conn2, connBranch);

            return teeFitting;
        }

        private static Connector GetConnectorAtPoint(Element elem, XYZ point, double tolerance)
        {
            ConnectorManager cm = null;
            if (elem is MEPCurve mepCurve)
            {
                cm = mepCurve.ConnectorManager;
            }
            else if (elem is FamilyInstance fi && fi.MEPModel != null)
            {
                cm = fi.MEPModel.ConnectorManager;
            }

            if (cm == null) return null;

            Connector closest = null;
            double minDist = double.MaxValue;

            foreach (Connector conn in cm.Connectors)
            {
                double dist = conn.Origin.DistanceTo(point);
                if (dist < minDist)
                {
                    minDist = dist;
                    closest = conn;
                }
            }

            return minDist <= tolerance ? closest : null;
        }

        private static XYZ CalculateBranchDirection(XYZ pipeDir, string orientationMode)
        {
            pipeDir = pipeDir.Normalize();
            XYZ zAxis = XYZ.BasisZ;

            bool isVertical = Math.Abs(Math.Abs(pipeDir.DotProduct(zAxis)) - 1.0) < 1e-3;

            XYZ branchDir;
            if (isVertical)
            {
                branchDir = pipeDir.CrossProduct(XYZ.BasisX);
                if (branchDir.IsZeroLength())
                {
                    branchDir = pipeDir.CrossProduct(XYZ.BasisY);
                }
            }
            else
            {
                if (orientationMode.Equals("up", StringComparison.OrdinalIgnoreCase))
                {
                    branchDir = zAxis;
                }
                else if (orientationMode.Equals("down", StringComparison.OrdinalIgnoreCase))
                {
                    branchDir = -zAxis;
                }
                else
                {
                    branchDir = pipeDir.CrossProduct(zAxis);
                }
            }

            return branchDir.Normalize();
        }
    }
}
