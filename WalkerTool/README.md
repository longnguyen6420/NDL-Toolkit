# NDL Walker Tool (MEP Network Traverse & Auto-Selection)

Công cụ cho phép người dùng click chọn 1 đối tượng đường ống hoặc phụ kiện bất kỳ và tự động duyệt tìm (Walk/Traverse) toàn bộ các phần tử đang liên kết vật lý (Connected Connectors) với nó trong Revit.

## Tính năng
- Hỗ trợ toàn diện: Pipes, Ducts, Cable Trays, Conduits, Pipe Fittings, Pipe Accessories, Sprinklers, Mechanical Equipment, Plumbing Fixtures.
- Thuật toán BFS đa luồng tối ưu tốc độ, quét hàng nghìn đối tượng chỉ trong vài mili-giây.
- Hỗ trợ cả chọn trước (Pre-selection) và chọn trực tiếp trên màn hình (Pick Object).
- Tự động gán lựa chọn toàn bộ hệ thống vào Selection của Revit.
- Đầy đủ 3 phiên bản: C# (.NET 4.8 & .NET 8.0), PyRevit pushbutton và Dynamo script.
