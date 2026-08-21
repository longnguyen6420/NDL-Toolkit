using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using NDL.AutoHangerTool.UI;

namespace NDL.AutoHangerTool.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoHangerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null) return Result.Cancelled;

            Document doc = uidoc.Document;
            List<Element> selectedPipes = new List<Element>();

            // 1. Check Pre-selection
            ICollection<ElementId> currentSelectedIds = uidoc.Selection.GetElementIds();
            if (currentSelectedIds != null && currentSelectedIds.Count > 0)
            {
                foreach (ElementId id in currentSelectedIds)
                {
                    Element elem = doc.GetElement(id);
                    if (IsValidPipeElement(elem))
                    {
                        selectedPipes.Add(elem);
                    }
                }
            }

            // 2. If no pre-selection, prompt user to select pipes on plan
            if (selectedPipes.Count == 0)
            {
                try
                {
                    IList<Reference> pickedRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        new PipeSelectionFilter(),
                        "NDL Hanger: Quét chọn các đoạn ống cần rải ti treo & insert:");

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
                TaskDialog.Show("NDL Auto Hanger", "Không có đoạn ống hợp lệ nào được chọn.");
                return Result.Cancelled;
            }

            // 3. Open Configuration Window
            try
            {
                AutoHangerWindow window = new AutoHangerWindow(doc, selectedPipes);
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
