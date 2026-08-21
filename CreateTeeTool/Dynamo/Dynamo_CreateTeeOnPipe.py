# -*- coding: utf-8 -*-
"""
Dynamo Python Script: Create Pipe Tee at Selected Point
Sử dụng trong node "Python Script" của Dynamo Revit
Input:
- IN[0]: Target Pipe (Element)
- IN[1]: Point (XYZ hoặc Dynamo Point - vị trí muốn đặt Tê trên ống)
- IN[2]: Direction Mode ("horizontal", "up", "down") [Tùy chọn]
Output:
- OUT: Tee Fitting (FamilyInstance)
"""

import clr
import math

clr.AddReference('RevitServices')
import RevitServices
from RevitServices.Persistence import DocumentManager
from RevitServices.Transactions import TransactionManager

clr.AddReference('RevitAPI')
from Autodesk.Revit.DB import *
from Autodesk.Revit.DB.Plumbing import *

doc = DocumentManager.Instance.CurrentDBDocument

# Nhận input từ Dynamo
pipe_elem = UnwrapElement(IN[0]) if IN[0] else None
dynamo_point = IN[1]
orientation_mode = IN[2] if len(IN) > 2 and IN[2] else "horizontal"

DEFAULT_BRANCH_LENGTH_FEET = 1.0 # 300 mm
MIN_DISTANCE_FEET = 0.2

def dynamo_pt_to_xyz(pt):
    if hasattr(pt, "X") and hasattr(pt, "Y") and hasattr(pt, "Z"):
        # Chuyển đổi mm sang Feet nếu Dynamo đang dùng mm
        # Revit API mặc định dùng Feet
        return XYZ(pt.X / 304.8 if abs(pt.X) > 10 else pt.X, 
                   pt.Y / 304.8 if abs(pt.Y) > 10 else pt.Y, 
                   pt.Z / 304.8 if abs(pt.Z) > 10 else pt.Z)
    return pt

def get_connector_at_point(elem, point, tolerance=0.1):
    cm = None
    if hasattr(elem, "ConnectorManager") and elem.ConnectorManager:
        cm = elem.ConnectorManager
    elif hasattr(elem, "MEPModel") and elem.MEPModel and elem.MEPModel.ConnectorManager:
        cm = elem.MEPModel.ConnectorManager

    if not cm: return None
    closest = None
    min_dist = float('inf')
    for conn in cm.Connectors:
        dist = conn.Origin.DistanceTo(point)
        if dist < min_dist:
            min_dist = dist
            closest = conn
    return closest if min_dist <= tolerance else None

def calculate_branch_dir(pipe_dir, mode="horizontal"):
    pipe_dir = pipe_dir.Normalize()
    z_axis = XYZ.BasisZ
    if math.isclose(abs(pipe_dir.DotProduct(z_axis)), 1.0, rel_tol=1e-3):
        b_dir = pipe_dir.CrossProduct(XYZ.BasisX)
        if b_dir.IsZeroLength(): b_dir = pipe_dir.CrossProduct(XYZ.BasisY)
    else:
        if mode == "up": b_dir = z_axis
        elif mode == "down": b_dir = -z_axis
        else: b_dir = pipe_dir.CrossProduct(z_axis)
    return b_dir.Normalize()

result_tee = None

if pipe_elem and isinstance(pipe_elem, Pipe) and dynamo_point:
    picked_xyz = dynamo_pt_to_xyz(dynamo_point)
    curve = pipe_elem.Location.Curve
    split_pt = curve.Project(picked_xyz).XYZPoint

    p0 = curve.GetEndPoint(0)
    p1 = curve.GetEndPoint(1)
    pipe_dir = (p1 - p0).Normalize()

    TransactionManager.Instance.EnsureInTransaction(doc)
    try:
        # Break Pipe
        new_pipe_id = PlumbingUtils.BreakCurve(doc, pipe_elem.Id, split_pt)
        doc.Regenerate()

        pipe1 = pipe_elem
        pipe2 = doc.GetElement(new_pipe_id)

        conn1 = get_connector_at_point(pipe1, split_pt)
        conn2 = get_connector_at_point(pipe2, split_pt)

        branch_dir = calculate_branch_dir(pipe_dir, orientation_mode)
        branch_end = split_pt + (branch_dir * DEFAULT_BRANCH_LENGTH_FEET)

        system_type_id = pipe1.MEPSystem.GetTypeId() if pipe1.MEPSystem else pipe1.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM).AsElementId()
        pipe_type_id = pipe1.PipeType.Id
        level_id = pipe1.LevelId
        diameter = pipe1.Diameter

        branch_pipe = Pipe.Create(doc, system_type_id, pipe_type_id, level_id, split_pt, branch_end)
        branch_pipe.Diameter = diameter
        doc.Regenerate()

        conn_branch = get_connector_at_point(branch_pipe, split_pt)

        result_tee = doc.Create.NewTeeFitting(conn1, conn2, conn_branch)
    except Exception as ex:
        result_tee = "Error: " + str(ex)

    TransactionManager.Instance.TransactionTaskDone()

OUT = result_tee
