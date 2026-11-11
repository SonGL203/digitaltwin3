import { api } from '../api/client.js';
import { getState, loadLayout, setState, showMessage } from './store.js';

// Các hành động của người dùng: gọi API → tải lại mặt bằng → báo kết quả.
// Không có luật nghiệp vụ ở đây: đặt được hay không, chiếm chỗ bao nhiêu, bao nhiêu khung… đều do server trả lời.
// Frontend chỉ lo những việc của giao diện: snap theo lưới, sinh mã kệ mới, chờ (debounce) khi gọi validate.

const VALIDATE_DELAY_MS = 120;   // chờ chuột dừng một chút rồi mới hỏi server, không gửi mỗi lần chuột nhích

// ---------- tiện ích ----------

/** Phòng đang mở: mọi API mặt bằng đều nằm dưới /rooms/{roomId}. */
const roomId = () => getState().currentRoomId;

/** Đổi kệ server trả về thành RackRequest để gửi lại (PUT, validate). */
export function rackToRequest(r) {
  return {
    code: r.code, x: r.x, y: r.y, rotation: r.rotation,
    levels: r.levels, levelHeight: r.levelHeight, bays: r.bays, bayWidth: r.bayWidth, depth: r.depth,
    hasTop: r.hasTop, maxWeight: r.maxWeight, productTypeId: r.productTypeId ?? null,
  };
}

/**
 * Hít toạ độ (m) vào bội số của bước lưới gần nhất, không âm. Chỉ là trợ giúp nhập liệu của giao diện:
 * server nhận số mét bất kỳ. toFixed(3) để 0,1 × 3 ra 0,3 chứ không phải 0,30000000000000004.
 */
export function snap(value) {
  const grid = getState().layout?.gridSize ?? 0.5;
  return Math.max(0, Number((Math.round(value / grid) * grid).toFixed(3)));
}

/** Mã kệ mới: K + (số lớn nhất đang có + 1). Frontend phải tự sinh vì validate coi kệ cùng mã là "dời kệ". */
export function nextRackCode() {
  const numbers = (getState().layout?.racks ?? [])
    .map((r) => /^K(\d+)$/i.exec(r.code)?.[1])
    .filter(Boolean)
    .map(Number);
  return `K${Math.max(0, ...numbers) + 1}`;
}

// ---------- bóng mờ (kiểm tra thử) ----------

let validateTimer = null;
let validateSeq = 0;   // số thứ tự lần hỏi; trả lời của lần hỏi cũ (đến muộn) bị bỏ qua

function sameShape(a, b) {
  return a.rotation === b.rotation && a.bays === b.bays && a.bayWidth === b.bayWidth && a.depth === b.depth;
}

/**
 * Hiện bóng mờ cho một kệ rồi hỏi server POST /racks/validate. Bóng xám khi đang chờ, xanh/đỏ khi có trả lời.
 * Kích thước bóng lấy từ server (footprintWidth/Depth); frontend không tự tính, kể cả khi xoay.
 * footprint: gợi ý ban đầu lấy từ chính dữ liệu server (ví dụ kệ đang dời), để bóng hiện ngay không phải chờ.
 */
export function previewRack(request, { rackId = null, source, footprint = null } = {}) {
  const prev = getState().preview;
  const keep = prev && prev.rackId === rackId && sameShape(prev.request, request);
  const width = footprint?.width ?? (keep ? prev.footprintWidth : null);
  const depth = footprint?.depth ?? (keep ? prev.footprintDepth : null);

  setState({
    preview: { request, rackId, source, footprintWidth: width, footprintDepth: depth, isValid: null, reason: null },
  });

  clearTimeout(validateTimer);
  const seq = ++validateSeq;
  validateTimer = setTimeout(async () => {
    try {
      const check = await api.validateRack(roomId(), request);
      if (seq !== validateSeq) return;
      setState({
        preview: {
          ...getState().preview,
          isValid: check.isValid,
          reason: check.reason,
          footprintWidth: check.footprintWidth,
          footprintDepth: check.footprintDepth,
        },
      });
    } catch (err) {
      // Ví dụ 400 vì số tầng = 0 ([Range] của ViewModel): coi như đỏ, lý do là câu lỗi của server.
      if (seq !== validateSeq) return;
      setState({ preview: { ...getState().preview, isValid: false, reason: err.message } });
    }
  }, VALIDATE_DELAY_MS);
}

/** Ngừng hỏi server cho bóng mờ hiện tại nhưng vẫn để bóng hiện (ví dụ lúc đang gửi lệnh lưu). */
export function stopPreviewCheck() {
  validateSeq++;
  clearTimeout(validateTimer);
}

export function clearPreview() {
  validateSeq++;
  clearTimeout(validateTimer);
  if (getState().preview) setState({ preview: null });
}

// ---------- ghi ----------

/** Gọi API ghi; thành công thì tải lại mặt bằng và báo; lỗi thì hiện câu lý do của server. */
async function run(action, success) {
  try {
    const result = await action();
    await loadLayout();
    showMessage(typeof success === 'function' ? success(result) : success);
    return result;
  } catch (err) {
    showMessage(err.message, 'error');
    return null;
  }
}

export const moveRack = (rack, x, y, rotation) =>
  run(() => api.updateRack(roomId(), rack.id, { ...rackToRequest(rack), x, y, rotation }), `Đã dời ${rack.code}.`);

export const rotateRack = (rack) =>
  run(() => api.updateRack(roomId(), rack.id, { ...rackToRequest(rack), rotation: rack.rotation === 90 ? 0 : 90 }),
    `Đã xoay ${rack.code}.`);

export const saveRack = (rack, request) =>
  run(() => api.updateRack(roomId(), rack.id, request), (r) => `Đã lưu ${rack.code}: ${r.slotCount} khung.`);

export async function addRack(request) {
  const created = await run(() => api.addRack(roomId(), request), (r) => `Đã đặt kệ ${r.code}.`);
  if (created) setState({ selectedRackId: created.id, selectedObstacleId: null });
  return created;
}

export const saveRoom = (room) => run(() => api.updateRoom(roomId(), room), 'Đã lưu phòng.');

export async function addObstacle(obstacle) {
  const created = await run(() => api.addObstacle(roomId(), obstacle),
    obstacle.kind === 'Wall' ? 'Đã thêm tường.' : 'Đã thêm cột.');
  if (created) setState({ selectedObstacleId: created.id, selectedRackId: null });
  return created;
}

// Xoá không hoàn tác được: luôn hỏi lại trước. confirm() có sẵn trong trình duyệt, đủ cho đồ án;
// trả false (bấm Huỷ) thì không gửi gì lên server.

export async function removeObstacle(obstacle) {
  const what = obstacle.kind === 'Wall' ? 'tường' : 'cột';
  if (!confirm(`Xoá ${what} tại (${obstacle.x}; ${obstacle.y}) m?`)) return;
  const removed = await run(() => api.removeObstacle(roomId(), obstacle.id), `Đã xoá ${what}.`);
  if (removed) setState({ selectedObstacleId: null });
}

export async function removeRack(rack) {
  if (!confirm(`Xoá kệ ${rack.code} và ${rack.slotCount} khung của nó? Không hoàn tác được.`)) return;
  clearPreview();
  const removed = await run(() => api.removeRack(roomId(), rack.id), `Đã xoá kệ ${rack.code}.`);
  if (removed) setState({ selectedRackId: null });
}
