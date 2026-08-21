using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ViewRenameTool.ViewModels;
using ViewRenameTool.Views;

namespace ViewRenameTool
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ViewRenameCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            try
            {
                var views = GetSelectedViews(uiDoc, doc);
                if (views.Count == 0)
                {
                    TaskDialog.Show("Thông báo", "Không có View nào được chọn để đổi tên!");
                    return Result.Cancelled;
                }

                var renameItems = new List<ViewRenameItem>();
                foreach (var v in views)
                {
                    string levelName = GetViewLevelName(v, doc);
                    renameItems.Add(new ViewRenameItem(v, levelName));
                }

                var viewModel = new RenameViewModel(renameItems);
                var window = new RenameWindow(viewModel);

                try
                {
                    var helper = new System.Windows.Interop.WindowInteropHelper(window);
                    helper.Owner = uiApp.MainWindowHandle;
                }
                catch { }

                bool? dialogResult = window.ShowDialog();
                if (dialogResult != true)
                {
                    return Result.Cancelled;
                }

                int successCount = 0;
                int failedCount = 0;

                using (Transaction t = new Transaction(doc, "View Rename - Antigravity"))
                {
                    t.Start();

                    foreach (var item in viewModel.Items)
                    {
                        if (!item.IsSelected) continue;

                        string sanitizedName = SanitizeViewName(item.CalculatedName.Trim()).ToUpper();
                        if (string.IsNullOrEmpty(sanitizedName)) continue;
                        if (item.OriginalName.Equals(sanitizedName, StringComparison.Ordinal)) continue;

                        try
                        {
                            item.RevitView.Name = sanitizedName;
                            successCount++;
                        }
                        catch (Exception)
                        {
                            failedCount++;
                        }
                    }

                    t.Commit();
                }

                TaskDialog.Show("Thành công", $"Đã hoàn thành đổi tên View!\n\n- Thành công: {successCount} view(s)\n- Bỏ qua / Lỗi: {failedCount} view(s)");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Lỗi NDL View Rename", "Lỗi khi chạy công cụ View Rename:\n\n" + ex.ToString());
                message = ex.Message;
                return Result.Failed;
            }
        }

        private List<View> GetSelectedViews(UIDocument uiDoc, Document doc)
        {
            var selectedIds = uiDoc.Selection.GetElementIds();
            var views = new List<View>();

            foreach (var id in selectedIds)
            {
                Element elem = doc.GetElement(id);
                if (elem is View v && !v.IsTemplate)
                {
                    views.Add(v);
                }
            }

            return views;
        }

        private string GetViewLevelName(View view, Document doc)
        {
            try
            {
                // 1. GenLevel
                if (view.GenLevel != null && !string.IsNullOrEmpty(view.GenLevel.Name))
                {
                    return view.GenLevel.Name.ToUpper();
                }

                // 2. BuiltInParameter PLAN_VIEW_LEVEL
                Parameter param = view.get_Parameter(BuiltInParameter.PLAN_VIEW_LEVEL);
                if (param != null && param.HasValue)
                {
                    ElementId levelId = param.AsElementId();
                    if (levelId != null && levelId != ElementId.InvalidElementId)
                    {
                        Element levelElem = doc.GetElement(levelId);
                        if (levelElem != null && !string.IsNullOrEmpty(levelElem.Name)) return levelElem.Name.ToUpper();
                    }
                }

                // 3. Fallback: LookupParameter "Associated Level" or "Level"
                Parameter paramAssoc = view.LookupParameter("Associated Level") ?? view.LookupParameter("Level");
                if (paramAssoc != null && paramAssoc.HasValue)
                {
                    if (paramAssoc.StorageType == StorageType.ElementId)
                    {
                        ElementId levelId = paramAssoc.AsElementId();
                        if (levelId != null && levelId != ElementId.InvalidElementId)
                        {
                            Element levelElem = doc.GetElement(levelId);
                            if (levelElem != null && !string.IsNullOrEmpty(levelElem.Name)) return levelElem.Name.ToUpper();
                        }
                    }
                    else if (paramAssoc.StorageType == StorageType.String)
                    {
                        return (paramAssoc.AsString() ?? string.Empty).ToUpper();
                    }
                }
            }
            catch { }

            return string.Empty;
        }

        private string SanitizeViewName(string name)
        {
            char[] invalidChars = new char[] { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' };
            foreach (char c in invalidChars)
            {
                name = name.Replace(c, '_');
            }
            return name.ToUpper();
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchRenameViewsCommand : ViewRenameCommand
    {
    }
}

namespace BatchRenameViewsTool
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchRenameViewsCommand : ViewRenameTool.ViewRenameCommand
    {
    }
}
