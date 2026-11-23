import { rackPoint } from './rackGeometry.js';

/**
 * Các tầng của một kệ, tính sẵn đáy và chiều cao từng tầng (mét), từ danh sách rack.levels server trả về.
 * Tầng thường xếp chồng từ dưới lên theo Number: đáy tầng = tổng chiều cao các tầng thường nằm dưới nó.
 * Tầng nóc (isTop) nằm ngay trên tầng thường cao nhất, không giới hạn chiều cao.
 * Trả về { byNumber: Map(number → { bottom, height, isTop }), top: chiều cao tới tấm trên cùng, bottoms: [đáy từng tầng thường] }
 */
export function levelsOf(rack) {
  const normal = rack.levels.filter((l) => !l.isTop).sort((a, b) => a.number - b.number);
  const byNumber = new Map();
  const bottoms = [];
  let bottom = 0;
  for (const level of normal) {
    byNumber.set(level.number, { bottom: bottom, height: level.height, isTop: false });
    bottoms.push(bottom);
    bottom += level.height;
  }
  for (const level of rack.levels.filter((l) => l.isTop)) {
    byNumber.set(level.number, { bottom: bottom, height: level.height, isTop: true });
  }
  return { byNumber: byNumber, top: bottom, bottoms: bottoms };
}

/**
 * Tâm của một khung kệ trong cảnh 3D, bằng mét: { x, y, z } (y là chiều cao, z là toạ độ Y của mặt bằng).
 *
 * - Ngang: ô thứ `bay` (đếm từ 1) có tâm cách đầu kệ (bay - 0.5) × bayWidth; chiều sâu ở giữa: depth / 2.
 *   rackPoint đổi (dọc kệ, sâu) ra điểm trên sàn, đã lo việc xoay 90° và cộng rack.x, rack.y.
 * - Cao: tâm = đáy tầng của khung + nửa chiều cao tầng (mỗi tầng có thể cao khác nhau, lấy từ levelsOf).
 */
export function slotWorldPosition(rack, slot) {
  const { x, z } = rackPoint(rack, (slot.bay - 0.5) * rack.bayWidth, rack.depth / 2);
  const level = levelsOf(rack).byNumber.get(slot.level) ?? { bottom: 0, height: 0 };
  const y = level.bottom + level.height / 2;
  return { x, y, z };
}
