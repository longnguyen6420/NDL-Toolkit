using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using AlignTagTool.UI;

namespace AlignTagTool.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AlignTagCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            // Lấy danh sách Tag đã được quét chọn trước trên View 2D
            ICollection<ElementId> selectedIds = uidoc.Selection.GetElementIds();
            List<IndependentTag> tags = new List<IndependentTag>();

            if (selectedIds != null && selectedIds.Count > 0)
            {
                foreach (ElementId id in selectedIds)
                {
                    Element el = doc.GetElement(id);
                    if (el is IndependentTag tag)
                    {
                        tags.Add(tag);
                    }
                }
            }

            // Nếu người dùng chưa quét chọn trước -> Cho phép chọn tương tác
            if (tags.Count == 0)
            {
                try
                {
                    IList<Reference> pickedRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        new TagSelectionFilter(),
                        "Quét chọn các Tag cần căn lề & sắp xếp khoảng cách:");

                    foreach (Reference r in pickedRefs)
                    {
                        IndependentTag tag = doc.GetElement(r) as IndependentTag;
                        if (tag != null && !tags.Contains(tag))
                        {
                            tags.Add(tag);
                        }
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }
            }

            if (tags.Count < 2)
            {
                TaskDialog.Show("Align Tag Tool", "Vui lòng chọn ít nhất 2 Tags để thực hiện căn chỉnh.");
                return Result.Cancelled;
            }

            // Mở giao diện điều khiển căn lề Tag
            try
            {
                AlignTagWindow window = new AlignTagWindow(doc, tags);
                window.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private class TagSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem is IndependentTag;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }
    }
}
