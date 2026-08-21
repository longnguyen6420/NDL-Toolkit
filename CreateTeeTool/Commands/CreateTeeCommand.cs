using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Autodesk.Revit.Exceptions;
using CreateRevitTeeTool.Services;

namespace CreateRevitTeeTool.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateTeeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            int createdCount = 0;

            TaskDialog.Show("Tạo Tê Ống (Pipe Tee Tool)", 
                "Click chọn điểm trên ống để tạo Tê.\nThao tác lặp liên tục cho tới khi nhấn ESC.");

            while (true)
            {
                try
                {
                    Reference reference = uidoc.Selection.PickObject(
                        ObjectType.PointOnElement, 
                        "Chọn 1 điểm bất kỳ trên đường ống để tạo Tê (Bấm ESC để dừng)"
                    );

                    if (reference == null) break;

                    Element elem = doc.GetElement(reference.ElementId);
                    if (!(elem is Pipe pipe))
                    {
                        TaskDialog.Show("Cảnh báo", "Đối tượng được chọn không phải là Đường Ống (Pipe)!");
                        continue;
                    }

                    XYZ pickedPoint = reference.GlobalPoint;

                    using (Transaction trans = new Transaction(doc, "Create Pipe Tee Fitting"))
                    {
                        trans.Start();
                        try
                        {
                            PipeTeeService.CreateTeeAtPoint(doc, pipe, pickedPoint, "horizontal");
                            trans.Commit();
                            createdCount++;
                        }
                        catch (Exception ex)
                        {
                            trans.RollBack();
                            TaskDialog.Show("Lỗi", "Không thể tạo Tê tại vị trí chọn:\n" + ex.Message);
                        }
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    // Người dùng bấm ESC
                    break;
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Lỗi hệ thống", ex.Message);
                    break;
                }
            }

            if (createdCount > 0)
            {
                TaskDialog.Show("Hoàn thành", $"Đã tạo thành công {createdCount} cái Tê trên ống!");
            }

            return Result.Succeeded;
        }
    }
}
