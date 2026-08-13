using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using OffsetPipeTool.UI;

namespace OffsetPipeTool.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class OffsetPipeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            List<Element> selectedPipes = new List<Element>();

            // 1. Kiểm tra Pre-selection
            ICollection<ElementId> selectedIds = uidoc.Selection.GetElementIds();
            if (selectedIds != null && selectedIds.Count > 0)
            {
                foreach (ElementId id in selectedIds)
                {
                    Element elem = doc.GetElement(id);
                    if (IsValidPipeElement(elem))
                    {
                        selectedPipes.Add(elem);
                    }
                }
            }

            // 2. Nếu chưa chọn trước -> Yêu cầu người dùng quét chọn nhiều ống trên mặt bằng
            if (selectedPipes.Count == 0)
            {
                try
                {
                    IList<Reference> pickedRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        new PipeSelectionFilter(),
                        "Chọn các đối tượng đường ống (Pipes / Placeholders / Ducts) cần dịch vuông góc:");

                    if (pickedRefs != null)
                    {
                        foreach (Reference r in pickedRefs)
                        {
                            Element elem = doc.GetElement(r);
                            if (IsValidPipeElement(elem))
                            {
                                selectedPipes.Add(elem);
                            }
                        }
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }
            }

            if (selectedPipes.Count == 0)
            {
                TaskDialog.Show("Offset Pipe Tool", "Không có đối tượng đường ống hợp lệ nào được chọn.");
                return Result.Cancelled;
            }

            // 3. Mở cửa sổ nhập khoảng cách dịch vuông góc
            try
            {
                OffsetPipeWindow window = new OffsetPipeWindow(doc, selectedPipes);
                window.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static bool IsValidPipeElement(Element elem)
        {
            if (elem == null) return false;
            return elem.Location is LocationCurve;
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
