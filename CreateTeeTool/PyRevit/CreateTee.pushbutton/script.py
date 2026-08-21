# -*- coding: utf-8 -*-
"""
Revit Pipe Tee Creator Tool (PyRevit / Revit API Python)
CÔNG CỤ TẠO CÁI TÊ (TEE FITTING) TẠI VỊ TRÍ CHỌN BẤT KỲ TRÊN ỐNG
---------------------------------------------------------------
Tác giả: Antigravity AI Assistant
Mô tả: 
- Cho phép người dùng click chọn 1 điểm bất kỳ trên ống (Pipe).
- Tự động cắt (break) ống tại vị trí click.
- Tự động tạo đoạn ống nhánh (branch pipe stub) hoặc nối với ống nhánh có sẵn.
- Chèn Tê fitting (NewTeeFitting) đúng theo Routing Preference của Pipe Type.
- Hoạt động lặp liên tục (Pick loop) giúp thao tác nhanh chóng cho tới khi nhấn ESC.
"""

import math
import clr

clr.AddReference('RevitAPI')
clr.AddReference('RevitAPIUI')

from Autodesk.Revit.DB import *
from Autodesk.Revit.DB.Plumbing import *
from Autodesk.Revit.UI import *
from Autodesk.Revit.UI.Selection import *
from Autodesk.Revit.Exceptions import OperationCanceledException

# Độ dài mặc định của đoạn ống nhánh tạo ra (Feet). 1 Foot ≈ 304.8 mm
DEFAULT_BRANCH_LENGTH_FEET = 1.0  # 300 mm
MIN_DISTANCE_FROM_ENDPOINT_FEET = 0.2  # ~60 mm để tránh cắt sát đầu ống

def get_connector_at_point(elem, point, tolerance=0.01):
    """
    Lấy Connector của element nằm gần vị trí point nhất
    """
    cm = None
    if hasattr(elem, "ConnectorManager") and elem.ConnectorManager:
        cm = elem.ConnectorManager
    elif hasattr(elem, "MEPModel") and elem.MEPModel and elem.MEPModel.ConnectorManager:
        cm = elem.MEPModel.ConnectorManager

    if not cm:
        return None

    closest_conn = None
    min_dist = float('inf')

    for conn in cm.Connectors:
        dist = conn.Origin.DistanceTo(point)
        if dist < min_dist:
            min_dist = dist
            closest_conn = conn

    if min_dist <= tolerance:
        return closest_conn
    return None


def calculate_branch_direction(pipe_dir, orientation_mode="horizontal"):
    """
    Tính toán hướng vuông góc để đặt nhánh Tê.
    pipe_dir: Vector hướng của ống chính
    orientation_mode: "horizontal" (nhánh ngang) hoặc "up" (nhánh hướng lên) hoặc "down" (nhánh hướng xuống)
    """
    pipe_dir = pipe_dir.Normalize()
    
    # Đơn vị trục Z
    z_axis = XYZ.BasisZ

    # Kiểm tra xem ống chính có đang dựng đứng (vertical) hay không
    is_vertical = math.isclose(abs(pipe_dir.DotProduct(z_axis)), 1.0, rel_tol=1e-3)

    if is_vertical:
        # Nếu ống đứng, nhánh sẽ nằm trong mặt phẳng XOY
        branch_dir = pipe_dir.CrossProduct(XYZ.BasisX)
        if branch_dir.IsZeroLength():
            branch_dir = pipe_dir.CrossProduct(XYZ.BasisY)
    else:
        if orientation_mode == "up":
            branch_dir = z_axis
        elif orientation_mode == "down":
            branch_dir = -z_axis
        else:
            # Mặc định nằm ngang (Horizontal): Tích có hướng giữa Vector ống và Z
            branch_dir = pipe_dir.CrossProduct(z_axis)

    return branch_dir.Normalize()


def create_tee_at_point(doc, pipe, split_point, orientation_mode="horizontal"):
    """
    Thực hiện chia ống và chèn Tê tại split_point
    """
    curve = pipe.Location.Curve
    p0 = curve.GetEndPoint(0)
    p1 = curve.GetEndPoint(1)

    # Kiểm tra điểm split_point không sát đầu/cuối ống
    d0 = p0.DistanceTo(split_point)
    d1 = p1.DistanceTo(split_point)

    if d0 < MIN_DISTANCE_FROM_ENDPOINT_FEET or d1 < MIN_DISTANCE_FROM_ENDPOINT_FEET:
        raise Exception("Vị trí chọn quá gần đầu ống (tối thiểu 60mm từ đầu ống)!")

    # Vector hướng ống
    pipe_dir = (p1 - p0).Normalize()

    # 1. Chia đoạn ống chính thành 2 đoạn tại split_point
    new_pipe_id = PlumbingUtils.BreakCurve(doc, pipe.Id, split_point)
    doc.Regenerate()

    pipe1 = doc.GetElement(pipe.Id)
    pipe2 = doc.GetElement(new_pipe_id)

    # 2. Tìm connector của pipe1 và pipe2 tại split_point
    conn1 = get_connector_at_point(pipe1, split_point, tolerance=0.1)
    conn2 = get_connector_at_point(pipe2, split_point, tolerance=0.1)

    if not conn1 or not conn2:
        raise Exception("Không thể tìm thấy Connector của ống sau khi chia!")

    # 3. Tính toán hướng đoạn ống nhánh
    branch_dir = calculate_branch_direction(pipe_dir, orientation_mode)
    branch_end_point = split_point + (branch_dir * DEFAULT_BRANCH_LENGTH_FEET)

    # Lấy các thông số của ống chính để áp dụng cho ống nhánh
    system_type_id = pipe1.MEPSystem.GetTypeId() if pipe1.MEPSystem else pipe1.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM).AsElementId()
    pipe_type_id = pipe1.PipeType.Id
    level_id = pipe1.LevelId
    diameter = pipe1.Diameter

    # 4. Tạo đoạn ống nhánh ngắn (Stub Pipe)
    branch_pipe = Pipe.Create(doc, system_type_id, pipe_type_id, level_id, split_point, branch_end_point)
    diam_param = branch_pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)
    if diam_param and not diam_param.IsReadOnly:
        diam_param.Set(diameter)
    doc.Regenerate()

    # 5. Lấy connector của ống nhánh tại split_point
    conn_branch = get_connector_at_point(branch_pipe, split_point, tolerance=0.1)

    if not conn_branch:
        raise Exception("Không thể tìm thấy Connector của ống nhánh!")

    # 6. Chèn Tê Fitting (NewTeeFitting)
    tee_fitting = doc.Create.NewTeeFitting(conn1, conn2, conn_branch)

    return tee_fitting


def main():
    uidoc = __revit__.ActiveUIDocument
    doc = uidoc.Document

    created_count = 0

    TaskDialog.Show("Tạo Tê Ống (Pipe Tee)", 
                    "Click chọn điểm trên ống để tạo Tê.\n"
                    "Tool sẽ lặp liên tục. Bấm ESC để hoàn thành.")

    while True:
        try:
            # Cho phép người dùng click chọn trực tiếp vị trí trên ống
            reference = uidoc.Selection.PickObject(
                ObjectType.PointOnElement, 
                "Chọn 1 điểm bất kỳ trên đường ống để tạo Tê (Bấm ESC để dừng)"
            )

            if not reference:
                break

            pipe = doc.GetElement(reference.ElementId)
            if not isinstance(pipe, Pipe):
                TaskDialog.Show("Cảnh báo", "Đối tượng chọn không phải là Đường Ống (Pipe)!")
                continue

            picked_pt = reference.GlobalPoint

            # Project điểm được chọn lên tâm đường ống
            curve = pipe.Location.Curve
            proj_result = curve.Project(picked_pt)
            split_pt = proj_result.XYZPoint

            # Thực hiện trong Transaction
            t = Transaction(doc, "Create Pipe Tee Fitting")
            t.Start()

            try:
                tee = create_tee_at_point(doc, pipe, split_pt, orientation_mode="horizontal")
                t.Commit()
                created_count += 1
            except Exception as ex:
                t.RollBack()
                TaskDialog.Show("Lỗi tạo Tê", "Không thể tạo Tê tại vị trí này:\n" + str(ex))

        except OperationCanceledException:
            # Người dùng bấm ESC
            break
        except Exception as ex:
            TaskDialog.Show("Lỗi", str(ex))
            break

    if created_count > 0:
        TaskDialog.Show("Hoàn thành", "Đã tạo thành công {} cái Tê trên ống!".format(created_count))


if __name__ == "__main__":
    main()
