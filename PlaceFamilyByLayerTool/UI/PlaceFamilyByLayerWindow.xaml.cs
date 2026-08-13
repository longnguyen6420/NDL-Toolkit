using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using PlaceFamilyByLayerTool.Services;

namespace PlaceFamilyByLayerTool.UI
{
    public partial class PlaceFamilyByLayerWindow : Window
    {
        private Document _doc;
        private ImportInstance _cadLink;
        private List<FamilySymbol> _symbols;
        private List<Level> _levels;

        public PlaceFamilyByLayerWindow(Document doc, ImportInstance cadLink)
        {
            InitializeComponent();
            _doc = doc;
            _cadLink = cadLink;

            LoadData();
        }

        private void LoadData()
        {
            // 1. Lấy danh sách FamilySymbol trong dự án
            _symbols = new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .OrderBy(s => s.FamilyName)
                .ThenBy(s => s.Name)
                .ToList();

            cboFamilyType.ItemsSource = _symbols.Select(s => $"{s.FamilyName} : {s.Name}").ToList();
            if (_symbols.Count > 0) cboFamilyType.SelectedIndex = 0;

            // 2. Lấy danh sách Level trong dự án
            _levels = new FilteredElementCollector(_doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            cboLevel.ItemsSource = _levels.Select(l => l.Name).ToList();
            if (_levels.Count > 0) cboLevel.SelectedIndex = 0;

            // 3. Nạp TẤT CẢ TÊN LAYER từ file CAD link đã chọn trên mặt bằng
            if (_cadLink != null)
            {
                List<string> layers = CadLayerService.GetAllLayerNames(_doc, _cadLink);
                if (layers.Count > 0)
                {
                    cboLayerName.ItemsSource = layers;
                    cboLayerName.SelectedIndex = 0;
                }
            }
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            if (cboFamilyType.SelectedIndex < 0 || cboLevel.SelectedIndex < 0)
            {
                TaskDialog.Show("Place Family", "Vui lòng chọn Family Type và Level.");
                return;
            }

            string layerName = cboLayerName.Text.Trim();
            if (string.IsNullOrWhiteSpace(layerName))
            {
                TaskDialog.Show("Place Family", "Vui lòng chọn hoặc nhập tên Layer Name.");
                return;
            }

            FamilySymbol selectedSymbol = _symbols[cboFamilyType.SelectedIndex];
            Level selectedLevel = _levels[cboLevel.SelectedIndex];
            double offsetFeet = CadLayerService.ParseElevationToFeet(txtElevation.Text);

            // Lấy dung sai gộp điểm (Tolerance mm)
            double toleranceMm = 300.0;
            if (double.TryParse(txtTolerance.Text.Trim(), out double tolVal) && tolVal > 0)
            {
                toleranceMm = tolVal;
            }

            // Thuật toán Lọc & Gộp cụm điểm (Clustering Centroid) tránh trùng lặp đè Family
            List<XYZ> clusteredPoints = CadLayerService.GetClusteredInsertionPoints(_doc, _cadLink, layerName, toleranceMm);

            if (clusteredPoints.Count == 0)
            {
                TaskDialog.Show("Place Family", $"Không tìm thấy điểm hoặc đối tượng nào thuộc Layer '{layerName}' trong file CAD link!");
                return;
            }

            int count = 0;

            using (Transaction tx = new Transaction(_doc, "Place Family by CAD Layer"))
            {
                tx.Start();

                if (!selectedSymbol.IsActive)
                {
                    selectedSymbol.Activate();
                    _doc.Regenerate();
                }

                foreach (XYZ pt in clusteredPoints)
                {
                    try
                    {
                        double targetZ = selectedLevel.Elevation + offsetFeet;
                        XYZ placePt = new XYZ(pt.X, pt.Y, targetZ);

                        FamilyInstance inst = _doc.Create.NewFamilyInstance(
                            placePt,
                            selectedSymbol,
                            selectedLevel,
                            StructuralType.NonStructural
                        );

                        if (inst != null)
                        {
                            // Đặt cao độ offset nếu thuộc tính tồn tại
                            Parameter paramOffset = inst.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
                            if (paramOffset != null && !paramOffset.IsReadOnly)
                            {
                                paramOffset.Set(offsetFeet);
                            }
                            else
                            {
                                Parameter paramElev = inst.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM);
                                if (paramElev != null && !paramElev.IsReadOnly)
                                {
                                    paramElev.Set(offsetFeet);
                                }
                            }
                            count++;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("Lỗi place family: " + ex.Message);
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Place Family Success", 
                $"Hoàn thành chèn Family thông minh!\n" +
                $"- Layer xử lý: '{layerName}'\n" +
                $"- Số cụm Block nhận diện được: {clusteredPoints.Count}\n" +
                $"- Đã chèn thành công: {count} Family (mỗi cụm chèn đúng 1 Family tại tâm).");
            
            this.Close();
        }
    }
}
