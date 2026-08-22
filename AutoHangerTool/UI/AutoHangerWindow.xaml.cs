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

            txtSelectedInfo.Text = $"Selected: {_pipes.Count} pipe segments";
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
                    DisplayName = "[No Family Loaded - Click 'Auto-Generate' Below]"
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
                TaskDialog.Show("NDL Hanger", "✅ Standard 'NDL_Hanger_Insert.rfa' family created and loaded successfully into project!");
            }
            else
            {
                TaskDialog.Show("NDL Hanger", "⚠️ Loaded existing project families. You can select any family from the dropdown list above.");
            }
        }

        private void BtnPlace_Click(object sender, RoutedEventArgs e)
        {
            double spacingInches = 96;
            double.TryParse(txtSpacing.Text, out spacingInches);

            double offsetInches = 12;
            double.TryParse(txtFittingOffset.Text, out offsetInches);

            double slabHeightInches = 144;
            double.TryParse(txtSlabHeight.Text, out slabHeightInches);

            double thresholdSize = 6;
            double.TryParse(txtThresholdSize.Text, out thresholdSize);

            double sideClearance = 2.5;
            double.TryParse(txtSideClearance.Text, out sideClearance);

            string rodSize = (cboRodSize.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "1/2\"";

            HangerMode mode = HangerMode.AutoBySize;
            if (cboHangerMode.SelectedIndex == 1) mode = HangerMode.SingleRodAlways;
            else if (cboHangerMode.SelectedIndex == 2) mode = HangerMode.DualRodAlways;

            var settings = new HangerSettings
            {
                SpacingInches = spacingInches,
                FittingOffsetInches = offsetInches,
                PlaceNearFittings = chkNearFittings.IsChecked == true,
                RodSize = rodSize,
                DefaultSlabHeightInches = slabHeightInches,
                Mode = mode,
                DualRodThresholdInches = thresholdSize,
                RodSideClearanceInches = sideClearance
            };

            FamilySymbol selectedSymbol = (cboHangerFamily.SelectedItem as HangerFamilyItem)?.Symbol;
            if (selectedSymbol == null)
            {
                selectedSymbol = HangerFamilyService.GetOrLoadHangerFamilySymbol(_doc);
            }

            if (selectedSymbol == null)
            {
                TaskDialog.Show("NDL Hanger", "Please select a Hanger Family from the dropdown or click 'Auto-Generate' below.");
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

            TaskDialog.Show("NDL Hanger & Insert", $"✅ Successfully placed {placedCount} hangers/inserts across {_pipes.Count} pipe segments!");
            DialogResult = true;
            Close();
        }
    }
}
