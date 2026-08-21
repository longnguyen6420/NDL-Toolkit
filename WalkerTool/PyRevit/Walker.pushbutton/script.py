# -*- coding: utf-8 -*-
"""
NDL Walker - Tự động chọn toàn bộ hệ thống đường ống kết nối
Author: NDL Toolkit
"""
from pyrevit import revit, DB, UI, forms
from System.Collections.Generic import List

doc = revit.doc
uidoc = revit.uidoc

def get_connectors(element):
    if hasattr(element, 'ConnectorManager') and element.ConnectorManager:
        return [c for c in element.ConnectorManager.Connectors if c.ConnectorType != DB.ConnectorType.Logical]
    elif hasattr(element, 'MEPModel') and element.MEPModel and element.MEPModel.ConnectorManager:
        return [c for c in element.MEPModel.ConnectorManager.Connectors if c.ConnectorType != DB.ConnectorType.Logical]
    return []

# 1. Check selection or pick
selected_ids = list(uidoc.Selection.GetElementIds())
start_elements = [doc.GetElement(eid) for eid in selected_ids if doc.GetElement(eid)]

if not start_elements:
    try:
        ref = uidoc.Selection.PickObject(UI.Selection.ObjectType.Element, "NDL Walker: Chọn 1 ống hoặc phụ kiện để chọn toàn bộ hệ thống:")
        if ref:
            start_elements = [doc.GetElement(ref)]
    except Exception:
        pass

if not start_elements:
    forms.alert("Chưa chọn đối tượng nào.", title="NDL Walker")
else:
    visited = set()
    queue = list(start_elements)
    
    for elem in start_elements:
        visited.add(elem.Id)
        
    while queue:
        curr = queue.pop(0)
        connectors = get_connectors(curr)
        for conn in connectors:
            if not conn.IsConnected:
                continue
            for ref_conn in conn.AllRefs:
                owner = ref_conn.Owner
                if not owner or owner.Id == curr.Id:
                    continue
                if isinstance(owner, (DB.Plumbing.PipingSystem, DB.Mechanical.MechanicalSystem, DB.Electrical.ElectricalSystem)):
                    continue
                if ref_conn.ConnectorType == DB.ConnectorType.Logical:
                    continue
                if owner.Id not in visited:
                    visited.add(owner.Id)
                    queue.append(owner)

    # Set selection
    id_list = List[DB.ElementId]()
    for eid in visited:
        id_list.Add(eid)
    uidoc.Selection.SetElementIds(id_list)
