# Tool Đổi Tên View (View Rename Tool)

Bộ công cụ đổi tên hàng loạt các View được chọn trong Autodesk Revit theo định dạng chuẩn hóa:
- **Tên gốc chung (Base Name)**: Ví dụ `VERTICAL PENETRATION SLEEVE`.
- **Tiền tố theo Tầng (Level Prefix)**: Tự động đọc Tầng của từng View để gán tiền tố (Ví dụ View ở Level 1 -> `1ST VERTICAL PENETRATION SLEEVE`, Level 2 -> `2ND VERTICAL PENETRATION SLEEVE`).
- **Tiền tố & Hậu tố bổ sung**: Tùy chọn thêm prefix (vd: `ARC_`) hoặc suffix (vd: `_REV01`).
- **Bảng xem trước trực quan (Live Preview)**: Xem tên mới trước khi áp dụng vào Revit.
- **Tự động xử lý trùng tên**: Tự đánh số (-01, -02) nếu trùng tên trong cùng một tầng.
- **Icon 32x32 chuẩn HD**: Tích hợp icon 32x32 cho Ribbon & pyRevit.

---

## 📂 Thư mục dự án

`D:\NDL\ViewRenameTool\`

```
ViewRenameTool/
├── icon.png
├── icon32.png
├── viewrename.png
├── PyRevit/
│   └── ViewRename.pushbutton/
│       ├── bundle.yaml
│       ├── icon.png
│       ├── icon32.png
│       ├── script.py
│       └── ui.xaml
├── CSharp/
│   ├── ViewRenameTool.csproj
│   ├── ViewRenameCommand.cs
│   ├── ViewRenameTool.addin
│   ├── icon32.png
│   ├── ViewModels/
│   │   └── RenameViewModel.cs
│   └── Views/
│       ├── RenameWindow.xaml
│       └── RenameWindow.xaml.cs
├── Dynamo/
│   └── Dynamo_ViewRename.py
└── README.md
```

---

## 🚀 1. Hướng dẫn sử dụng với PyRevit (Khuyên dùng)

### Cách 1: Copy nút bấm vào Extension pyRevit có sẵn
1. Thêm folder `ViewRename.pushbutton` vào bất kỳ extension pyRevit nào của bạn (ví dụ: `%APPDATA%\pyRevit\Extensions\Custom.extension\Custom.tab\Views.panel\ViewRename.pushbutton`).
2. Mở Revit -> Nhấn nút **Reload** của pyRevit.
3. Chọn các View cần đổi tên trong Project Browser -> Bấm nút **View Rename** trên Ribbon.

---

## 🛠️ 2. Hướng dẫn sử dụng Add-in C# WPF (.NET)

1. Mở thư mục `CSharp` và biên dịch bằng `dotnet build` hoặc Visual Studio.
2. Copy `ViewRenameTool.dll` và `ViewRenameTool.addin` vào thư mục Addins của Revit:
   `%APPDATA%\Autodesk\Revit\Addins\2024\` (hoặc 2020-2025).
3. Mở Revit -> Add-in sẽ tự động xuất hiện trong menu NDL Tools / External Tools.

---

## ⚡ 3. Hướng dẫn sử dụng trong Dynamo

1. Mở Dynamo trong Revit.
2. Tạo một **Python Script** node và copy toàn bộ nội dung file `Dynamo/Dynamo_ViewRename.py`.
3. Nối đầu vào:
   - `IN[0]`: Danh sách Views (Select Model Elements hoặc views node).
   - `IN[1]`: Tên gốc `"VERTICAL PENETRATION SLEEVE"`.
   - `IN[2]`: `True` (Tự động lấy Level làm Prefix).
   - `IN[3]`: Tiền tố bổ sung (nếu có, vd: `""`).
   - `IN[4]`: Hậu tố bổ sung (nếu có, vd: `""`).
   - `IN[5]`: Phân cách `" "`.
4. Bấm **Run** để đổi tên View.
