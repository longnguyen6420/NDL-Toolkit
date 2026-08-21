using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NDL.AutoHangerTool.Services;

namespace NDL.AutoHangerTool.UI
{
    public partial class AutoHangerWindow : Window
    {
        private readonly Document _doc;
        private readonly List<Element> _pipes;

        public AutoHangerWindow(Document doc, List<Element> pipes)
        {
            InitializeComponent();
            _doc = doc;
            _pipes = pipes;

            txtSelectedInfo.Text = $"Đã chọn: {_pipes.Count} đoạn ống cơ điện";
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnPlace_Click(object sender, RoutedEventArgs e)
        {
            double spacing = 2000;
            double.TryParse(txtSpacing.Text, out spacing);

            double offset = 300;
            double.TryParse(txtFittingOffset.Text, out offset);

            double slabHeight = 3600;
            double.TryParse(txtSlabHeight.Text, out slabHeight);

            string rodSize = (cboRodSize.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "M10";

            var settings = new HangerSettings
            {
                SpacingMm = spacing,
                FittingOffsetMm = offset,
                PlaceNearFittings = chkNearFittings.IsChecked == true,
                RodSize = rodSize,
                DefaultSlabHeightMm = slabHeight
            };

            FamilySymbol symbol = HangerFamilyService.GetOrLoadHangerFamilySymbol(_doc);
            if (symbol == null)
            {
                TaskDialog.Show("NDL Hanger", "Không tìm thấy Family Hanger/Insert hợp lệ trong dự án.");
                return;
            }

            int placedCount = HangerPlacementService.PlaceHangersOnPipes(_doc, _pipes, settings, symbol);

            TaskDialog.Show("NDL Hanger & Insert", $"✅ Đã gắn thành công {placedCount} điểm ti treo & Insert trần trên {_pipes.Count} đoạn ống!");
            DialogResult = true;
            Close();
        }
    }
}
