using System;
using System.Collections.Generic;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RotateVerticalTool.Services;

namespace RotateVerticalTool.UI
{
    public partial class RotateVerticalWindow : Window
    {
        private Document _doc;
        private ICollection<ElementId> _elementIds;
        private Element _axisPipe;
        private ExternalEvent _exEvent;
        private RotateExternalEventHandler _handler;

        private int _rotationClickCount = 0;
        private double _totalRotatedAngle = 0.0;

        public RotateVerticalWindow(Document doc, ICollection<ElementId> elementIds, Element axisPipe, ExternalEvent exEvent, RotateExternalEventHandler handler)
        {
            InitializeComponent();
            _doc = doc;
            _elementIds = elementIds;
            _axisPipe = axisPipe;
            _exEvent = exEvent;
            _handler = handler;

            lblElementCount.Text = $"Số đối tượng sẽ xoay: {_elementIds.Count} đối tượng";
            lblAxisPipeInfo.Text = $"Ống tim xoay: {axisPipe.Name} (ID: {axisPipe.Id})";

            _handler.Doc = _doc;
            _handler.ElementIds = _elementIds;
            _handler.AxisPipe = _axisPipe;
            _handler.OnExecuted += Handler_OnExecuted;
        }

        private void BtnRotate_Click(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(txtAngle.Text.Trim(), out double angle) || Math.Abs(angle) < 0.001)
            {
                TaskDialog.Show("Rotate Vertical", "Vui lòng nhập góc xoay hợp lệ (ví dụ: 45, 90, 15, -30).");
                return;
            }

            _handler.AngleDegrees = angle;
            _exEvent.Raise();
        }

        private void Handler_OnExecuted(bool success)
        {
            if (success)
            {
                _rotationClickCount++;
                _totalRotatedAngle += _handler.AngleDegrees;
                lblAxisPipeInfo.Text = $"Ống tim xoay: {_axisPipe.Name} (Đã xoay {_rotationClickCount} lần - Tổng: {_totalRotatedAngle:F1}°)";
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_handler != null)
            {
                _handler.OnExecuted -= Handler_OnExecuted;
            }
            base.OnClosed(e);
        }
    }
}
