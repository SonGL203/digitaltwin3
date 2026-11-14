import { OCCUPIED } from './gridMap.js';

// Bản đồ lưới (robot thấy gì) → mặt bằng (tường, cột, kệ bằng mét), cùng dạng với GET /api/rooms/{id}.
// Kết quả là BẢN NHÁP cho người dùng sửa rồi bấm Lưu nháp (PUT /layout có sẵn kiểm tra luật đặt kệ);
// hàm này không lưu gì, không gọi API.
//
// Ba bước:
// 1. Gom ô có vật CAO (≥ minHeight) liền nhau (8 hướng) thành từng khối. Vật thấp (dây điện…) là đồ vương vãi
//    trên sàn, không thuộc thiết kế kho nên bỏ.
// 2. Mỗi khối lấy hình chữ nhật bao. Cảm biến chỉ thấy MẶT NGOÀI của kệ nên khối kệ là một "vòng" ô rỗng ruột;
//    hình chữ nhật bao của vòng chính là đáy kệ. 8 hướng (không phải 4) để vòng đi chéo ở góc không bị đứt.
// 3. Phân loại theo hình dạng (chiều cao không phân biệt được: tường, cột, kệ đều cao 2,5–3,6 m):
//    - cả hai cạnh nhỏ (≤ maxColumnSize) → cột
//    - cạnh ngắn mảnh (≤ maxWallThickness) → tường
//    - còn lại → kệ
//
// Độ chính xác: ô nào có một phần vật là ô vật (gridMap.js), nên mỗi cạnh có thể to hơn thật tối đa một ô (0,1 m).
// Vật thẳng lưới 0,1 m thì ra đúng.
//
// Giới hạn: hai vật chạm nhau (kệ sát tường ngăn, hai kệ liền MinGap = 0) gộp thành một khối; kệ bị khuất một mặt
// thì đáy bị hụt. Robot KHÔNG biết số tầng (chỉ đo được tổng chiều cao): số tầng, chiều cao tầng, số ô là đoán
// theo cỡ mặc định, đánh dấu `needsReview` để giao diện bắt người dùng xác nhận.

export const DEFAULT_CONVERT = {
  minHeight: 0.3,          // thấp hơn thì coi là đồ trên sàn, bỏ
  minCells: 3,             // khối nhỏ hơn: nhiễu, bỏ (báo lại trong `ignored`)
  maxColumnSize: 0.8,      // cột: cả hai cạnh ≤ 0,8 m
  maxWallThickness: 0.3,   // tường: cạnh ngắn ≤ 0,3 m
  bayWidth: 1.2,           // đoán số ô của kệ theo cỡ ô này
  levelHeight: 1.2,        // đoán số tầng theo chiều cao tầng này
  hasTop: true,
  maxWeight: 1000,
};

/**
 * @returns {{ width, depth, racks: Array, obstacles: Array, ignored: Array<{x, y, width, depth, cells}> }}
 *  racks có cùng trường với RackRequest (+ footprintWidth/Depth, measuredHeight, needsReview)
 */
export function mapToLayout(map, options = {}) {
  const opt = { ...DEFAULT_CONVERT, ...options };
  const rects = findBlobs(map, opt.minHeight).map((blob) => ({ ...boundingRect(map, blob), cells: blob.cells.length, height: blob.height }));

  // Thứ tự tất định: trên xuống, trái sang (theo góc trên trái). Mã kệ K1, K2… đi theo thứ tự này.
  rects.sort((a, b) => a.y - b.y || a.x - b.x);

  const racks = [];
  const obstacles = [];
  const ignored = [];
  for (const r of rects) {
    const rect = { x: r.x, y: r.y, width: r.width, depth: r.depth };
    const short = Math.min(r.width, r.depth);
    const long = Math.max(r.width, r.depth);
    if (r.cells < opt.minCells) ignored.push({ ...rect, cells: r.cells });
    else if (long <= opt.maxColumnSize + 1e-9) obstacles.push({ kind: 'Column', ...rect });
    else if (short <= opt.maxWallThickness + 1e-9) obstacles.push({ kind: 'Wall', ...rect });
    else racks.push(toRack(r, racks.length + 1, opt));
  }
  return { width: map.width, depth: map.depth, racks, obstacles, ignored };
}

/** Khối ô có vật cao liền nhau (8 hướng). Mỗi khối: { cells, minC, maxC, minR, maxR, height } */
export function findBlobs(map, minHeight = DEFAULT_CONVERT.minHeight) {
  const { cols, rows, state, height } = map;
  const solid = (i) => state[i] === OCCUPIED && height[i] >= Math.fround(minHeight);
  const seen = new Uint8Array(state.length);
  const blobs = [];

  for (let start = 0; start < state.length; start++) {
    if (seen[start] || !solid(start)) continue;
    // Loang bằng ngăn xếp (không đệ quy: khối tường dài hàng trăm ô sẽ tràn ngăn xếp gọi hàm).
    const blob = { cells: [], minC: Infinity, maxC: -Infinity, minR: Infinity, maxR: -Infinity, height: 0 };
    const stack = [start];
    seen[start] = 1;
    while (stack.length > 0) {
      const i = stack.pop();
      const c = i % cols;
      const r = Math.floor(i / cols);
      blob.cells.push(i);
      blob.minC = Math.min(blob.minC, c);
      blob.maxC = Math.max(blob.maxC, c);
      blob.minR = Math.min(blob.minR, r);
      blob.maxR = Math.max(blob.maxR, r);
      blob.height = Math.max(blob.height, height[i]);
      for (let dr = -1; dr <= 1; dr++) {
        for (let dc = -1; dc <= 1; dc++) {
          const nc = c + dc;
          const nr = r + dr;
          if (nc < 0 || nr < 0 || nc >= cols || nr >= rows) continue;
          const n = nr * cols + nc;
          if (!seen[n] && solid(n)) {
            seen[n] = 1;
            stack.push(n);
          }
        }
      }
    }
    blobs.push(blob);
  }
  return blobs;
}

/** Hình chữ nhật bao (mét) của một khối: từ mép trái ô trái nhất tới mép phải ô phải nhất. Làm tròn 3 chữ số cho gọn. */
function boundingRect(map, blob) {
  const cs = map.cellSize;
  return {
    x: round(blob.minC * cs),
    y: round(blob.minR * cs),
    width: round((blob.maxC - blob.minC + 1) * cs),
    depth: round((blob.maxR - blob.minR + 1) * cs),
  };
}

/**
 * Hình chữ nhật → cấu hình kệ. Dài chạy theo trục nào thì xoay theo trục đó (rộng ≥ sâu: 0°, ngược lại 90°),
 * khớp Rack.GetBounds ở backend: x, y là góc trên trái của đáy, xoay 90° đổi chỗ dài/sâu.
 * Số ô và số tầng là ĐOÁN: chọn số nguyên gần nhất với cỡ mặc định, rồi chia đều để tổng khớp đúng số đo
 * (bays × bayWidth = chiều dài đo được, levels × levelHeight = chiều cao đo được).
 */
function toRack(r, number, opt) {
  const rotation = r.width >= r.depth ? 0 : 90;
  const length = rotation === 0 ? r.width : r.depth;
  const depth = rotation === 0 ? r.depth : r.width;
  const bays = clamp(Math.round(length / opt.bayWidth), 1, 50);
  const measuredHeight = round(r.height);
  const levels = clamp(Math.round(measuredHeight / opt.levelHeight), 1, 20);
  return {
    code: `K${number}`,
    x: r.x, y: r.y, rotation,
    bays, bayWidth: round(length / bays), depth,
    levels, levelHeight: round(measuredHeight / levels),
    hasTop: opt.hasTop, maxWeight: opt.maxWeight, productTypeId: null,
    footprintWidth: r.width, footprintDepth: r.depth,
    measuredHeight,
    needsReview: true,   // số tầng / số ô là đoán: người dùng phải xác nhận
  };
}

const round = (v) => Math.round(v * 1000) / 1000;
const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
