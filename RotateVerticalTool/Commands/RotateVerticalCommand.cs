using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using RotateVerticalTool.Services;
using RotateVerticalTool.UI;

namespace RotateVerticalTool.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RotateVerticalCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            List<ElementId> selectedElementIds = new List<ElementId>();

            // 1. BƯỚC 1: Kiểm tra Pre-selection đối tượng cần xoay
            ICollection<ElementId> currentSelectedIds = uidoc.Selection.GetElementIds();
            if (currentSelectedIds != null && currentSelectedIds.Count > 0)
            {
                selectedElementIds = currentSelectedIds.ToList();
            }

            // Nếu chưa chọn trước -> Yêu cầu người dùng quét chọn các đối tượng cần xoay trên View 3D
            if (selectedElementIds.Count == 0)
            {
                try
                {
                    IList<Reference> pickedRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        "BƯỚC 1: Quét chọn cụm đối tượng (Pipes / Fittings / Sprinklers) cần xoay trong View 3D:");

                    if (pickedRefs == null || pickedRefs.Count == 0) return Result.Cancelled;
                    selectedElementIds = pickedRefs.Select(r => r.ElementId).ToList();
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }
            }

            if (selectedElementIds.Count == 0)
            {
                TaskDialog.Show("Rotate Vertical Tool", "Không có đối tượng nào được chọn.");
                return Result.Cancelled;
            }

            // 2. BƯỚC 2: Chọn 1 Ống trong cụm làm TIM XOAY 3D
            Element axisPipe = null;
            try
            {
                Reference axisRef = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    new PipeSelectionFilter(),
                    "BƯỚC 2: Click chọn 1 ĐƯỜNG ỐNG trong cụm để làm TIM XOAY TRỤC:");

                if (axisRef != null)
                {
                    axisPipe = doc.GetElement(axisRef);
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }

            if (axisPipe == null || !(axisPipe.Location is LocationCurve))
            {
                TaskDialog.Show("Rotate Vertical Tool", "Ống làm tim xoay không hợp lệ.");
                return Result.Cancelled;
            }

            // 3. Khởi tạo ExternalEvent và Mở Hộp thoại điều khiển Xoay dồn nhiều lần
            try
            {
                RotateExternalEventHandler handler = new RotateExternalEventHandler();
                ExternalEvent exEvent = ExternalEvent.Create(handler);

                RotateVerticalWindow window = new RotateVerticalWindow(doc, selectedElementIds, axisPipe, exEvent, handler);
                window.Show(); // Mở cửa sổ Modeless để có thể vừa xem vừa click nút Xoay nhiều lần!

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private class PipeSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem != null && elem.Location is LocationCurve;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }
    }
}
