using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using AutoConnectNearestPipeTool.UI;

namespace AutoConnectNearestPipeTool.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoConnectNearestCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            List<Element> selectedSprinklers = new List<Element>();
            List<Element> targetPipes = new List<Element>();

            // 1. Kiểm tra Pre-selection trên màn hình
            ICollection<ElementId> selectedIds = uidoc.Selection.GetElementIds();
            if (selectedIds != null && selectedIds.Count > 0)
            {
                foreach (ElementId id in selectedIds)
                {
                    Element elem = doc.GetElement(id);
                    if (IsSprinkler(elem))
                    {
                        selectedSprinklers.Add(elem);
                    }
                    else if (IsPipe(elem))
                    {
                        targetPipes.Add(elem);
                    }
                }
            }

            // 2. Nếu chưa quét đủ 2 loại -> Yêu cầu quét chọn tương tác 2 bước
            if (selectedSprinklers.Count == 0 || targetPipes.Count == 0)
            {
                try
                {
                    // Bước 1: Chọn Đầu phun
                    if (selectedSprinklers.Count == 0)
                    {
                        IList<Reference> sprinklerRefs = uidoc.Selection.PickObjects(
                            ObjectType.Element,
                            new SprinklerSelectionFilter(),
                            "BƯỚC 1: Chọn các ĐẦU PHUN (Sprinklers) cần tự động nối:");

                        if (sprinklerRefs == null || sprinklerRefs.Count == 0) return Result.Cancelled;
                        selectedSprinklers = sprinklerRefs.Select(r => doc.GetElement(r)).Where(IsSprinkler).ToList();
                    }

                    // Bước 2: Chọn Ống chính
                    if (targetPipes.Count == 0)
                    {
                        IList<Reference> pipeRefs = uidoc.Selection.PickObjects(
                            ObjectType.Element,
                            new PipeSelectionFilter(),
                            "BƯỚC 2: Chọn ĐƯỜNG ỐNG CHÍNH để kết nối vào:");

                        if (pipeRefs == null || pipeRefs.Count == 0) return Result.Cancelled;
                        targetPipes = pipeRefs.Select(r => doc.GetElement(r)).Where(IsPipe).ToList();
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }
            }

            if (selectedSprinklers.Count == 0 || targetPipes.Count == 0)
            {
                TaskDialog.Show("Sprinkler Auto-Connect", "Vui lòng chọn đầy đủ các Đầu phun cần nối và Đường ống chính.");
                return Result.Cancelled;
            }

            // 3. Mở cửa sổ điều khiển nối Đầu phun bằng Cút 90° & Tê 90°
            try
            {
                AutoConnectNearestWindow window = new AutoConnectNearestWindow(doc, selectedSprinklers, targetPipes);
                window.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static bool IsSprinkler(Element elem)
        {
            if (elem == null || elem.Category == null) return false;
            return elem.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Sprinklers;
        }

        private static bool IsPipe(Element elem)
        {
            if (elem == null) return false;
            return elem.Location is LocationCurve;
        }

        private class SprinklerSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return IsSprinkler(elem);
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }

        private class PipeSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return IsPipe(elem);
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }
    }
}
