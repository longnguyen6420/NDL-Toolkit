using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AlignTagTool.UI
{
    public partial class AlignTagWindow : Window
    {
        private Document _doc;
        private List<IndependentTag> _tags;

        public AlignTagWindow(Document doc, List<IndependentTag> tags)
        {
            InitializeComponent();
            _doc = doc;
            _tags = tags ?? new List<IndependentTag>();
            txtStatus.Text = $"Đã chọn: {_tags.Count} Tags";
        }

        private double GetSpacingMm()
        {
            if (double.TryParse(txtSpacing.Text.Trim(), out double val) && val > 0)
            {
                return val;
            }
            return 300.0;
        }

        private void ExecuteAlign(AlignMode mode)
        {
            if (_tags.Count < 2)
            {
                TaskDialog.Show("Tag Align", "Vui lòng chọn ít nhất 2 Tags để thực hiện căn chỉnh.");
                return;
            }

            double spacingMm = GetSpacingMm();

            using (Transaction tx = new Transaction(_doc, "Align & Distribute Tags"))
            {
                tx.Start();

                switch (mode)
                {
                    case AlignMode.Left:
                        {
                            double minX = _tags.Min(t => t.TagHeadPosition.X);
                            foreach (var tag in _tags)
                            {
                                XYZ pos = tag.TagHeadPosition;
                                tag.TagHeadPosition = new XYZ(minX, pos.Y, pos.Z);
                            }
                        }
                        break;

                    case AlignMode.Right:
                        {
                            double maxX = _tags.Max(t => t.TagHeadPosition.X);
                            foreach (var tag in _tags)
                            {
                                XYZ pos = tag.TagHeadPosition;
                                tag.TagHeadPosition = new XYZ(maxX, pos.Y, pos.Z);
                            }
                        }
                        break;

                    case AlignMode.CenterX:
                        {
                            double avgX = _tags.Average(t => t.TagHeadPosition.X);
                            foreach (var tag in _tags)
                            {
                                XYZ pos = tag.TagHeadPosition;
                                tag.TagHeadPosition = new XYZ(avgX, pos.Y, pos.Z);
                            }
                        }
                        break;

                    case AlignMode.Top:
                        {
                            double maxY = _tags.Max(t => t.TagHeadPosition.Y);
                            foreach (var tag in _tags)
                            {
                                XYZ pos = tag.TagHeadPosition;
                                tag.TagHeadPosition = new XYZ(pos.X, maxY, pos.Z);
                            }
                        }
                        break;

                    case AlignMode.Bottom:
                        {
                            double minY = _tags.Min(t => t.TagHeadPosition.Y);
                            foreach (var tag in _tags)
                            {
                                XYZ pos = tag.TagHeadPosition;
                                tag.TagHeadPosition = new XYZ(pos.X, minY, pos.Z);
                            }
                        }
                        break;

                    case AlignMode.CenterY:
                        {
                            double avgY = _tags.Average(t => t.TagHeadPosition.Y);
                            foreach (var tag in _tags)
                            {
                                XYZ pos = tag.TagHeadPosition;
                                tag.TagHeadPosition = new XYZ(pos.X, avgY, pos.Z);
                            }
                        }
                        break;

                    case AlignMode.DistributeV:
                        {
                            var sorted = _tags.OrderByDescending(t => t.TagHeadPosition.Y).ToList();
                            double maxY = sorted.First().TagHeadPosition.Y;
                            double minY = sorted.Last().TagHeadPosition.Y;
                            double stepY = (maxY - minY) / (sorted.Count - 1);

                            for (int i = 0; i < sorted.Count; i++)
                            {
                                XYZ pos = sorted[i].TagHeadPosition;
                                double targetY = maxY - i * stepY;
                                sorted[i].TagHeadPosition = new XYZ(pos.X, targetY, pos.Z);
                            }
                        }
                        break;

                    case AlignMode.DistributeH:
                        {
                            var sorted = _tags.OrderBy(t => t.TagHeadPosition.X).ToList();
                            double minX = sorted.First().TagHeadPosition.X;
                            double maxX = sorted.Last().TagHeadPosition.X;
                            double stepX = (maxX - minX) / (sorted.Count - 1);

                            for (int i = 0; i < sorted.Count; i++)
                            {
                                XYZ pos = sorted[i].TagHeadPosition;
                                double targetX = minX + i * stepX;
                                sorted[i].TagHeadPosition = new XYZ(targetX, pos.Y, pos.Z);
                            }
                        }
                        break;

                    case AlignMode.FixedSpacingV:
                        {
                            double stepFeet = spacingMm / 304.8;
                            var sorted = _tags.OrderByDescending(t => t.TagHeadPosition.Y).ToList();
                            double startY = sorted.First().TagHeadPosition.Y;

                            for (int i = 0; i < sorted.Count; i++)
                            {
                                XYZ pos = sorted[i].TagHeadPosition;
                                double targetY = startY - i * stepFeet;
                                sorted[i].TagHeadPosition = new XYZ(pos.X, targetY, pos.Z);
                            }
                        }
                        break;

                    case AlignMode.FixedSpacingH:
                        {
                            double stepFeet = spacingMm / 304.8;
                            var sorted = _tags.OrderBy(t => t.TagHeadPosition.X).ToList();
                            double startX = sorted.First().TagHeadPosition.X;

                            for (int i = 0; i < sorted.Count; i++)
                            {
                                XYZ pos = sorted[i].TagHeadPosition;
                                double targetX = startX + i * stepFeet;
                                sorted[i].TagHeadPosition = new XYZ(targetX, pos.Y, pos.Z);
                            }
                        }
                        break;
                }

                tx.Commit();
            }
        }

        private void BtnAlignLeft_Click(object sender, RoutedEventArgs e) => ExecuteAlign(AlignMode.Left);
        private void BtnAlignCenterX_Click(object sender, RoutedEventArgs e) => ExecuteAlign(AlignMode.CenterX);
        private void BtnAlignRight_Click(object sender, RoutedEventArgs e) => ExecuteAlign(AlignMode.Right);

        private void BtnAlignTop_Click(object sender, RoutedEventArgs e) => ExecuteAlign(AlignMode.Top);
        private void BtnAlignCenterY_Click(object sender, RoutedEventArgs e) => ExecuteAlign(AlignMode.CenterY);
        private void BtnAlignBottom_Click(object sender, RoutedEventArgs e) => ExecuteAlign(AlignMode.Bottom);

        private void BtnDistributeV_Click(object sender, RoutedEventArgs e) => ExecuteAlign(AlignMode.DistributeV);
        private void BtnDistributeH_Click(object sender, RoutedEventArgs e) => ExecuteAlign(AlignMode.DistributeH);

        private void BtnFixedSpacingV_Click(object sender, RoutedEventArgs e) => ExecuteAlign(AlignMode.FixedSpacingV);
        private void BtnFixedSpacingH_Click(object sender, RoutedEventArgs e) => ExecuteAlign(AlignMode.FixedSpacingH);

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }

    public enum AlignMode
    {
        Left,
        Right,
        CenterX,
        Top,
        Bottom,
        CenterY,
        DistributeV,
        DistributeH,
        FixedSpacingV,
        FixedSpacingH
    }
}
