using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using PipePlaceholderTool.UI;

namespace PipePlaceholderTool.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PipePlaceholderCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            // 1. Kiểm tra pre-selection CAD Link
            ICollection<ElementId> selectedIds = uidoc.Selection.GetElementIds();
            ImportInstance cadLink = null;

            if (selectedIds != null && selectedIds.Count > 0)
            {
                foreach (ElementId id in selectedIds)
                {
                    Element el = doc.GetElement(id);
                    if (el is ImportInstance imp)
                    {
                        cadLink = imp;
                        break;
                    }
                }
            }

            // 2. Nếu chưa chọn trước -> Quét tự động trong View hiện tại
            if (cadLink == null)
            {
                List<ImportInstance> viewCadLinks = new FilteredElementCollector(doc, activeView.Id)
                    .OfClass(typeof(ImportInstance))
                    .Cast<ImportInstance>()
                    .ToList();

                if (viewCadLinks.Count > 0)
                {
                    cadLink = viewCadLinks[0];
                }
            }

            // 3. Nếu vẫn chưa chọn -> Yêu cầu người dùng chọn tương tác
            if (cadLink == null)
            {
                try
                {
                    Reference pickedRef = uidoc.Selection.PickObject(
                        ObjectType.Element,
                        new CadLinkSelectionFilter(),
                        "Chọn bản vẽ CAD Link (ImportInstance) để tạo Pipe Placeholder theo Layer:");

                    cadLink = doc.GetElement(pickedRef) as ImportInstance;
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }
            }

            if (cadLink == null)
            {
                TaskDialog.Show("Pipe Placeholder Tool", "Không tìm thấy bản vẽ CAD Link nào trong View.");
                return Result.Cancelled;
            }

            // 4. Mở cửa sổ thiết lập Pipe Placeholder Settings
            try
            {
                PipePlaceholderWindow window = new PipePlaceholderWindow(doc, cadLink);
                window.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private class CadLinkSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem is ImportInstance;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }
    }
}
