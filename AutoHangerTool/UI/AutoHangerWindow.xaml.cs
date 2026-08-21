using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NDL.AutoHangerTool.Services;

namespace NDL.AutoHangerTool.UI
{
    public class HangerFamilyItem
    {
        public FamilySymbol Symbol { get; set; }
        public string DisplayName { get; set; }

        public override string ToString()
        {
            return DisplayName;
        }
    }

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
            LoadFamilyList();
        }

        private void LoadFamilyList()
        {
            cboHangerFamily.Items.Clear();

            var available = HangerFamilyService.GetAvailableHangerSymbols(_doc);
            foreach (var sym in available)
            {
                cboHangerFamily.Items.Add(new HangerFamilyItem
                {
                    Symbol = sym,
                    DisplayName = $"{sym.FamilyName} : {sym.Name}"
                });
            }

            if (cboHangerFamily.Items.Count > 0)
            {
                // Prioritize NDL_Hanger_Insert or Hanger
                int defaultIndex = 0;
                for (int i = 0; i < cboHangerFamily.Items.Count; i++)
                {
                    var item = cboHangerFamily.Items[i] as HangerFamilyItem;
                    if (item != null && (item.DisplayName.IndexOf("NDL", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         item.DisplayName.IndexOf("Hanger", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        defaultIndex = i;
                        break;
                    }
                }
                cboHangerFamily.SelectedIndex = defaultIndex;
            }
            else
            {
                cboHangerFamily.Items.Add(new HangerFamilyItem
                {
                    Symbol = null,
                    DisplayName = "[Chưa có Family - Bấm nút 'Tự Động Tạo' bên dưới]"
                });
                cboHangerFamily.SelectedIndex = 0;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnCreateFamily_Click(object sender, RoutedEventArgs e)
        {
            bool success = HangerFamilyService.CreateStandardHangerFamily(_doc.Application);
            if (success)
            {
                HangerFamilyService.GetOrLoadHangerFamilySymbol(_doc);
                LoadFamilyList();
                TaskDialog.Show("NDL Hanger", "✅ Đã tự động tạo Family chuẩn 'NDL_Hanger_Insert.rfa' và nạp vào dự án thành công!");
            }
            else
            {
                TaskDialog.Show("NDL Hanger", "⚠️ Đã nạp danh sách Family có sẵn trong dự án. Bạn có thể chọn 1 Family bất kỳ ở ô danh sách phía trên.");
            }
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

            FamilySymbol selectedSymbol = (cboHangerFamily.SelectedItem as HangerFamilyItem)?.Symbol;
            if (selectedSymbol == null)
            {
                selectedSymbol = HangerFamilyService.GetOrLoadHangerFamilySymbol(_doc);
            }

            if (selectedSymbol == null)
            {
                TaskDialog.Show("NDL Hanger", "Vui lòng chọn 1 Family Ti Treo từ danh sách hoặc bấm nút 'Tự Động Tạo' bên dưới.");
                return;
            }

            if (!selectedSymbol.IsActive)
            {
                using (Transaction t = new Transaction(_doc, "Activate Symbol"))
                {
                    t.Start();
                    selectedSymbol.Activate();
                    t.Commit();
                }
            }

            int placedCount = HangerPlacementService.PlaceHangersOnPipes(_doc, _pipes, settings, selectedSymbol);

            TaskDialog.Show("NDL Hanger & Insert", $"✅ Đã gắn thành công {placedCount} điểm ti treo & Insert trần trên {_pipes.Count} đoạn ống!");
            DialogResult = true;
            Close();
        }
    }
}
