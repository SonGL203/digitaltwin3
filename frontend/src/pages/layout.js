import { readRoomId, showRoomTabs, showTopBar } from '../shared/nav.js';
import { showMessage } from '../shared/message.js';
import { formatMeters } from '../shared/format.js';
import { createCanvas } from '../layout/canvas.js';
import { updateSidebar } from '../layout/sidebar.js';
import { loadRoom, onChange, state } from '../layout/state.js';

// Trang Mặt bằng (layout.html?roomId=3). File này chỉ NỐI các phần lại:
//   layout/state.js    dữ liệu bản nháp + các hành động sửa + gọi server
//   layout/canvas.js   vẽ canvas 2D và xử lý chuột, phím
//   layout/sidebar.js  các form bên trái
// Luồng: mở trang → GET /api/rooms/{id} → bản nháp → canvas và bảng bên vẽ từ bản nháp.
// Mỗi lần dữ liệu đổi, state.js gọi hàm đăng ký ở onChange bên dưới; hàm đó vẽ lại giao diện.

showTopBar('rooms');

const roomId = readRoomId();
if (roomId == null) {
  showMessage('Thiếu roomId trên địa chỉ trang. Chọn một kho ở trang Danh sách kho.', true);
} else {
  start(roomId);
}

async function start(id) {
  state.roomId = id;
  showRoomTabs(id, 'layout');

  const canvas = createCanvas(document.getElementById('canvasWrap'), {
    // Hiện toạ độ chuột (mét) và tên kệ đang trỏ vào ở thanh dưới cùng
    onCursor(info) {
      const text = document.getElementById('status-cursor');
      if (!info) {
        text.textContent = '';
        return;
      }
      const where = `x ${formatMeters(info.x)} m · y ${formatMeters(info.y)} m`;
      text.textContent = info.rack ? `${where} · ${info.rack.code}` : where;
    },
  });

  // Mỗi lần dữ liệu đổi: vẽ lại canvas và bảng bên
  onChange(function (what) {
    document.getElementById('roomTitle').textContent = state.room?.name ?? '';
    updateSidebar(what);
    canvas.redraw();
  });

  try {
    await loadRoom();
  } catch (err) {
    showMessage(err.message, true);   // ví dụ kho không tồn tại (404)
  }
}

// Đóng tab / tải lại trang / bấm link sang trang khác khi còn thay đổi chưa lưu: trình duyệt tự hỏi lại
window.addEventListener('beforeunload', function (event) {
  if (state.dirty) event.preventDefault();
});
