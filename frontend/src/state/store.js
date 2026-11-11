import { api, ApiError } from '../api/client.js';

// Trạng thái dùng chung của giao diện. Một chỗ giữ dữ liệu, các phần (danh sách phòng, editor 2D, bảng bên,
// thanh trạng thái) đăng ký nghe thay đổi rồi tự vẽ lại. Không dùng framework: dự án nhỏ, một hàm subscribe là đủ.
//
// layout: đúng như GET /api/rooms/{id} trả về (mét). Frontend không sửa layout tại chỗ; muốn đổi thì gọi API
// rồi tải lại, để màn hình luôn khớp database.

const state = {
  step: 'layout',          // 'layout' | 'warehouse' | 'receive'

  rooms: [],               // danh sách phòng (tóm tắt) cho màn chọn phòng
  roomsLoaded: false,
  currentRoomId: null,     // phòng đang mở trong editor; null = đang ở màn danh sách phòng

  layout: null,            // phòng đang mở (đủ kệ, vật cản), null khi chưa mở phòng nào
  layoutVersion: 0,        // tăng mỗi lần tải lại: bảng bên dựa vào đây để biết khi nào vẽ lại form
  loading: false,
  selectedRackId: null,
  selectedObstacleId: null,
  showGrid: true,

  // Công cụ chuột trên editor 2D: 'select' (chọn, kéo dời kệ), 'add-rack' (đặt kệ mới), 'add-column' (đặt cột).
  tool: 'select',

  // Bóng mờ của kệ đang kéo / sắp đặt / đang sửa trong form.
  // { request, rackId, source: 'drag' | 'add' | 'form', footprintWidth, footprintDepth, isValid, reason }
  // isValid = null khi đang chờ server trả lời; footprint lấy từ server (null cho tới lần trả lời đầu).
  preview: null,

  // Cấu hình kệ mới khi dùng công cụ "Đặt kệ" (mét, kg). Mã kệ tự sinh lúc đặt.
  newRack: {
    rotation: 0, levels: 3, levelHeight: 1.2, bays: 4, bayWidth: 1.2, depth: 1.0, hasTop: true, maxWeight: 1000,
  },

  message: null,           // { text, kind: 'info' | 'error' }
};

const listeners = new Set();

export function getState() {
  return state;
}

export function subscribe(listener) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function setState(patch) {
  Object.assign(state, patch);
  for (const listener of listeners) listener(state);
}

export function showMessage(text, kind = 'info') {
  setState({ message: { text, kind } });
}

// ---------- danh sách phòng, mở/đóng phòng ----------
// Địa chỉ trang giữ phòng đang mở (#/rooms/3), để tải lại trang hay bấm Back vẫn đúng chỗ.

export async function loadRooms() {
  try {
    setState({ rooms: await api.listRooms(), roomsLoaded: true });
  } catch (err) {
    setState({ roomsLoaded: true });
    showMessage(err.message, 'error');
  }
}

/** Mở một phòng trong editor. Đổi phòng thì bỏ chọn, bỏ bóng mờ, về công cụ Chọn. */
export async function openRoom(roomId) {
  if (location.hash !== `#/rooms/${roomId}`) history.pushState(null, '', `#/rooms/${roomId}`);
  setState({
    currentRoomId: roomId, layout: null, selectedRackId: null, selectedObstacleId: null,
    preview: null, tool: 'select', message: null,
  });
  await loadLayout();
}

/** Về màn danh sách phòng (tải lại danh sách vì số kệ, tên… có thể vừa đổi). */
export async function closeRoom() {
  if (location.hash !== '#/rooms') history.pushState(null, '', '#/rooms');
  setState({ currentRoomId: null, layout: null, preview: null, tool: 'select', message: null });
  await loadRooms();
}

/** Đọc địa chỉ trang rồi mở đúng màn: #/rooms/3 → phòng 3, còn lại → danh sách. */
export function syncWithAddress() {
  const id = /^#\/rooms\/(\d+)$/.exec(location.hash)?.[1];
  return id ? openRoom(Number(id)) : closeRoom();
}

/** Tải lại phòng đang mở từ server. Phòng không còn (ví dụ gõ sai Id trên địa chỉ) thì quay về danh sách. */
export async function loadLayout() {
  const roomId = state.currentRoomId;
  if (roomId == null) return;
  setState({ loading: true });
  try {
    const layout = await api.getRoom(roomId);
    if (state.currentRoomId !== roomId) return;   // người dùng đã chuyển phòng khác trong lúc chờ
    setState({
      layout,
      layoutVersion: state.layoutVersion + 1,
      loading: false,
      selectedRackId: layout.racks.some((r) => r.id === state.selectedRackId) ? state.selectedRackId : null,
      selectedObstacleId: layout.obstacles.some((o) => o.id === state.selectedObstacleId) ? state.selectedObstacleId : null,
    });
  } catch (err) {
    setState({ loading: false });
    if (err instanceof ApiError && err.status === 404) {
      await closeRoom();
    }
    showMessage(err.message, 'error');
  }
}

export function selectedRack() {
  return state.layout?.racks.find((r) => r.id === state.selectedRackId) ?? null;
}

export function selectedObstacle() {
  return state.layout?.obstacles.find((o) => o.id === state.selectedObstacleId) ?? null;
}
