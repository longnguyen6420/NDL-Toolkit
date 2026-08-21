# -*- coding: utf-8 -*-
"""
Tool Đổi Tên Hàng Loạt View Trong Revit (CHỮ IN HOA) - NDL Tools
Batch Rename Selected Views with Level Prefix, Base Name, and Suffix (UPPERCASE)
Author: NDL Tools / Antigravity AI
"""

import sys
import os
import clr

clr.AddReference('RevitAPI')
clr.AddReference('RevitAPIUI')
clr.AddReference('PresentationFramework')
clr.AddReference('PresentationCore')
clr.AddReference('WindowsBase')

from Autodesk.Revit import DB
from Autodesk.Revit.DB import Transaction, ElementId, BuiltInParameter

from pyrevit import revit, DB, UI, forms, script
from System.Windows import Window, Application
from System.Windows.Markup import XamlReader
from System.Collections.ObjectModel import ObservableCollection

doc = revit.doc
uidoc = revit.uidoc
logger = script.get_logger()
output = script.get_output()

# ----------------------------------------------------
# Helper Class for Preview Table
# ----------------------------------------------------
class ViewRenameItem(object):
    def __init__(self, view, level_name, is_selected=True):
        self.view = view
        self.view_id = view.Id
        self.original_name = view.Name
        self.level_name = level_name.upper() if level_name else ""
        self.calculated_name = ""
        self.is_selected = is_selected

# ----------------------------------------------------
# Main WPF Window Class
# ----------------------------------------------------
class RenameViewsWindow(forms.WPFWindow):
    def __init__(self, xaml_file_name, items):
        forms.WPFWindow.__init__(self, xaml_file_name)
        self.items = items
        self.view_rename_collection = ObservableCollection[object]()
        for item in self.items:
            self.view_rename_collection.Add(item)
            
        self.dtg_preview.ItemsSource = self.view_rename_collection
        
        # Default values
        self.txt_basename.Text = "VERTICAL PENETRATION SLEEVE"
        self.chk_level_prefix.IsChecked = True
        self.txt_custom_prefix.Text = ""
        self.txt_suffix.Text = ""
        self.txt_separator.Text = " "
        self.chk_auto_index.IsChecked = True
        
        # Connect Events
        self.txt_basename.TextChanged += self.on_parameter_changed
        self.txt_custom_prefix.TextChanged += self.on_parameter_changed
        self.txt_suffix.TextChanged += self.on_parameter_changed
        self.txt_separator.TextChanged += self.on_parameter_changed
        self.chk_level_prefix.Checked += self.on_parameter_changed
        self.chk_level_prefix.Unchecked += self.on_parameter_changed
        self.chk_auto_index.Checked += self.on_parameter_changed
        self.chk_auto_index.Unchecked += self.on_parameter_changed
        
        self.update_preview()

    def on_parameter_changed(self, sender, e):
        self.update_preview()

    def update_preview(self):
        base_name = self.txt_basename.Text.strip().upper()
        use_level_prefix = self.chk_level_prefix.IsChecked == True
        custom_prefix = self.txt_custom_prefix.Text.upper()
        suffix = self.txt_suffix.Text.upper()
        sep = self.txt_separator.Text
        auto_index = self.chk_auto_index.IsChecked == True

        # Track calculated names to prevent duplicates
        name_counts = {}

        for item in self.items:
            if not getattr(item, 'is_selected', True):
                item.calculated_name = item.original_name.upper()
                continue

            prefix_parts = []
            if use_level_prefix and item.level_name:
                prefix_parts.append(item.level_name.upper())
            if custom_prefix:
                prefix_parts.append(custom_prefix.upper())
                
            prefix_str = sep.join(prefix_parts).upper() if prefix_parts else ""
            
            # Combine parts
            full_parts = []
            if prefix_str:
                full_parts.append(prefix_str)
            if base_name:
                full_parts.append(base_name)
            elif not prefix_str and not suffix:
                full_parts.append(item.original_name.upper())
                
            combined = sep.join(full_parts).upper()
            if suffix:
                combined = combined + sep + suffix if combined else suffix
                
            # Duplicate handling within current selection
            if combined in name_counts:
                name_counts[combined] += 1
                if auto_index:
                    calculated = "{}{}-{:02d}".format(combined, sep, name_counts[combined])
                else:
                    calculated = combined
            else:
                name_counts[combined] = 1
                calculated = combined

            item.calculated_name = calculated.upper()
            
        # Refresh DataGrid
        self.dtg_preview.Items.Refresh()

    def btn_apply_click(self, sender, e):
        self.DialogResult = True
        self.Close()

    def btn_cancel_click(self, sender, e):
        self.DialogResult = False
        self.Close()

# ----------------------------------------------------
# Main Execution Logic
# ----------------------------------------------------
def get_selected_views():
    selected_ids = uidoc.Selection.GetElementIds()
    views = []
    
    if selected_ids:
        for eid in selected_ids:
            elem = doc.GetElement(eid)
            if isinstance(elem, DB.View) and not elem.IsTemplate:
                views.append(elem)
                
    return views

def get_view_level_name(view):
    # 1. GenLevel
    if hasattr(view, "GenLevel") and view.GenLevel:
        return view.GenLevel.Name.upper()
        
    # 2. BuiltInParameter PLAN_VIEW_LEVEL
    param = view.get_Parameter(BuiltInParameter.PLAN_VIEW_LEVEL)
    if param and param.HasValue:
        level_id = param.AsElementId()
        if level_id and level_id != ElementId.InvalidElementId:
            level_elem = doc.GetElement(level_id)
            if level_elem:
                return level_elem.Name.upper()
                
    # 3. LookupParameter "Associated Level" or "Level"
    param_assoc = view.LookupParameter("Associated Level") or view.LookupParameter("Level")
    if param_assoc and param_assoc.HasValue:
        if param_assoc.StorageType == DB.StorageType.ElementId:
            level_id = param_assoc.AsElementId()
            if level_id and level_id != ElementId.InvalidElementId:
                level_elem = doc.GetElement(level_id)
                if level_elem:
                    return level_elem.Name.upper()
        elif param_assoc.StorageType == DB.StorageType.String:
            return (param_assoc.AsString() or "").upper()

    return ""

def sanitize_view_name(name):
    invalid_chars = ['\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~']
    for char in invalid_chars:
        name = name.replace(char, "_")
    return name.upper()

def main():
    views = get_selected_views()
    if not views:
        forms.alert(
            "Vui lòng quét chọn các View trong Project Browser trước khi bấm chạy lệnh!",
            title="NDL Tools - Thông báo"
        )
        return

    items = []
    for v in views:
        lvl_name = get_view_level_name(v)
        items.append(ViewRenameItem(v, lvl_name, is_selected=True))

    xaml_path = os.path.join(os.path.dirname(__file__), "ui.xaml")
    win = RenameViewsWindow(xaml_path, items)
    res = win.show_dialog()

    if not res:
        return

    # Execute Transaction to rename views
    renamed_count = 0
    skipped_count = 0
    
    t = Transaction(doc, "View Rename IN HOA - NDL Tools")
    t.Start()
    
    for item in items:
        if not getattr(item, 'is_selected', True):
            continue
        new_name = sanitize_view_name(item.calculated_name.strip()).upper()
        if not new_name:
            continue
            
        if item.original_name == new_name:
            skipped_count += 1
            continue
            
        try:
            item.view.Name = new_name
            renamed_count += 1
        except Exception as ex:
            logger.error("Không thể đổi tên view '{}' sang '{}': {}".format(item.original_name, new_name, str(ex)))
            skipped_count += 1
            
    t.Commit()

    forms.alert(
        "Đã hoàn thành đổi tên (IN HOA) cho các View được chọn!\n- Thành công: {} view(s)\n- Bỏ qua / Lỗi: {} view(s)".format(renamed_count, skipped_count),
        title="NDL Tools - Thành công"
    )

if __name__ == "__main__":
    main()
