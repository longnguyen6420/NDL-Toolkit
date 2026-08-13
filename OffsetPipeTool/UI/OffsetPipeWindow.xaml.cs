using System;
using System.Collections.Generic;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using OffsetPipeTool.Services;

namespace OffsetPipeTool.UI
{
    public partial class OffsetPipeWindow : Window
    {
        private Document _doc;
        private List<Element> _selectedPipes;

        public OffsetPipeWindow(Document doc, List<Element> selectedPipes)
        {
            InitializeComponent();
            _doc = doc;
            _selectedPipes = selectedPipes;

            lblInfo.Text = $"Đã chọn: {_selectedPipes.Count} đối tượng đường ống";
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            string input = txtDistance.Text.Trim();
            if (string.IsNullOrWhiteSpace(input))
            {
                TaskDialog.Show("Offset Pipe Tool", "Vui lòng nhập khoảng cách dịch.");
                return;
            }

            double distanceFeet = OffsetPipeService.ParseLengthToFeet(input);

            if (Math.Abs(distanceFeet) < 0.0001)
            {
                TaskDialog.Show("Offset Pipe Tool", "Khoảng cách dịch bằng 0 hoặc không hợp lệ.");
                return;
            }

            int moved = OffsetPipeService.ShiftPipesPerpendicularly(_doc, _selectedPipes, distanceFeet);

            TaskDialog.Show("Offset Pipe Success",
                $"Hoàn thành!\n" +
                $"- Số lượng ống đã dịch vuông góc: {moved} / {_selectedPipes.Count} ống.\n" +
                $"- Giá trị nhập: '{input}' ({distanceFeet:F3} ft)");

            this.Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
