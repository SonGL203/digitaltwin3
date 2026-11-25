import { getJson, sendJson } from '../shared/api.js';
import { askConfirm } from '../shared/dialog.js';
import { showMessage } from '../shared/message.js';

// DỮ LIỆU của trang Mặt bằng + các hành động sửa nó.
//
// Cách làm việc: tải kho từ server về thành BẢN NHÁP (state.room). Mọi thao tác vẽ (đặt, dời, xoay, xoá kệ, tường, cột,
// sửa phòng) chỉ sửa bản nháp trong bộ nhớ, KHÔNG gọi server. Bấm "Lưu nháp" mới gửi cả bản nháp lên (PUT).
// Đối tượng mới vẽ chưa có Id của server nên mang Id TẠM ÂM (-1, -2…); lúc lưu gửi id = null.
//
// Không có luật nghiệp vụ ở đây: đặt được hay không, kệ chiếm chỗ bao nhiêu… đều do server trả lời
// (POST /layout/validate cho bóng mờ, PUT /layout kiểm tra lần cuối). Frontend chỉ lo việc của giao diện:
// "hít" theo lưới, sinh mã kệ mới, Id tạm, chờ chuột dừng rồi mới hỏi server.

export const state = {
  roomId: null,
  room: null,               // bản nháp: cùng dạng với GET /api/rooms/{id} (đơn vị mét)
  dirty: false,             // bản nháp có thay đổi chưa lưu
  saving: false,
  draftIssue: null,         // lỗi đầu tiên khi server kiểm tra cả bản nháp ("K3: Kệ đè cột…"), null = ổn
  selectedRackId: null,
  selectedObstacleId: null,
  tool: 'select',           // 'select' (chọn/dời) | 'add-rack' (đặt kệ) | 'add-column' (đặt cột)
  showGrid: true,
  // Bóng mờ của kệ đang kéo / sắp đặt / đang sửa trong form:
  // { request, rackId, source: 'drag' | 'add' | 'form', footprintWidth, footprintDepth, isValid, reason }
  // isValid = null là đang chờ server trả lời.
  preview: null,
  // Cấu hình kệ mới khi dùng công cụ "Đặt kệ". Mã kệ tự sinh lúc đặt.
  newRack: { rotation: 0, levels: 3, levelHeight: 1.2, bays: 4, bayWidth: 1.2, depth: 1.0, hasTop: true, maxWeight: 1000 },
  // Đặt dãy kệ (công cụ 'add-array'): rows hàng × perRow kệ, mỗi kệ theo cấu hình newRack.
  // gapAlong = khe giữa hai kệ cạnh nhau trong một hàng, aisle = lối đi giữa hai hàng; null = lấy lối đi tối thiểu của phòng.
  newArray: { rows: 2, perRow: 3, gapAlong: null, aisle: null },
  // Bóng mờ của cả dãy: { requests: [...kệ], results: null | [{ isValid, reason, footprintWidth, footprintDepth }], reason }
  // results = null là đang chờ server trả lời.
  arrayPreview: null,
};

// ---------- báo cho giao diện biết dữ liệu vừa đổi ----------
// Trang đăng ký MỘT hàm bằng onChange(). Mỗi lần dữ liệu đổi, hàm đó được gọi với `what`:
//   'draft'     bản nháp đổi (tải lại, sửa, thêm, xoá)  → vẽ lại cả bảng bên
//   'selection' đổi kệ/cột đang chọn                      → vẽ lại bảng bên
//   'preview'   bóng mờ đổi                               → chỉ vẽ lại canvas và dòng kiểm tra
//   'status'    trạng thái lưu, lỗi bản nháp, kích thước  → chỉ vẽ lại canvas và thanh bản nháp
//   'view'      đổi công cụ, bật/tắt lưới                  → chỉ vẽ lại canvas và thanh công cụ
let listener = function () {};
export function onChange(fn) {
  listener = fn;
}
function notify(what) {
  listener(what);
}

// ---------- tiện ích ----------

/** Đổi kệ trong bản nháp thành dữ liệu gửi server (kiểm tra thử, lưu). */
export function rackToRequest(r) {
  return {
    code: r.code, x: r.x, y: r.y, rotation: r.rotation,
    levels: r.levels, levelHeight: r.levelHeight, bays: r.bays, bayWidth: r.bayWidth, depth: r.depth,
    hasTop: r.hasTop, maxWeight: r.maxWeight, productTypeId: r.productTypeId ?? null,
  };
}

/** Cả bản nháp thành dữ liệu gửi server. Đối tượng mới (Id âm) gửi id = null. */
export function toLayoutRequest(room) {
  return {
    name: room.name, width: room.width, depth: room.depth, height: room.height,
    gridSize: room.gridSize, minGap: room.minGap, margin: room.margin,
    version: room.version,   // số phiên bản lúc tải: server so để biết có ai lưu trước mình không
    obstacles: room.obstacles.map((o) => ({
      id: o.id > 0 ? o.id : null, kind: o.kind, x: o.x, y: o.y, width: o.width, depth: o.depth,
    })),
    racks: room.racks.map((r) => ({ id: r.id > 0 ? r.id : null, ...rackToRequest(r) })),
  };
}

/**
 * "Hít" toạ độ (m) vào bội số của bước lưới gần nhất, không âm. Chỉ là trợ giúp nhập liệu của giao diện:
 * server nhận số mét bất kỳ. toFixed(3) để 0,1 × 3 ra 0,3 chứ không phải 0,30000000000000004.
 */
export function snap(value) {
  const grid = state.room?.gridSize ?? 0.5;
  return Math.max(0, Number((Math.round(value / grid) * grid).toFixed(3)));
}

/** Mã kệ mới: K + (số lớn nhất đang có trong bản nháp + 1). Phải tự sinh vì hai kệ cùng mã bị server từ chối. */
export function nextRackCode() {
  const numbers = (state.room?.racks ?? [])
    .map((r) => /^K(\d+)$/i.exec(r.code)?.[1])
    .filter(Boolean)
    .map(Number);
  return `K${Math.max(0, ...numbers) + 1}`;
}

export function selectedRack() {
  return state.room?.racks.find((r) => r.id === state.selectedRackId) ?? null;
}

export function selectedObstacle() {
  return state.room?.obstacles.find((o) => o.id === state.selectedObstacleId) ?? null;
}

// ---------- tải, lưu, bỏ thay đổi ----------

/** Tải kho từ server thành bản nháp mới (mất mọi thay đổi chưa lưu). Lỗi thì ném ra cho nơi gọi xử lý. */
export async function loadRoom() {
  const room = await getJson(`/api/rooms/${state.roomId}`);
  state.room = room;
  state.dirty = false;
  state.draftIssue = null;
  state.preview = null;
  if (!room.racks.some((r) => r.id === state.selectedRackId)) state.selectedRackId = null;
  if (!room.obstacles.some((o) => o.id === state.selectedObstacleId)) state.selectedObstacleId = null;
  notify('draft');
}

/** Gửi cả bản nháp lên server (một lần, một transaction). Lỗi thì giữ nguyên bản nháp để sửa tiếp. */
export async function saveDraft() {
  if (!state.room || !state.dirty || state.saving) return;
  state.saving = true;
  notify('status');
  try {
    // Server kiểm tra luật ngay (400 thì ném lỗi), rồi ghi database ở nền: hỏi tiến độ tới khi xong
    const job = await sendJson('PUT', `/api/rooms/${state.roomId}/layout`, toLayoutRequest(state.room));
    await waitForSave(job);
    state.saving = false;
    await loadRoom();   // tải lại: Id thật của kệ mới, số khung do server sinh, version mới
    showMessage('Đã lưu nháp.');
  } catch (err) {
    state.saving = false;
    notify('status');
    showMessage(err.message, true);
  }
}

/**
 * Hỏi GET /layout/save tới khi lưu xong. job: { status, total, built, error }.
 * Mất mạng một lúc thì thử lại; Conflict / Failed thì ném Error kèm câu lý do của server.
 */
async function waitForSave(job) {
  while (job.status === 'Queued' || job.status === 'Running') {
    await new Promise((resolve) => setTimeout(resolve, 1000));
    try {
      job = await getJson(`/api/rooms/${state.roomId}/layout/save`);
    } catch (err) {
      if (err.message === 'Không kết nối được máy chủ.') continue;   // mất mạng: hỏi lại
      throw err;                                                     // 404: server khởi động lại, lần lưu bị bỏ
    }
  }
  if (job.status !== 'Done') throw new Error(job.error ?? 'Lưu không thành công.');
}

/** Bỏ mọi thay đổi chưa lưu, lấy lại kho như đã lưu trên server. */
export async function discardDraft() {
  if (!state.dirty) return;
  if (!await askConfirm('Bỏ mọi thay đổi chưa lưu?', { okText: 'Bỏ thay đổi', danger: true })) return;
  clearPreview();
  try {
    await loadRoom();
    showMessage('Đã bỏ thay đổi, quay lại bản đã lưu.');
  } catch (err) {
    showMessage(err.message, true);
  }
}

// ---------- chọn, công cụ ----------

export function select(rackId, obstacleId) {
  state.selectedRackId = rackId;
  state.selectedObstacleId = obstacleId;
  if (state.preview?.source === 'form') state.preview = null;   // bóng của form kệ cũ không còn đúng
  notify('selection');
}

export function setTool(tool) {
  state.tool = tool;
  notify('view');
}

export function setShowGrid(show) {
  state.showGrid = show;
  notify('view');
}

// ---------- sửa bản nháp (chưa gọi server) ----------

let tempId = 0;
const newTempId = () => --tempId;   // -1, -2, … không bao giờ trùng Id thật (luôn dương)

/** Sửa bản nháp: nhân bản state.room, áp `change` lên bản nhân bản, đánh dấu chưa lưu, rồi nhờ server kiểm tra lại. */
function edit(change) {
  const room = structuredClone(state.room);
  change(room);
  state.room = room;
  state.dirty = true;
  notify('draft');
  scheduleDraftCheck();
}

function patchRack(rack, patch) {
  edit((room) => {
    const r = room.racks.find((k) => k.id === rack.id);
    if (!r) return;
    // Server sinh lại khung khi số tầng/ô/nóc đổi, nên số khung chưa biết cho tới khi lưu.
    if (('levels' in patch && patch.levels !== r.levels) || ('bays' in patch && patch.bays !== r.bays)
      || ('hasTop' in patch && patch.hasTop !== r.hasTop)) r.slotCount = null;
    Object.assign(r, patch);
  });
}

export const moveRack = (rack, x, y, rotation) => patchRack(rack, { x, y, rotation });

export const rotateRack = (rack) => patchRack(rack, { rotation: rack.rotation === 90 ? 0 : 90 });

export function updateRack(rack, values) {
  const { code, ...rest } = values;   // mã không đổi được khi sửa
  patchRack(rack, rest);
  showMessage(`Đã sửa ${rack.code} trong bản nháp (chưa lưu).`);
}

export function addRack(request) {
  const created = { ...request, id: newTempId(), slotCount: null, footprintWidth: 0, footprintDepth: 0 };
  edit((room) => room.racks.push(created));
  select(created.id, null);
  showMessage(`Đã đặt kệ ${created.code} trong bản nháp (chưa lưu).`);
  return created;
}

export function updateRoom(values) {
  edit((room) => Object.assign(room, values));
  showMessage('Đã sửa phòng trong bản nháp (chưa lưu).');
}

export function addObstacle(values) {
  const created = { ...values, id: newTempId() };
  edit((room) => room.obstacles.push(created));
  select(null, created.id);
  showMessage(values.kind === 'Wall' ? 'Đã thêm tường (chưa lưu).' : 'Đã thêm cột (chưa lưu).');
  return created;
}

export function updateObstacle(obstacle, values) {
  const { x, y, width, depth } = values;
  edit((room) => {
    const o = room.obstacles.find((item) => item.id === obstacle.id);
    if (o) Object.assign(o, { x, y, width, depth });
  });
  showMessage(`Đã sửa ${obstacle.kind === 'Wall' ? 'tường' : 'cột'} trong bản nháp (chưa lưu).`);
}

// Xoá chỉ sửa bản nháp nên "Bỏ thay đổi" lấy lại được; vẫn hỏi trước vì xoá nhầm kệ rất mất công vẽ lại.

export async function removeObstacle(obstacle) {
  const what = obstacle.kind === 'Wall' ? 'tường' : 'cột';
  if (!await askConfirm(`Xoá ${what} tại (${obstacle.x}; ${obstacle.y}) m?`, { okText: 'Xoá', danger: true })) return;
  edit((room) => { room.obstacles = room.obstacles.filter((o) => o.id !== obstacle.id); });
  select(null, null);
  showMessage(`Đã xoá ${what} khỏi bản nháp (chưa lưu).`);
}

export async function removeRack(rack) {
  if (!await askConfirm(`Xoá kệ ${rack.code} khỏi bản nháp? Kệ chỉ thật sự bị xoá khi bấm Lưu nháp.`, { okText: 'Xoá', danger: true })) return;
  clearPreview();
  edit((room) => { room.racks = room.racks.filter((r) => r.id !== rack.id); });
  select(null, null);
  showMessage(`Đã xoá kệ ${rack.code} khỏi bản nháp (chưa lưu).`);
}

// ---------- hỏi server kiểm tra bản nháp ----------

let draftCheckTimer = null;

/** Sau mỗi thay đổi: hỏi server kiểm tra cả bản nháp, lấy kích thước chiếm chỗ mới cho từng kệ và lỗi đầu tiên (nếu có). */
/**
 * Bảng tra kết quả kiểm tra theo mã kệ (không phân biệt hoa thường). Trùng mã thì giữ dòng đầu, giống find trước đây.
 * Nghìn kệ mà mỗi kệ find trong nghìn kết quả là cả trăm triệu lần so chuỗi: trình duyệt đơ.
 */
function checksByCode(check) {
  const map = new Map();
  for (const c of check.racks) {
    const key = c.code.toLowerCase();
    if (!map.has(key)) map.set(key, c);
  }
  return map;
}

function scheduleDraftCheck() {
  clearTimeout(draftCheckTimer);
  draftCheckTimer = setTimeout(async function () {
    const room = state.room;
    if (!room) return;
    try {
      // check: { isValid, reason, racks: [{ code, isValid, reason, footprintWidth, footprintDepth }] }
      const check = await sendJson('POST', `/api/rooms/${state.roomId}/layout/validate`, toLayoutRequest(room));
      if (state.room !== room) return;   // bản nháp đã đổi tiếp: kết quả này cũ, lần hỏi sau sẽ thay
      const byCode = checksByCode(check);
      for (const r of room.racks) {
        const c = byCode.get(r.code.toLowerCase());
        if (c) {
          r.footprintWidth = c.footprintWidth;
          r.footprintDepth = c.footprintDepth;
        }
      }
      state.draftIssue = check.isValid ? null : check.reason;
      notify('status');
    } catch {
      // mất mạng: bỏ qua, lúc Lưu nháp sẽ báo
    }
  }, 250);
}

// ---------- bóng mờ (kiểm tra thử một kệ) ----------

const VALIDATE_DELAY_MS = 120;   // chờ chuột dừng một chút rồi mới hỏi server, không gửi mỗi lần chuột nhích
let validateTimer = null;
let validateSeq = 0;             // số thứ tự lần hỏi; trả lời của lần hỏi cũ (đến muộn) bị bỏ qua

/** Bản nháp với một kệ được thay bằng `request` (kệ có Id rackId), hoặc thêm vào nếu rackId = null. Không sửa state. */
function draftWith(request, rackId) {
  const room = state.room;
  const racks = rackId == null
    ? [...room.racks, { ...request, id: newTempId() }]
    : room.racks.map((r) => (r.id === rackId ? { ...r, ...request } : r));
  return { ...room, racks };
}

function sameShape(a, b) {
  return a.rotation === b.rotation && a.bays === b.bays && a.bayWidth === b.bayWidth && a.depth === b.depth;
}

/**
 * Hiện bóng mờ cho một kệ rồi hỏi server POST /layout/validate. Bóng xám khi đang chờ, xanh/đỏ khi có trả lời.
 * Server được hỏi theo CẢ BẢN NHÁP (kệ đang kéo được thay bằng vị trí của bóng).
 * Kích thước bóng lấy từ server (footprintWidth/Depth); frontend không tự tính, kể cả khi xoay.
 * footprint: gợi ý ban đầu lấy từ chính dữ liệu server (ví dụ kệ đang dời), để bóng hiện ngay không phải chờ.
 */
export function previewRack(request, { rackId = null, source, footprint = null } = {}) {
  const prev = state.preview;
  const keep = prev && prev.rackId === rackId && sameShape(prev.request, request);
  const width = footprint?.width ?? (keep ? prev.footprintWidth : null);
  const depth = footprint?.depth ?? (keep ? prev.footprintDepth : null);

  state.preview = { request, rackId, source, footprintWidth: width, footprintDepth: depth, isValid: null, reason: null };
  notify('preview');

  clearTimeout(validateTimer);
  const seq = ++validateSeq;
  validateTimer = setTimeout(async function () {
    try {
      const body = toLayoutRequest(draftWith(request, rackId));
      const check = await sendJson('POST', `/api/rooms/${state.roomId}/layout/validate`, body);
      if (seq !== validateSeq) return;
      const mine = check.racks.find((r) => r.code.toLowerCase() === request.code.toLowerCase());
      if (mine) {
        state.preview = {
          ...state.preview, isValid: mine.isValid, reason: mine.reason,
          footprintWidth: mine.footprintWidth, footprintDepth: mine.footprintDepth,
        };
      } else {
        // Bản nháp có lỗi cấu trúc (ví dụ trùng mã): không có kết quả riêng cho kệ này, hiện lý do chung
        state.preview = { ...state.preview, isValid: false, reason: check.reason };
      }
      notify('preview');
    } catch (err) {
      // Ví dụ 400 vì số tầng = 0: coi như đỏ, lý do là câu lỗi của server
      if (seq !== validateSeq) return;
      state.preview = { ...state.preview, isValid: false, reason: err.message };
      notify('preview');
    }
  }, VALIDATE_DELAY_MS);
}

/** Ngừng hỏi server cho bóng mờ hiện tại nhưng vẫn để bóng hiện. */
export function stopPreviewCheck() {
  validateSeq++;
  clearTimeout(validateTimer);
}

export function clearPreview() {
  validateSeq++;
  clearTimeout(validateTimer);
  if (state.preview || state.arrayPreview) {
    state.preview = null;
    state.arrayPreview = null;
    notify('preview');
  }
}

// ---------- đặt dãy kệ ----------

/** count mã kệ mới liên tiếp: K(n+1), K(n+2)… với n là số lớn nhất đang có trong bản nháp. */
export function nextRackCodes(count) {
  const first = Number(nextRackCode().slice(1));
  return Array.from({ length: count }, (_, i) => `K${first + i}`);
}

/**
 * Các kệ của một dãy, góc trên trái của kệ đầu tiên ở (x, y). Hàng chạy theo trục X, các hàng xếp xuống theo trục Y.
 * Ở đây phải tự tính chỗ chiếm của một kệ (dài = số ô × rộng ô, xoay 90° thì đổi chỗ dài và sâu, giống Rack.GetBounds ở
 * server) để biết kệ sau đặt ở đâu. Đặt được hay không vẫn do server trả lời.
 */
export function arrayRequests(x, y) {
  const rack = state.newRack;
  const { rows, perRow } = state.newArray;
  const gapAlong = state.newArray.gapAlong ?? state.room.minGap;
  const aisle = state.newArray.aisle ?? state.room.minGap;
  const length = rack.bays * rack.bayWidth;
  const [width, depth] = rack.rotation === 90 ? [rack.depth, length] : [length, rack.depth];
  const round = (v) => Number(v.toFixed(3));   // 0,1 × 3 ra 0,3 chứ không phải 0,30000000000000004

  const codes = nextRackCodes(rows * perRow);
  const requests = [];
  for (let row = 0; row < rows; row++) {
    for (let i = 0; i < perRow; i++) {
      requests.push({
        ...rack, productTypeId: null, code: codes[requests.length],
        x: round(x + i * (width + gapAlong)), y: round(y + row * (depth + aisle)),
        footprintWidth: width, footprintDepth: depth,   // tạm để vẽ bóng ngay; server trả số chính thức
      });
    }
  }
  return requests;
}

/** Hiện bóng mờ cả dãy rồi hỏi server POST /layout/validate một lần cho cả bản nháp có thêm các kệ này. */
export function previewRackArray(requests) {
  state.arrayPreview = { requests, results: null, reason: null };
  notify('preview');

  clearTimeout(validateTimer);
  const seq = ++validateSeq;
  validateTimer = setTimeout(async function () {
    try {
      const room = state.room;
      const body = toLayoutRequest({ ...room, racks: [...room.racks, ...requests.map((r) => ({ ...r, id: null }))] });
      const check = await sendJson('POST', `/api/rooms/${state.roomId}/layout/validate`, body);
      if (seq !== validateSeq) return;
      const byCode = checksByCode(check);
      const results = requests.map((r) => {
        const c = byCode.get(r.code.toLowerCase());
        return c ?? { isValid: false, reason: check.reason, footprintWidth: r.footprintWidth, footprintDepth: r.footprintDepth };
      });
      // Lỗi chung của bản nháp (ví dụ tường ra ngoài phòng) cũng chặn việc đặt dãy
      const firstBad = results.find((c) => !c.isValid);
      state.arrayPreview = { requests, results, reason: firstBad?.reason ?? (check.isValid ? null : check.reason) };
      notify('preview');
    } catch (err) {
      if (seq !== validateSeq) return;
      state.arrayPreview = { requests, results: [], reason: err.message };
      notify('preview');
    }
  }, VALIDATE_DELAY_MS);
}

/** Thêm cả dãy vào bản nháp bằng MỘT lần sửa: "Bỏ thay đổi" huỷ được cả dãy. */
export function addRacks(requests) {
  const created = requests.map((r) => ({ ...r, id: newTempId(), slotCount: null }));
  edit((room) => room.racks.push(...created));
  select(null, null);
  showMessage(`Đã đặt ${created.length} kệ (${created[0].code}–${created.at(-1).code}) trong bản nháp (chưa lưu).`);
}
