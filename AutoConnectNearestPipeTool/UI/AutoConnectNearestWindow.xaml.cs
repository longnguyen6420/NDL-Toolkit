using System;
using System.Collections.Generic;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using AutoConnectNearestPipeTool.Services;

namespace AutoConnectNearestPipeTool.UI
{
    public partial class AutoConnectNearestWindow : Window
    {
        private Document _doc;
        private List<Element> _sprinklers;
        private List<Element> _pipes;

        public AutoConnectNearestWindow(Document doc, List<Element> sprinklers, List<Element> pipes)
        {
            InitializeComponent();
            _doc = doc;
            _sprinklers = sprinklers;
            _pipes = pipes;

            lblInfo.Text = $"Đã nạp: {_sprinklers.Count} Đầu phun được chọn & {_pipes.Count} Ống chính";
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            if (_sprinklers.Count == 0 || _pipes.Count == 0)
            {
                TaskDialog.Show("Sprinkler Auto-Connect", "Vui lòng chọn Đầu phun và Ống chính.");
                return;
            }

            int count = OrthogonalAutoConnectService.ConnectSelectedSprinklersToPipe(_doc, _sprinklers, _pipes);

            TaskDialog.Show("Sprinkler Auto-Connect Success",
                $"Hoàn thành kết nối Đầu phun!\n" +
                $"- Số lượng Đầu phun đã được kết nối: {count} / {_sprinklers.Count} đầu phun.\n" +
                $"- Phụ kiện lắp đặt: CHỈ SỬ DỤNG CÚT 90° VÀ TÊ 90° (Chuẩn 100%).");

            this.Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
