using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RotateVerticalTool.Services
{
    public class RotateExternalEventHandler : IExternalEventHandler
    {
        public Document Doc { get; set; }
        public ICollection<ElementId> ElementIds { get; set; }
        public Element AxisPipe { get; set; }
        public double AngleDegrees { get; set; }

        public event Action<bool> OnExecuted;

        public void Execute(UIApplication app)
        {
            if (Doc == null || ElementIds == null || AxisPipe == null) return;

            bool success = RotateVerticalService.RotateGroupAroundPipeAxis(Doc, ElementIds, AxisPipe, AngleDegrees);
            OnExecuted?.Invoke(success);
        }

        public string GetName()
        {
            return "Rotate Group Around Pipe Axis Event Handler";
        }
    }
}
