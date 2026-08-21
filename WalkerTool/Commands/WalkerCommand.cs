using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using NDL.WalkerTool.Services;

namespace NDL.WalkerTool.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class WalkerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null) return Result.Cancelled;

            Document doc = uidoc.Document;
            List<Element> startElements = new List<Element>();

            // 1. Check Pre-selection
            ICollection<ElementId> currentSelectedIds = uidoc.Selection.GetElementIds();
            if (currentSelectedIds != null && currentSelectedIds.Count > 0)
            {
                foreach (ElementId id in currentSelectedIds)
                {
                    Element elem = doc.GetElement(id);
                    if (elem != null && MepWalkerService.IsMepConnectable(elem))
                    {
                        startElements.Add(elem);
                    }
                }
            }

            // 2. If no pre-selection, prompt user to pick 1 MEP element
            if (startElements.Count == 0)
            {
                try
                {
                    Reference pickedRef = uidoc.Selection.PickObject(
                        ObjectType.Element,
                        new MepSelectionFilter(),
                        "NDL Walker: Click chọn 1 ống hoặc phụ kiện để chọn toàn bộ hệ thống nối với nó:");

                    if (pickedRef != null)
                    {
                        Element pickedElem = doc.GetElement(pickedRef);
                        if (pickedElem != null)
                        {
                            startElements.Add(pickedElem);
                        }
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }
            }

            if (startElements.Count == 0)
            {
                TaskDialog.Show("NDL Walker", "Không có đối tượng MEP hợp lệ nào được chọn.");
                return Result.Cancelled;
            }

            // 3. Traverse entire physically connected network
            WalkResult walkResult = MepWalkerService.TraverseConnectedNetwork(startElements);

            if (walkResult.TotalCount == 0)
            {
                TaskDialog.Show("NDL Walker", "Không tìm thấy kết nối nào từ đối tượng đã chọn.");
                return Result.Cancelled;
            }

            // 4. Update Revit UI Selection
            uidoc.Selection.SetElementIds(walkResult.ElementIds);

            return Result.Succeeded;
        }

        private class MepSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return MepWalkerService.IsMepConnectable(elem);
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }
    }
}
