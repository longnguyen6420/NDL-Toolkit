using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace PendentSprinklerOptimizer
{
    /// <summary>
    /// Kiểm tra tính khả dụng của lệnh trong phiên làm việc của Revit.
    /// Đảm bảo lệnh chỉ chạy khi tài liệu đang mở không phải là môi trường thiết kế Family.
    /// </summary>
    public class CommandAvailability : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
        {
            UIDocument uiDoc = applicationData.ActiveUIDocument;
            if (uiDoc == null) return false;

            Document doc = uiDoc.Document;
            return doc != null && !doc.IsFamilyDocument;
        }
    }
}
