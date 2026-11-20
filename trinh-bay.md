# Kịch bản trình bày WarehouseTwin (5 phút)

> Cột **Nói** là lời nói (đọc vài lần cho quen, không cần thuộc từng chữ).
> Dòng **▶ Làm** là thao tác trên máy cùng lúc đang nói.

---

## Chuẩn bị trước khi vào (không tính giờ)

- [ ] Chạy `dotnet run --project src/Warehouse.Api`, mở http://localhost:5251
- [ ] Có sẵn 3 kho: **một kho trống có kệ cao và kệ thấp** (cho lô 6A + 2B), **Demo loại hàng**, **1234**
- [ ] Màn 3D đang để **Màu theo: Cỡ**
- [ ] Bấm thử toàn bộ một lượt trước giờ trình bày
- [ ] Demo lỗi giữa chừng: nói "em chuyển sang phần tiếp theo", không sửa code trước mặt thầy

---

## 1. Bài toán — 0:00 → 0:40

**Nói:**
> Dạ em chào thầy. Đề tài của em là **WarehouseTwin**, bản sao số của một kho hàng.
>
> Khi một lô hàng về, nhân viên phải tự nghĩ pallet nào vào ô nào. Xếp tuỳ tiện thì pallet dễ tính chiếm mất chỗ của pallet khó tính, hàng cùng loại nằm rải rác, lúc lấy ra phải chạy khắp kho.
>
> Phần mềm của em làm 3 việc: **vẽ mặt bằng kho**, **tự xếp pallet theo luật**, và **xem kết quả trên 3D**.

## 2. Kiến trúc — 0:40 → 1:10

**Nói:**
> Backend là ASP.NET Core với PostgreSQL, frontend dùng Three.js. **Toàn bộ luật nằm ở server**, giao diện chỉ hiển thị. Em tách luật thành các hàm thuần, không dính database, nên test tự động được hết: hiện có hơn 300 test.

## 3. Vẽ mặt bằng — 1:10 → 1:50

▶ **Làm:** mở kho trống → kéo một kệ đè lên cột → bóng mờ đỏ hiện lý do → kéo ra chỗ trống → bóng xanh.

**Nói:**
> Mọi kích thước tính bằng mét. Khi đặt kệ, server kiểm tra theo thứ tự: ra ngoài sàn, đè tường hay cột, đè kệ khác, rồi thiếu lối đi. Không đặt được thì hệ thống nói rõ lý do, như "Kệ đè cột tại…".

## 4. Nhập lô và xếp — 1:50 → 3:20 (phần quan trọng nhất)

▶ **Làm:** Nhập lô hàng → **Lô ví dụ 6A + 2B** → **Kiểm tra các kho**.

**Nói:**
> Em chỉ nhập số lượng và kích thước. Pallet cùng kích thước được tự gom thành type A, B, không phải cấu hình trước. Đây là bước xem trước: chưa ghi gì, chỉ cho biết kho nào nhận được bao nhiêu.

▶ **Làm:** **Nhập vào kho này** → **Xem 3D** → xem pallet được chở vào.

**Nói:**
> B cao, chỉ vừa kệ cao. Nếu xếp theo thứ tự đến, A có thể chiếm hết kệ cao và B mất chỗ. Thuật toán của em xếp **nhóm có ít chỗ vừa nhất trước**, nên cả 8 pallet đều có chỗ.
>
> Thứ tự ưu tiên của em: **vừa khung là bắt buộc**, rồi **xếp được nhiều nhất**, rồi **ít lên nóc nhất**, cuối cùng là **gom gọn**. Thuật toán tất định: chạy hai lần ra cùng một kết quả.

## 5. Loại hàng — 3:20 → 4:10

▶ **Làm:** mở kho **Demo loại hàng** → Kho 3D → bấm **Màu theo: Loại hàng**.

**Nói:**
> Kho thật cần lấy hàng theo loại. Em gom **cùng loại hàng chung một kệ**, trong kệ thì **cùng cỡ chung một tầng**. Đỏ là ốc vít, xanh là bánh xe, xám là hàng chưa phân loại. Lô sau về thì hàng cũng tự tìm về đúng kệ của loại đó.

▶ **Làm (nếu còn giờ):** bấm vào một khung → hiện `K1-T2-R1 · Pallet b82-P001 · type A · Ốc vít`.

**Nói:**
> Mã khung là kệ 1, tầng 2, ô 1. Mã pallet là lô 82, pallet số 1. Vị trí lưu ở khung nên dời pallet không phải đổi mã.

## 6. Kết và giới hạn — 4:10 → 5:00

**Nói:**
> Em làm được: luật đặt kệ, luật xếp theo cỡ và loại hàng, xem trước, nhiều kho, 3D.
>
> Giới hạn em biết:
> - Thuật toán là heuristic, không đảm bảo tối ưu tuyệt đối, vì bài toán xếp hàng là NP-khó.
> - Kệ chỉ đặt 0° hoặc 90°.
> - Hai người bấm xếp cùng lúc có thể đè nhau; cách sửa là khoá dòng phòng trong transaction.
>
> Em cảm ơn thầy ạ.

---

## Dự phòng: thầy hỏi thì trả lời

| Thầy hỏi | Trả lời ngắn |
|---|---|
| Pallet vừa khít có bị từ chối không? | Không. Em so số thực có sai số (`Tolerance`), vì máy tính tính 0,1 + 0,2 ra 0,30000000000000004. |
| Pallet quá cao thì sao? | Lên nóc, vì nóc không giới hạn chiều cao. Hết nóc thì bị từ chối kèm lý do, vẫn thuộc lô để xếp lại sau. |
| Sao không xếp được hết? (kho **1234**) | Pallet cao 1,6 m chỉ vừa nóc, kho có 11 khung nóc nên pallet thứ 12 không có chỗ. Hệ thống không nhét bừa. |
| Ưu tiên loại hàng hay cỡ? | Cỡ là luật cứng (không vừa thì không vào). Trong các khung vừa: loại hàng chọn kệ, cỡ chọn tầng. |
| Sao type tên A, B mà không có nghĩa? | Type là "cùng số đo"; tên chỉ là thứ tự xuất hiện. Nó dùng để gom nhóm, tô màu, xếp nhóm khó trước. |
| Sao mã pallet chỉ có lô và số thứ tự? | Chỉ cần duy nhất và không đổi. Cỡ, loại hàng, vị trí lưu ở bảng riêng. |
| Bấm Xếp hai lần thì sao? | Chỉ xếp pallet chưa có khung, nên không xếp đôi. Cả lần xếp nằm trong một transaction. |
| Sao kệ chéo phải làm tròn? | Cả app (luật va chạm, lối đi, 3D) dùng hình chữ nhật thẳng trục. Kho thật gần như luôn đặt kệ song song tường. |
| Vì sao tách Rules riêng? | Luật là phần quan trọng nhất: hàm thuần thì test được không cần database, controller không lén viết lại luật. |
