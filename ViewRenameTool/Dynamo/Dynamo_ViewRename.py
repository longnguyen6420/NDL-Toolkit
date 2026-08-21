# -*- coding: utf-8 -*-
"""
Dynamo Python Script: View Rename (Batch Rename Selected Views with Level Prefix and Base Name)
Author: Antigravity AI

Inputs:
  IN[0]: Views (List of Revit View elements)
  IN[1]: BaseName (String, e.g., "VERTICAL PENETRATION SLEEVE")
  IN[2]: UseLevelPrefix (Boolean, True/False)
  IN[3]: CustomPrefix (String, optional, e.g., "ARC_")
  IN[4]: Suffix (String, optional, e.g., "_REV01")
  IN[5]: Separator (String, optional, default " ")
"""

import clr
clr.AddReference('RevitServices')
clr.AddReference('RevitAPI')

from RevitServices.Persistence import DocumentManager
from RevitServices.Transactions import TransactionManager
import Autodesk.Revit.DB as DB

doc = DocumentManager.Instance.CurrentDBDocument

# Unwrap Dynamo Elements
def Unwrap(item):
    return UnwrapElement(item) if hasattr(item, "__iter__") is False else [UnwrapElement(i) for i in item]

views = UnwrapElement(IN[0]) if isinstance(IN[0], list) else [UnwrapElement(IN[0])]
base_name = IN[1] if IN[1] else "VERTICAL PENETRATION SLEEVE"
use_level_prefix = bool(IN[2]) if len(IN) > 2 and IN[2] is not None else True
custom_prefix = str(IN[3]) if len(IN) > 3 and IN[3] is not None else ""
suffix = str(IN[4]) if len(IN) > 4 and IN[4] is not None else ""
sep = str(IN[5]) if len(IN) > 5 and IN[5] is not None else " "

def get_view_level_name(v):
    if hasattr(v, "GenLevel") and v.GenLevel:
        return v.GenLevel.Name
    p1 = v.get_Parameter(DB.BuiltInParameter.PLAN_VIEW_LEVEL)
    if p1 and p1.HasValue:
        l_elem = doc.GetElement(p1.AsElementId())
        if l_elem: return l_elem.Name
    p2 = v.get_Parameter(DB.BuiltInParameter.VIEW_ASSOCIATED_LEVEL)
    if p2 and p2.HasValue:
        l_elem = doc.GetElement(p2.AsElementId())
        if l_elem: return l_elem.Name
    return ""

def sanitize(name):
    for c in ['\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~']:
        name = name.replace(c, "_")
    return name

results = []
name_tracker = {}

TransactionManager.Instance.EnsureInTransaction(doc)

for v in views:
    if not isinstance(v, DB.View) or v.IsTemplate:
        continue
        
    lvl_name = get_view_level_name(v)
    prefix_parts = []
    if use_level_prefix and lvl_name:
        prefix_parts.append(lvl_name)
    if custom_prefix:
        prefix_parts.append(custom_prefix)
        
    prefix_str = sep.join(prefix_parts) if prefix_parts else ""
    full_parts = []
    if prefix_str: full_parts.append(prefix_str)
    if base_name: full_parts.append(base_name)
    
    combined = sep.join(full_parts)
    if suffix:
        combined = combined + sep + suffix if combined else suffix
        
    if combined in name_tracker:
        name_tracker[combined] += 1
        final_name = "{}{}-{:02d}".format(combined, sep, name_tracker[combined])
    else:
        name_tracker[combined] = 1
        final_name = combined
        
    final_name = sanitize(final_name.strip())
    
    orig_name = v.Name
    try:
        if orig_name != final_name:
            v.Name = final_name
            results.append("SUCCESS: '{}' -> '{}'".format(orig_name, final_name))
        else:
            results.append("UNCHANGED: '{}'".format(orig_name))
    except Exception as ex:
        results.append("ERROR: '{}' -> '{}' ({})".format(orig_name, final_name, str(ex)))

TransactionManager.Instance.TransactionTaskDone()

OUT = results
