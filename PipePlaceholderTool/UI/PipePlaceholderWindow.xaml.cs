using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using PipePlaceholderTool.Services;

namespace PipePlaceholderTool.UI
{
    public partial class PipePlaceholderWindow : Window
    {
        private Document _doc;
        private ImportInstance _cadLink;
        private List<PipingSystemType> _systemTypes;
        private List<Level> _levels;
        private List<PipeType> _pipeTypes;

        public PipePlaceholderWindow(Document doc, ImportInstance cadLink)
        {
            InitializeComponent();
            _doc = doc;
            _cadLink = cadLink;

            LoadData();
        }

        private void LoadData()
        {
            // 1. PipingSystemType
            _systemTypes = new FilteredElementCollector(_doc)
                .OfClass(typeof(PipingSystemType))
                .Cast<PipingSystemType>()
                .OrderBy(s => s.Name)
                .ToList();

            cboSystemType.ItemsSource = _systemTypes.Select(s => s.Name).ToList();
            if (_systemTypes.Count > 0) cboSystemType.SelectedIndex = 0;

            // 2. Level
            _levels = new FilteredElementCollector(_doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            cboLevel.ItemsSource = _levels.Select(l => l.Name).ToList();
            if (_levels.Count > 0) cboLevel.SelectedIndex = 0;

            // 3. PipeType
            _pipeTypes = new FilteredElementCollector(_doc)
                .OfClass(typeof(PipeType))
                .Cast<PipeType>()
                .OrderBy(p => p.Name)
                .ToList();

            cboMainPipeType.ItemsSource = _pipeTypes.Select(p => p.Name).ToList();
            cboBranchPipeType.ItemsSource = _pipeTypes.Select(p => p.Name).ToList();
            if (_pipeTypes.Count > 0)
            {
                cboMainPipeType.SelectedIndex = 0;
                cboBranchPipeType.SelectedIndex = 0;
            }

            // 4. CAD DWG Layers
            if (_cadLink != null)
            {
                List<string> layers = CadPipeLineService.GetAllLayerNames(_doc, _cadLink);
                if (layers.Count > 0)
                {
                    cboLayerName.ItemsSource = layers;
                    cboLayerName.SelectedIndex = 0;
                }
            }
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            if (cboSystemType.SelectedIndex < 0 || cboLevel.SelectedIndex < 0 || cboMainPipeType.SelectedIndex < 0)
            {
                TaskDialog.Show("Pipe Placeholder", "Vui lòng chọn System Type, Level và Main Pipe Type.");
                return;
            }

            string layerName = cboLayerName.Text.Trim();
            if (string.IsNullOrWhiteSpace(layerName))
            {
                TaskDialog.Show("Pipe Placeholder", "Vui lòng chọn hoặc nhập tên Layer Name.");
                return;
            }

            PipingSystemType sysType = _systemTypes[cboSystemType.SelectedIndex];
            Level level = _levels[cboLevel.SelectedIndex];
            PipeType mainPipeType = _pipeTypes[cboMainPipeType.SelectedIndex];

            double elevationFeet = CadPipeLineService.ParseElevationToFeet(txtElevation.Text);
            double mainSizeFeet = CadPipeLineService.ParseSizeToFeet(txtMainSize.Text);

            // Quét các đoạn thẳng thuộc Layer trong CAD Link
            List<CadPipeSegment> segments = CadPipeLineService.GetLineSegmentsByLayer(_doc, _cadLink, layerName);

            if (segments.Count == 0)
            {
                TaskDialog.Show("Pipe Placeholder", $"Không tìm thấy đoạn thẳng/đường ống nào thuộc Layer '{layerName}' trong file CAD link!");
                return;
            }

            int count = 0;

            using (Transaction tx = new Transaction(_doc, "Create Pipe Placeholders from CAD Layer"))
            {
                tx.Start();

                foreach (var seg in segments)
                {
                    try
                    {
                        XYZ p1 = new XYZ(seg.Start.X, seg.Start.Y, level.Elevation + elevationFeet);
                        XYZ p2 = new XYZ(seg.End.X, seg.End.Y, level.Elevation + elevationFeet);

                        // Tạo Pipe Placeholder nguyên bản bằng Revit API
                        Pipe pipePlaceholder = Pipe.CreatePlaceholder(_doc, sysType.Id, mainPipeType.Id, level.Id, p1, p2);

                        if (pipePlaceholder != null)
                        {
                            // Đặt đường kính Pipe Size
                            Parameter diamParam = pipePlaceholder.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                            if (diamParam != null && !diamParam.IsReadOnly)
                            {
                                diamParam.Set(mainSizeFeet);
                            }

                            // Đặt cao độ Offset
                            Parameter offsetParam = pipePlaceholder.get_Parameter(BuiltInParameter.RBS_OFFSET_PARAM);
                            if (offsetParam != null && !offsetParam.IsReadOnly)
                            {
                                offsetParam.Set(elevationFeet);
                            }

                            count++;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("Lỗi tạo pipe placeholder: " + ex.Message);
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Pipe Placeholder Success",
                $"Hoàn thành!\n" +
                $"- Layer xử lý: '{layerName}'\n" +
                $"- Số lượng Pipe Placeholder đã tạo: {count} đoạn.\n" +
                $"- System: {sysType.Name} | Pipe Type: {mainPipeType.Name}");

            this.Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
