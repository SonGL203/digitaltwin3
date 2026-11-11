import { api } from './api/client.js';
import { createEditor2d } from './editor2d/editor2d.js';
import { createScene } from './scene3d/scene.js';
import { closeRoom, getState, setState, subscribe, syncWithAddress } from './state/store.js';
import { createRoomList } from './ui/roomList.js';
import { createSidebar } from './ui/sidebar.js';
import { formatMeters } from './ui/format.js';

// Điểm vào: nối các phần lại với nhau. Mỗi phần tự nghe store và tự vẽ lại.
// Bước "Mặt bằng" có hai màn: danh sách phòng (chưa mở phòng nào) và editor của phòng đang mở.

const views = {
  layout: document.getElementById('view-layout'),
  warehouse: document.getElementById('view-warehouse'),
  receive: document.getElementById('view-receive'),
};
const workspace = document.getElementById('workspace');
const roomListEl = document.getElementById('room-list');
const roomEditorEl = document.getElementById('room-editor');
const roomTitle = document.getElementById('room-title');
const stepButtons = document.querySelectorAll('#steps [data-step]');
const statusMessage = document.getElementById('status-message');
const statusCursor = document.getElementById('status-cursor');

createRoomList(roomListEl);
createSidebar(document.getElementById('sidebar'));
createEditor2d(document.getElementById('editor2d'), document.getElementById('toolbar-2d'), {
  onCursor(info) {
    if (!info) {
      statusCursor.textContent = '';
      return;
    }
    const where = `x ${formatMeters(info.x)} m · y ${formatMeters(info.y)} m`;
    statusCursor.textContent = info.rack ? `${where} · ${info.rack.code}` : where;
  },
});

document.getElementById('back-to-rooms').addEventListener('click', () => closeRoom());

// Cảnh 3D chỉ tạo khi mở bước 2 lần đầu: tạo lúc khung đang ẩn thì kích thước bằng 0.
let scene3d = null;

document.getElementById('steps').addEventListener('click', (e) => {
  const step = e.target.closest('[data-step]')?.dataset.step;
  if (step) setState({ step });
});

// Nút Back/Forward của trình duyệt: đọc lại địa chỉ (#/rooms hoặc #/rooms/3) rồi mở đúng màn.
window.addEventListener('popstate', () => syncWithAddress());

subscribe(render);
render(getState());

function render(state) {
  for (const [step, el] of Object.entries(views)) el.hidden = step !== state.step;
  stepButtons.forEach((b) => b.setAttribute('aria-current', b.dataset.step === state.step ? 'step' : 'false'));

  const inRoom = state.currentRoomId != null;
  roomListEl.hidden = inRoom;
  roomEditorEl.hidden = !inRoom;
  // Màn danh sách phòng không cần bảng bên: ẩn đi cho rộng chỗ.
  workspace.classList.toggle('no-sidebar', state.step === 'layout' && !inRoom);
  roomTitle.textContent = state.layout?.name ?? '';

  if (state.step === 'warehouse' && !scene3d) scene3d = createScene(document.getElementById('viewport3d'));

  statusMessage.textContent = state.message?.text ?? '';
  statusMessage.className = state.message?.kind === 'error' ? 'error' : '';
}

const apiStatus = document.getElementById('api-status');
api.health()
  .then(() => {
    apiStatus.textContent = 'API: kết nối được';
    apiStatus.className = 'api-status ok';
    return syncWithAddress();
  })
  .catch((err) => {
    apiStatus.textContent = `API lỗi: ${err.message}`;
    apiStatus.className = 'api-status error';
  });
