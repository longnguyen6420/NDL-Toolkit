using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace NDL.MakeArmTool
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MakeArmCommand : IExternalCommand
    {
        // Khoảng cách dịch đúng 6 inch (0.5 feet = 152.4 mm)
        private const double SHIFT_DISTANCE_INCHES = 6.0;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                IList<Reference> selectedRefs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new PipeSelectionFilter(),
                    "Quét chọn các ống nhánh cần chuyển sang Arm-Over 6\" (Make Arm)...");

                if (selectedRefs == null || selectedRefs.Count == 0)
                    return Result.Cancelled;

                double shiftDistFt = SHIFT_DISTANCE_INCHES / 12.0;

                using (Transaction trans = new Transaction(doc, "NDL Make Arm"))
                {
                    FailureHandlingOptions failOptions = trans.GetFailureHandlingOptions();
                    failOptions.SetFailuresPreprocessor(new WarningSwallower());
                    trans.SetFailureHandlingOptions(failOptions);

                    trans.Start();

                    foreach (Reference r in selectedRefs)
                    {
                        Pipe branchPipe = doc.GetElement(r) as Pipe;
                        if (branchPipe == null) continue;

                        try
                        {
                            Execute3Steps(doc, branchPipe, shiftDistFt);
                        }
                        catch { }
                    }

                    trans.Commit();
                }

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private bool Execute3Steps(Document doc, Pipe branchPipe, double shiftDistFt)
        {
            ElementId pipeTypeId = branchPipe.PipeType.Id;
            ElementId levelId = branchPipe.ReferenceLevel != null ? branchPipe.ReferenceLevel.Id : ElementId.InvalidElementId;
            ElementId systemTypeId = branchPipe.MEPSystem != null ? branchPipe.MEPSystem.GetTypeId() : ElementId.InvalidElementId;
            double diameter = branchPipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsDouble();

            LocationCurve branchLoc = branchPipe.Location as LocationCurve;
            if (branchLoc == null) return false;

            // 1. Nhận diện Tee và Cút 90 cũ
            FamilyInstance oldTee = null;
            FamilyInstance oldElbow = null;
            Pipe mainPipe = null;
            Connector mainBranchConn = null;
            Connector dropBranchConn = null;

            foreach (Connector conn in branchPipe.ConnectorManager.Connectors)
            {
                if (!conn.IsConnected) continue;

                foreach (Connector linked in conn.AllRefs)
                {
                    if (linked.Owner is FamilyInstance fi)
                    {
                        string catName = fi.Category != null ? fi.Category.Name : "";
                        if (catName.Contains("Fitting"))
                        {
                            List<Pipe> otherPipes = GetPipesConnectedToFitting(fi, branchPipe.Id);
                            if (otherPipes.Count >= 1 && (fi.MEPModel.ConnectorManager.Connectors.Size >= 3 || otherPipes.Count >= 2))
                            {
                                oldTee = fi;
                                mainBranchConn = conn;
                                mainPipe = otherPipes[0];
                            }
                            else
                            {
                                oldElbow = fi;
                                dropBranchConn = conn;
                            }
                        }
                    }
                    else if (linked.Owner is Pipe p && p.Id != branchPipe.Id)
                    {
                        mainBranchConn = conn;
                        mainPipe = p;
                    }
                }
            }

            if (mainPipe == null || mainBranchConn == null)
                return false;

            // Tìm connector của ống drop phía dưới
            Connector targetDropConnector = null;
            if (oldElbow != null && oldElbow.MEPModel != null)
            {
                foreach (Connector c in oldElbow.MEPModel.ConnectorManager.Connectors)
                {
                    if (!c.IsConnected) continue;
                    foreach (Connector refC in c.AllRefs)
                    {
                        if (refC.Owner.Id != branchPipe.Id && refC.Owner.Id != oldElbow.Id)
                        {
                            targetDropConnector = refC;
                            break;
                        }
                    }
                }
            }

            if (targetDropConnector == null) return false;

            LocationCurve mainLoc = mainPipe.Location as LocationCurve;
            XYZ mainDir = (mainLoc.Curve.GetEndPoint(1) - mainLoc.Curve.GetEndPoint(0)).Normalize();

            // Tọa độ đỉnh ống drop
            XYZ branchP0 = branchLoc.Curve.GetEndPoint(0);
            XYZ ptDropTop = new XYZ(targetDropConnector.Origin.X, targetDropConnector.Origin.Y, branchP0.Z);

            // BƯỚC 1: Xóa cút 90 độ cũ ở đầu phun
            if (oldElbow != null)
            {
                doc.Delete(oldElbow.Id);
                doc.Regenerate();
            }

            // Thử thực hiện Bước 2 và Bước 3 (+6" hoặc -6")
            bool ok = TryStep2And3(doc, branchPipe, oldTee, targetDropConnector, ptDropTop, mainDir, shiftDistFt, 1.0,
                                   systemTypeId, pipeTypeId, levelId, diameter);

            if (!ok)
            {
                TryStep2And3(doc, branchPipe, oldTee, targetDropConnector, ptDropTop, mainDir, shiftDistFt, -1.0,
                             systemTypeId, pipeTypeId, levelId, diameter);
            }

            return true;
        }

        private bool TryStep2And3(Document doc, Pipe branchPipe, FamilyInstance oldTee,
                                  Connector targetDropConnector, XYZ ptDropTop, XYZ mainDir,
                                  double shiftDistFt, double sign,
                                  ElementId systemTypeId, ElementId pipeTypeId, ElementId levelId, double diameter)
        {
            SubTransaction subTrans = new SubTransaction(doc);
            subTrans.Start();

            try
            {
                XYZ offsetVec = (mainDir * shiftDistFt) * sign;

                // BƯỚC 2: Dịch ống nhánh và Tee dọc theo ống chính 6 inch
                List<ElementId> elementsToMove = new List<ElementId> { branchPipe.Id };
                if (oldTee != null)
                {
                    elementsToMove.Add(oldTee.Id);
                }

                // Dịch chuyển đồng thời cả Branch Pipe và Tee để giữ nguyên kết nối với ống chính
                ElementTransformUtils.MoveElements(doc, elementsToMove, offsetVec);
                doc.Regenerate();

                // BƯỚC 3: Thêm ống ngang 6 inch nối ống nhánh với ống drop và bổ sung 2 cút 90 độ
                XYZ ptTurn = ptDropTop + offsetVec;

                // Tạo đoạn ống ngang 6 inch từ điểm uốn về đỉnh ống drop
                Pipe armSegment6Inch = Pipe.Create(doc, systemTypeId, pipeTypeId, levelId, ptTurn, ptDropTop);
                armSegment6Inch.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).Set(diameter);
                doc.Regenerate();

                // Cút 90 độ ngang tại điểm uốn (nối ống nhánh và đoạn 6 inch)
                Connector branchEndConn = GetClosestConnector(branchPipe, ptTurn);
                Connector armStartConn = GetClosestConnector(armSegment6Inch, ptTurn);

                if (branchEndConn == null || armStartConn == null)
                {
                    subTrans.RollBack();
                    return false;
                }

                FamilyInstance elbow1 = doc.Create.NewElbowFitting(branchEndConn, armStartConn);
                if (elbow1 == null)
                {
                    subTrans.RollBack();
                    return false;
                }
                doc.Regenerate();

                // Cút 90 độ tại đầu ống drop (nối đoạn 6 inch và ống drop)
                Connector armEndConn = GetClosestConnector(armSegment6Inch, ptDropTop);
                if (armEndConn == null)
                {
                    subTrans.RollBack();
                    return false;
                }

                FamilyInstance elbow2 = doc.Create.NewElbowFitting(armEndConn, targetDropConnector);
                if (elbow2 == null)
                {
                    subTrans.RollBack();
                    return false;
                }

                doc.Regenerate();
                subTrans.Commit();
                return true;
            }
            catch
            {
                subTrans.RollBack();
                return false;
            }
        }

        private List<Pipe> GetPipesConnectedToFitting(FamilyInstance fitting, ElementId excludePipeId)
        {
            List<Pipe> pipes = new List<Pipe>();
            if (fitting.MEPModel == null) return pipes;

            foreach (Connector c in fitting.MEPModel.ConnectorManager.Connectors)
            {
                if (!c.IsConnected) continue;
                foreach (Connector refC in c.AllRefs)
                {
                    if (refC.Owner is Pipe p && p.Id != excludePipeId && p.Id != fitting.Id)
                    {
                        if (!pipes.Any(x => x.Id == p.Id))
                            pipes.Add(p);
                    }
                }
            }
            return pipes;
        }

        private Connector GetClosestConnector(Pipe pipe, XYZ point)
        {
            Connector closest = null;
            double minDist = double.MaxValue;
            foreach (Connector c in pipe.ConnectorManager.Connectors)
            {
                double dist = c.Origin.DistanceTo(point);
                if (dist < minDist)
                {
                    minDist = dist;
                    closest = c;
                }
            }
            return closest;
        }
    }

    public class PipeSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem is Pipe;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }

    public class WarningSwallower : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            IList<FailureMessageAccessor> failList = failuresAccessor.GetFailureMessages();
            foreach (FailureMessageAccessor f in failList)
            {
                if (f.GetSeverity() == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(f);
                }
            }
            return FailureProcessingResult.Continue;
        }
    }
}
