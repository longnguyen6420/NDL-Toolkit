using System;
using System.Collections.Generic;
using System.Windows;
using Autodesk.Revit.DB;
using AutoSleeveTool.Services;

namespace AutoSleeveTool.UI
{
    public partial class AutoSleeveWindow : Window
    {
        private readonly Document _doc;

        public AutoSleeveWindow(Document doc)
        {
            InitializeComponent();
            _doc = doc;
        }

        private void OnUnitChanged(object sender, RoutedEventArgs e)
        {
            if (LblRoundClearance == null || LblRectClearance == null) return;

            if (RadioMm.IsChecked == true)
            {
                LblRoundClearance.Text = "Clearance ống Tròn (mm):";
                LblRectClearance.Text = "Clearance ống Chữ nhật (mm):";
                TxtRoundClearance.Text = "25";
                TxtRectClearance.Text = "50";
            }
            else
            {
                LblRoundClearance.Text = "Clearance ống Tròn (inch):";
                LblRectClearance.Text = "Clearance ống Chữ nhật (inch):";
                TxtRoundClearance.Text = "1.0";
                TxtRectClearance.Text = "2.0";
            }
        }

        private void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            bool isMm = RadioMm.IsChecked == true;

            double roundClrInput = 25;
            double rectClrInput = 50;

            double.TryParse(TxtRoundClearance.Text, out roundClrInput);
            double.TryParse(TxtRectClearance.Text, out rectClrInput);

            // Convert to internal Revit Feet units
            double roundClrFeet = isMm ? (roundClrInput / 304.8) : (roundClrInput / 12.0);
            double rectClrFeet = isMm ? (rectClrInput / 304.8) : (rectClrInput / 12.0);

            bool includePipes = ChkPipes.IsChecked == true;
            bool includeDucts = ChkDucts.IsChecked == true;

            bool checkWalls = ChkWalls.IsChecked == true;
            bool checkColumns = ChkColumns.IsChecked == true;
            bool checkBeams = ChkBeams.IsChecked == true;
            bool checkFloors = ChkFloors.IsChecked == true;

            if (!includePipes && !includeDucts)
            {
                MessageBox.Show("Vui lòng chọn ít nhất 1 loại đối tượng MEP (Duct hoặc Pipe)!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!checkWalls && !checkColumns && !checkBeams && !checkFloors)
            {
                MessageBox.Show("Vui lòng chọn ít nhất 1 loại đối tượng xuyên trong Revit Link!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                RevitLinkGeometryService linkService = new RevitLinkGeometryService(_doc);
                List<LinkPenetrationInfo> penetrations = linkService.FindPenetrations(
                    includePipes, includeDucts, checkWalls, checkColumns, checkBeams, checkFloors);

                if (penetrations.Count == 0)
                {
                    MessageBox.Show("Không tìm thấy vị trí giao cắt nào giữa Duct/Pipe và các đối tượng trong Revit Link!", "Kết quả", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                SleevePlacementService placementService = new SleevePlacementService(_doc);
                int count = placementService.PlaceSleeves(penetrations, roundClrFeet, rectClrFeet);

                MessageBox.Show($"Đã đặt thành công {count} Sleeve (DuctType 'sleeve') cho dự án!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Đã xảy ra lỗi khi tạo Sleeve: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
