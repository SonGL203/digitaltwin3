# WarehouseTwin — Kho 3D và tự xếp pallet

Web app mô phỏng kho hàng: cấu hình loại kệ và cỡ pallet, vẽ mặt bằng và đặt kệ, nhập một lô pallet, hệ thống **tự xếp từng pallet vào đúng khung kệ** và hiển thị kết quả trên kho 3D (pallet bay từ cửa nhập vào khung).

Luật nghiệp vụ nằm ở backend ASP.NET Core (C#); frontend Vite + Three.js chỉ hiển thị và gọi API.

> Trạng thái: đang triển khai theo kế hoạch 4 ngày. Robot quét bản đồ tạm bỏ, sẽ thêm lại sau từ code cũ.

## Tính năng

| Có trong phiên bản này | Tạm bỏ |
|---|---|
| Cấu hình cỡ pallet (A/B/C… là dữ liệu, không fix cứng) và loại kệ | Robot quét bản đồ, khôi phục mặt bằng |
| Vẽ mặt bằng trên lưới 2D, đặt kệ bằng chuột | SignalR realtime nhiều tab |
| Sinh khung kệ `K1-T1-R1` từ cấu hình | Đăng nhập, phân quyền |
| Nhập lô pallet theo dài × rộng × cao × nặng | Mã vạch, tem nhãn |
| Tự xếp cả lô: đúng cỡ, tràn lên cỡ lớn hơn, quá cao thì lên nóc | Vật lý, xe nâng lái được |
| Kho 3D: khung tô màu theo nhãn, hiệu ứng pallet bay vào khung | |
| Lưu bố cục và kết quả xếp qua API | |

## Công nghệ

- **Backend:** ASP.NET Core Web API (.NET), EF Core
- **Test:** xUnit, `WebApplicationFactory` cho test API
- **Frontend:** Vite + JavaScript thuần, Three.js (OrbitControls, InstancedMesh), Canvas 2D

## Cách chạy

Yêu cầu: .NET SDK 8 trở lên, Node.js 18 trở lên.

Chạy backend (mặc định phục vụ cả frontend đã build trong `wwwroot`):

```bash
dotnet run --project src/Warehouse.Api
```

Chạy frontend khi phát triển (Vite proxy `/api` sang backend):

```bash
cd frontend && npm install && npm run dev
```

Chạy toàn bộ test:

```bash
dotnet test
```

Build frontend vào `wwwroot` để demo:

```bash
cd frontend && npm run build
```

## Cấu trúc thư mục

```
src/Warehouse.Api/          Web API — project duy nhất cần chạy
├── Entities/               Đồ vật trong kho + luật của chính nó
│   ├── PalletSize.cs       Cỡ A/B/C (dữ liệu cấu hình), Contains()
│   ├── Pallet.cs           Dài, rộng, cao, nặng
│   ├── RackType.cs         Số khung, rộng khung, sâu, cao tầng, số tầng, có nóc
│   ├── Rack.cs             Mã, X, Y, xoay; CreateSlots() sinh K1-T1-R1
│   └── Slot.cs             CanHold(), Place(), Remove(); nhãn AssignedSize
├── Rules/                  Luật cần nhiều đối tượng cùng lúc
│   ├── PalletSizer.cs      Pallet thuộc cỡ nào; sức chứa của loại kệ
│   ├── Allocator.cs        Xếp cả lô: lớn trước, tràn lên cỡ lớn, quá cao lên nóc
│   └── PlacementValidator.cs  Đặt kệ: không ra ngoài, không đè, đủ lối đi
├── Services/               Điều phối: lấy dữ liệu → gọi luật → lưu
│   ├── IPalletService.cs / PalletService.cs
│   └── ILayoutService.cs / LayoutService.cs
├── Data/                   Lưu trữ
│   ├── IWarehouseStore.cs  Một interface cho mọi việc đọc/ghi
│   ├── EfWarehouseStore.cs Bản thật dùng EF Core
│   └── WarehouseDbContext.cs
├── Controllers/            Cửa nhận request
│   ├── PalletsController.cs   POST /api/pallets/allocate, GET /api/pallets
│   └── LayoutController.cs    GET/PUT /api/layout, POST /api/layout/racks
├── Dtos/                   Dữ liệu vào/ra API (có [Range] kiểm tra đầu vào)
├── wwwroot/                Frontend đã build (lúc demo, không commit)
└── Program.cs              Nối dây interface → lớp thật

tests/Warehouse.Tests/
├── Rules/                  SlotTests, AllocatorTests, PlacementValidatorTests
├── Services/               PalletServiceTests (service + kho giả)
├── Api/                    PalletsApiTests (gọi HTTP, service giả)
├── ArchitectureTests.cs    Entities/Rules không dính EF, ASP.NET
├── Fakes/                  InMemoryWarehouseStore (kho giả = List trong RAM)
└── TestData.cs             Kệ mẫu, lô 6A + 2B…

frontend/src/
├── api/                    Gọi backend
├── state/                  Dữ liệu duy nhất của giao diện
├── editor2d/               Vẽ mặt bằng, đặt kệ
├── scene3d/                Three.js: kệ, khung tô màu, pallet bay vào
└── ui/                     Bảng cấu hình, nhập lô, kết quả
```

### Phụ thuộc giữa các tầng

```
Controllers → Services → Rules → Entities
                 ↓
         IWarehouseStore ← EfWarehouseStore (Data)
```

- `Entities` và `Rules` là C# thuần: không dùng EF Core hay ASP.NET (`ArchitectureTests` kiểm tra).
- Controller mỏng: nhận DTO, gọi service, trả kết quả.
- **Frontend không chứa luật**: không tự chọn khung, không tự kiểm tra đặt kệ — mọi quyết định đi qua API.

## Mô hình dữ liệu

Đơn vị: kích thước pallet và khung tính bằng **mét**, khối lượng bằng **kg**; chỉ vị trí kệ tính bằng **ô lưới**.

| Đối tượng | Trường chính |
|---|---|
| `PalletSize` | `Code`, `MaxLength`, `MaxWidth`, `MaxHeight`, `Color` |
| `RackType` | `Bays`, `BayWidth`, `Depth`, `LevelHeight`, `Levels`, `HasTop`, `MaxWeight` |
| `Rack` | `Code`, `TypeId`, `X`, `Y`, `Rotation` |
| `Slot` | `Code`, `Level`, `Bay`, `IsTop`, `Width`, `Depth`, `ClearHeight` (nóc: `null`), `MaxWeight`, `AssignedSize`, `PalletId` |
| `Pallet` | `Code`, `Length`, `Width`, `Height`, `Weight` |
| Mặt bằng | `Cols`, `Rows`, `CellSize`, `Cells` (trống / tường / vật cản), `Racks` |

Chỉ kệ lưu toạ độ. Kích thước khung sinh lại từ `Rack.CreateSlots()`; trạng thái khung (`AssignedSize`, `PalletId`) được lưu riêng.

Mã khung: `K<kệ>-T<tầng>-R<ô>`, tầng và ô đếm từ 1; khung nóc là `K<kệ>-N-R<ô>`. Ví dụ kệ 3 ô × 4 tầng có nóc → 15 khung.

## Luật xếp pallet

Theo tài liệu *Quy tắc xếp pallet vào kệ*:

1. Mọi khung trong một kệ cùng kích thước. **Sức chứa** của kệ là cỡ lớn nhất mà khung chứa được. **Nhãn** (`AssignedSize`) gắn theo từng khung, nên một kệ có thể mang nhiều nhãn.
2. `PalletSizer` trả cỡ nhỏ nhất bao được pallet; không cỡ nào bao được thì pallet chỉ vừa nóc.
3. `Slot.CanHold(pallet)` trả `null` hoặc câu lý do; cho phép xoay pallet 90°; nóc không giới hạn chiều cao.
4. `Allocator.Allocate(pallets, slots, sizes)` trả `{ Placements, Overflowed, ToTop, Rejected }` và **không sửa khung** — `PalletService` mới gọi `Place()` và lưu:
   - Sắp pallet từ cỡ lớn xuống nhỏ (pallet chỉ vừa nóc trước).
   - Mỗi pallet tìm khung trống cùng cỡ; không có thì sang cỡ lớn hơn kế tiếp. **Không bao giờ tràn xuống cỡ nhỏ hơn.**
   - Không chọn "khung khít nhất" cho từng pallet, không xếp theo thứ tự pallet đến.
   - Tất định: duyệt khung theo thứ tự kệ → tầng → ô, không dùng ngẫu nhiên.
5. Thêm cỡ mới (ví dụ D) trong cấu hình phải chạy được **mà không sửa code**.

## API

| Method | Đường dẫn | Việc |
|---|---|---|
| `GET` | `/api/layout` | Lấy mặt bằng, loại kệ, cỡ pallet |
| `PUT` | `/api/layout` | Lưu mặt bằng |
| `POST` | `/api/layout/racks` | Đặt một kệ (kiểm tra bằng `PlacementValidator`) |
| `POST` | `/api/layout/racks/validate` | Kiểm tra vị trí kệ, trả lý do — dùng cho bóng mờ khi kéo chuột |
| `GET` | `/api/pallets` | Danh sách pallet và vị trí |
| `POST` | `/api/pallets/allocate` | Xếp một lô, trả kết quả xếp |

## Lộ trình

- [ ] **Ngày 1 — Entities và luật xếp:** `PalletSize`, `RackType`, `Rack.CreateSlots`, `Slot.CanHold`, `PalletSizer`, `Allocator` + test; lô 6A + 2B xếp đủ 8 pallet. Chưa có EF, chưa có controller.
- [ ] **Ngày 2 — Mặt bằng:** `PlacementValidator` + test, `LayoutService`, `LayoutController`, store tạm; trình vẽ 2D, đặt kệ có bóng mờ hợp lệ/không, phím R xoay.
- [ ] **Ngày 3 — Kho 3D và xếp lô:** `PalletService`, `PalletsController` + test; dựng kệ 3D, khung tô màu theo nhãn, màn Nhập hàng, nút Xếp tự động, hiệu ứng bay.
- [ ] **Ngày 4 — Hoàn thiện:** EF Core, build vào `wwwroot`, sửa lỗi + test hồi quy, trường hợp biên, dữ liệu demo, báo cáo thuật toán, video demo.

Nếu trễ, cắt theo thứ tự: EF Core (giữ store tạm) → hiệu ứng bay → bảng cấu hình trên giao diện → công cụ vẽ tường. Entities, Rules và test của chúng không bao giờ cắt.

## Kịch bản demo (~5 phút)

Vẽ phòng → cấu hình cỡ pallet và loại kệ → đặt kệ → nhập lô 6A + 2B → Xếp tự động trong 3D → thêm cỡ D mà không sửa code.

## Hướng phát triển

- Robot quét bản đồ, khôi phục mặt bằng (dùng lại code cũ).
- Realtime nhiều tab (SignalR), đăng nhập/phân quyền, mã vạch.
