import { getJson, sendJson } from '../shared/api.js';
import { showTopBar } from '../shared/nav.js';
import { showMessage } from '../shared/message.js';
import { escapeHtml, formatInt, formatMeters } from '../shared/format.js';

// Trang Danh sách kho (index.html).
// Luồng: mở trang → GET /api/rooms → vẽ mỗi kho một thẻ. Bấm thẻ là sang trang Mặt bằng của kho đó.
// "+ Thêm kho" → mở form → POST /api/rooms → sang trang Mặt bằng của kho vừa tạo.

showTopBar('rooms');

const roomCards = document.getElementById('roomCards');
const roomCount = document.getElementById('roomCount');
const roomForm = document.getElementById('roomForm');
const formError = document.getElementById('formError');
const emptyHint = document.getElementById('emptyHint');
const btnOpenForm = document.getElementById('btnOpenForm');
const btnCloseForm = document.getElementById('btnCloseForm');

let rooms = [];   // danh sách kho lấy từ server

// ---------- 1. Tải danh sách ----------
loadRooms();

async function loadRooms() {
  try {
    rooms = await getJson('/api/rooms');
    drawRooms();
  } catch (err) {
    showMessage(err.message, true);
  }
}

// Mỗi kho: { id, name, width, depth, gridSize, rackCount, obstacleCount, slotCount }
function drawRooms() {
  roomCount.textContent = rooms.length;

  roomCards.innerHTML = rooms.map((room) => `
    <li>
      <a class="room-card" href="layout.html?roomId=${room.id}">
        <strong>${escapeHtml(room.name)}</strong>
        <span>${formatMeters(room.width)} × ${formatMeters(room.depth)} m · lưới ${formatMeters(room.gridSize)} m</span>
        <span class="muted">${room.rackCount} kệ · ${formatInt(room.slotCount)} khung · ${room.obstacleCount} tường/cột</span>
      </a>
    </li>
  `).join('');

  // Chưa có kho nào: mở sẵn form (việc đầu tiên cần làm là tạo kho), ẩn nút Nhập lô
  const noRooms = rooms.length === 0;
  emptyHint.hidden = !noRooms;
  if (noRooms) openForm();
}

// ---------- 2. Form thêm kho ----------
btnOpenForm.addEventListener('click', openForm);

btnCloseForm.addEventListener('click', function () {
  roomForm.hidden = true;
  btnOpenForm.hidden = false;
});

function openForm() {
  roomForm.hidden = false;
  btnOpenForm.hidden = true;
  formError.textContent = '';
  roomForm.elements.name.value = suggestName();
  roomForm.elements.name.select();
  btnCloseForm.hidden = rooms.length === 0;   // chưa có kho nào thì không cho đóng form
}

// Tên gợi ý chưa trùng: "Phòng 1", "Phòng 2"… (server vẫn là bên kiểm tra trùng thật)
function suggestName() {
  const used = new Set(rooms.map((r) => r.name.toLowerCase()));
  let n = rooms.length + 1;
  while (used.has(`phòng ${n}`)) n++;
  return `Phòng ${n}`;
}

roomForm.addEventListener('submit', async function (event) {
  event.preventDefault();   // không cho trình duyệt tải lại trang
  const form = roomForm.elements;
  const room = {
    name: form.name.value,
    width: Number(form.width.value),
    depth: Number(form.depth.value),
    height: Number(form.height.value),
    gridSize: Number(form.gridSize.value),
    minGap: Number(form.minGap.value),
    margin: Number(form.margin.value),
  };
  try {
    const created = await sendJson('POST', '/api/rooms', room);   // server trả { id }
    location.href = `layout.html?roomId=${created.id}`;
  } catch (err) {
    // Ví dụ "Đã có phòng tên Kho A." (409): hiện ngay dưới form, giữ nguyên chữ đã gõ để sửa
    formError.textContent = err.message;
  }
});
