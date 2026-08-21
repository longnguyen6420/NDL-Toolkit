# Dynamo Python Script: NDL MEP Network Walker
import clr
clr.AddReference('RevitAPI')
clr.AddReference('RevitAPIUI')
clr.AddReference('RevitServices')

from Autodesk.Revit.DB import *
from Autodesk.Revit.DB.Plumbing import *
from Autodesk.Revit.DB.Mechanical import *
from RevitServices.Persistence import DocumentManager

doc = DocumentManager.Instance.CurrentDBDocument

# IN[0]: Start Element(s) or ElementId(s)
input_items = IN[0] if isinstance(IN[0], list) else [IN[0]]

def get_connectors(elem):
    if hasattr(elem, 'ConnectorManager') and elem.ConnectorManager:
        return [c for c in elem.ConnectorManager.Connectors if c.ConnectorType != ConnectorType.Logical]
    elif hasattr(elem, 'MEPModel') and elem.MEPModel and elem.MEPModel.ConnectorManager:
        return [c for c in elem.MEPModel.ConnectorManager.Connectors if c.ConnectorType != ConnectorType.Logical]
    return []

visited = set()
queue = []

for item in input_items:
    elem = UnwrapElement(item)
    if elem and elem.Id not in visited:
        visited.add(elem.Id)
        queue.append(elem)

while queue:
    curr = queue.pop(0)
    for conn in get_connectors(curr):
        if not conn.IsConnected:
            continue
        for ref_conn in conn.AllRefs:
            owner = ref_conn.Owner
            if not owner or owner.Id == curr.Id:
                continue
            if isinstance(owner, (PipingSystem, MechanicalSystem)):
                continue
            if ref_conn.ConnectorType == ConnectorType.Logical:
                continue
            if owner.Id not in visited:
                visited.add(owner.Id)
                queue.append(owner)

OUT = [doc.GetElement(eid) for eid in visited]
