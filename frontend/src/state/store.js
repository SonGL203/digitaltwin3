import { api, ApiError } from '../api/client.js';

// Trạng thái dùng chung của giao diện. Một chỗ giữ dữ liệu, các phần (editor 2D, bảng bên, thanh trạng thái)
// đăng ký nghe thay đổi rồi tự vẽ lại. Không dùng framework: dự án nhỏ, một hàm subscribe là đủ.
//
// layout: đúng như GET /api/layout trả về (mét). Frontend không sửa layout tại chỗ; muốn đổi thì gọi API
// rồi tải lại, để màn hình luôn khớp database.

const state = {
  step: 'layout',          // 'layout' | 'warehouse' | 'receive'
  layout: null,            // dữ liệu phòng từ server, null khi chưa có phòng
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

/** Tải lại mặt bằng từ server. 404 nghĩa là chưa có phòng, không phải lỗi. */
export async function loadLayout() {
  setState({ loading: true });
  try {
    const layout = await api.getLayout();
    setState({
      layout,
      layoutVersion: state.layoutVersion + 1,
      loading: false,
      selectedRackId: layout.racks.some((r) => r.id === state.selectedRackId) ? state.selectedRackId : null,
      selectedObstacleId: layout.obstacles.some((o) => o.id === state.selectedObstacleId) ? state.selectedObstacleId : null,
    });
  } catch (err) {
    if (err instanceof ApiError && err.status === 404) {
      setState({ layout: null, layoutVersion: state.layoutVersion + 1, loading: false });
      showMessage('Chưa có phòng. Bật "Seed:SampleLayout" hoặc tạo phòng qua API.');
    } else {
      setState({ loading: false });
      showMessage(err.message, 'error');
    }
  }
}

export function selectedRack() {
  return state.layout?.racks.find((r) => r.id === state.selectedRackId) ?? null;
}

export function selectedObstacle() {
  return state.layout?.obstacles.find((o) => o.id === state.selectedObstacleId) ?? null;
}
