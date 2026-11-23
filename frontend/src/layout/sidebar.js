import { escapeHtml, formatInt, formatMeters } from '../shared/format.js';
import {
  addObstacle, clearPreview, discardDraft, nextRackCode, previewRack, rackToRequest, removeObstacle, removeRack,
  saveDraft, select, selectedObstacle, selectedRack, setTool, state, updateObstacle, updateRack, updateRoom,
} from './state.js';

// Bảng bên trái. Các ô nhập đã viết sẵn trong layout.html; file này chỉ:
//   - ĐIỀN giá trị vào ô (fillForm) khi dữ liệu đổi,
//   - ĐỌC giá trị từ ô (readForm) khi bấm nút,
//   - vẽ hai danh sách (kệ, tường/cột).
// Mọi ô nhập chỉ sửa BẢN NHÁP; nút "Lưu nháp" mới gửi lên server.
//
// Chỉ điền lại các ô khi dữ liệu thật đổi (tải lại, đổi kệ đang chọn, bấm Áp dụng), KHÔNG điền lại mỗi lần
// bóng mờ cập nhật: nếu không thì chữ đang gõ dở bị xoá mất.

const $ = (id) => document.getElementById(id);

const draftStatus = $('draftStatus');
const btnSave = $('btnSave');
const btnDiscard = $('btnDiscard');
const roomForm = $('roomForm');
const nothingSelected = $('nothingSelected');
const rackPanel = $('rackPanel');
const rackForm = $('rackForm');
const rackLive = $('rackLive');
const obstaclePanel = $('obstaclePanel');
const obstacleForm = $('obstacleForm');
const newRackForm = $('newRackForm');
const newArrayForm = $('newArrayForm');
const addObstacleForm = $('addObstacleForm');

// ---------- ô nhập của kệ: chèn từ <template> vào form sửa kệ và form kệ mới ----------
const rackFields = $('rackFields');
rackForm.prepend(rackFields.content.cloneNode(true));
newRackForm.insertBefore(rackFields.content.cloneNode(true), newRackForm.querySelector('.form-actions'));
newRackForm.querySelectorAll('.pos').forEach((label) => label.remove());   // kệ mới chưa có vị trí: đặt bằng chuột

// ---------- đọc / điền form ----------

// Điền giá trị của object vào các ô cùng tên (name="width" ← obj.width)
function fillForm(form, values) {
  for (const element of form.elements) {
    if (!element.name || !(element.name in values)) continue;
    if (element.type === 'checkbox') element.checked = Boolean(values[element.name]);
    else element.value = values[element.name];
  }
}

// Đọc các ô thành object đúng kiểu: số thực, số nguyên (ô có data-int), true/false, chữ
function readForm(form) {
  const values = {};
  for (const element of form.elements) {
    if (!element.name || element.disabled) continue;
    if (element.type === 'checkbox') values[element.name] = element.checked;
    else if (element.hasAttribute('data-int')) values[element.name] = parseInt(element.value, 10);
    else if (element.type === 'number') values[element.name] = Number(element.value);
    else values[element.name] = element.value;
  }
  return values;
}

// ---------- vẽ ----------

// Trang gọi hàm này mỗi khi dữ liệu đổi (xem onChange trong layout.js)
export function updateSidebar(what) {
  if (what === 'draft' || what === 'selection') drawAll();
  drawDraftBar();
  drawLive();
}

function drawAll() {
  const room = state.room;
  if (!room) return;
  fillForm(roomForm, room);
  fillForm(newRackForm, state.newRack);
  fillForm(newArrayForm, {
    ...state.newArray,
    gapAlong: state.newArray.gapAlong ?? room.minGap,   // chưa nhập thì theo lối đi tối thiểu của phòng
    aisle: state.newArray.aisle ?? room.minGap,
  });
  drawArrayLive();
  $('nextRackCode').value = nextRackCode();
  drawSelected();
  drawRackList();
  drawObstacleList();
}

// Phần "đang chọn": form kệ, hoặc form tường/cột, hoặc câu hướng dẫn
function drawSelected() {
  const rack = selectedRack();
  const obstacle = selectedObstacle();
  rackPanel.hidden = !rack;
  obstaclePanel.hidden = !obstacle;
  nothingSelected.hidden = Boolean(rack || obstacle);

  if (rack) {
    $('rackTitle').textContent = `Kệ ${rack.code} · ${slotText(rack.slotCount)}`;
    $('rackHint').textContent = `Chiếm chỗ ${formatMeters(rack.footprintWidth)} × ${formatMeters(rack.footprintDepth)} m. Khung được sinh lại khi lưu nháp; kệ đang có hàng thì chỉ dời/xoay được.`;
    fillForm(rackForm, rack);
  }
  if (obstacle) {
    $('obstacleTitle').textContent = `${obstacle.kind === 'Wall' ? 'Tường' : 'Cột'} đang chọn`;
    fillForm(obstacleForm, obstacle);
  }
}

function drawRackList() {
  const racks = state.room.racks;
  const totalSlots = racks.reduce((sum, r) => sum + (r.slotCount ?? 0), 0);
  $('rackListTitle').textContent = `Kệ (${racks.length}) · ${formatInt(totalSlots)} khung`;
  $('rackList').innerHTML = racks.map((r) => `
    <li>
      <button type="button" class="pick" data-rack-id="${r.id}" aria-pressed="${r.id === state.selectedRackId}">
        <span>${escapeHtml(r.code)}</span><span class="muted">${slotText(r.slotCount)}</span>
      </button>
    </li>`).join('');
}

function drawObstacleList() {
  const obstacles = state.room.obstacles;
  $('obstacleListTitle').textContent = `Tường, cột (${obstacles.length})`;
  $('obstacleList').innerHTML = obstacles.map((o) => `
    <li>
      <button type="button" class="pick" data-obstacle-id="${o.id}" aria-pressed="${o.id === state.selectedObstacleId}">
        <span>${o.kind === 'Wall' ? 'Tường' : 'Cột'} (${formatMeters(o.x)}; ${formatMeters(o.y)})</span>
        <span class="muted">${formatMeters(o.width)} × ${formatMeters(o.depth)}</span>
      </button>
      <button type="button" class="icon danger" title="Xoá" data-remove-obstacle="${o.id}">×</button>
    </li>`).join('');
}

// Số khung; null = chưa biết (kệ mới hoặc vừa đổi cấu hình, server sinh khi lưu nháp)
function slotText(count) {
  return count == null ? 'chưa lưu' : `${count} khung`;
}

// Thanh trên cùng bảng bên: trạng thái bản nháp + nút Lưu / Bỏ
function drawDraftBar() {
  if (state.saving) draftStatus.textContent = 'Đang lưu…';
  else if (!state.dirty) draftStatus.textContent = 'Đã lưu';
  else draftStatus.textContent = state.draftIssue ? `Chưa lưu · ${state.draftIssue}` : 'Có thay đổi chưa lưu';
  draftStatus.className = 'draft-status' + (state.dirty ? (state.draftIssue ? ' error' : ' dirty') : '');
  btnSave.disabled = !state.dirty || state.saving;
  btnDiscard.disabled = !state.dirty || state.saving;
}

// Dòng kiểm tra trực tiếp dưới form sửa kệ (kết quả của bóng mờ)
function drawLive() {
  const p = state.preview;
  if (!p || p.source !== 'form' || p.rackId !== state.selectedRackId) {
    rackLive.textContent = '';
    rackLive.className = 'live full';
  } else if (p.isValid === null) {
    rackLive.textContent = 'Đang kiểm tra…';
    rackLive.className = 'live full';
  } else if (p.isValid) {
    rackLive.textContent = `Đặt được · chiếm ${formatMeters(p.footprintWidth)} × ${formatMeters(p.footprintDepth)} m`;
    rackLive.className = 'live full ok';
  } else {
    rackLive.textContent = p.reason;
    rackLive.className = 'live full error';
  }
}

// ---------- sự kiện ----------

btnSave.addEventListener('click', saveDraft);
btnDiscard.addEventListener('click', discardDraft);

// Form phòng: Áp dụng
roomForm.addEventListener('submit', function (event) {
  event.preventDefault();
  updateRoom(readForm(roomForm));
});

// Danh sách kệ: bấm một dòng để chọn kệ
$('rackList').addEventListener('click', function (event) {
  const button = event.target.closest('[data-rack-id]');
  if (button) select(Number(button.dataset.rackId), null);
});

// Danh sách tường/cột: bấm dòng để chọn, bấm × để xoá
$('obstacleList').addEventListener('click', function (event) {
  const pick = event.target.closest('[data-obstacle-id]');
  if (pick) select(null, Number(pick.dataset.obstacleId));

  const remove = event.target.closest('[data-remove-obstacle]');
  if (remove) {
    const obstacle = state.room.obstacles.find((o) => o.id === Number(remove.dataset.removeObstacle));
    if (obstacle) removeObstacle(obstacle);
  }
});

// Form sửa kệ: gõ tới đâu, bóng mờ và dòng kiểm tra cập nhật tới đó (server trả lời sau ~120 ms)
rackForm.addEventListener('input', function () {
  const rack = selectedRack();
  if (!rack) return;
  const request = { ...rackToRequest(rack), ...readForm(rackForm) };
  // Chưa đổi xoay/kích thước thì dùng luôn kích thước server đã trả cho kệ này để bóng hiện ngay;
  // đổi rồi thì chờ server trả kích thước mới, không tự tính.
  const sameShape = request.rotation === rack.rotation && request.bays === rack.bays
    && request.bayWidth === rack.bayWidth && request.depth === rack.depth;
  const footprint = sameShape ? { width: rack.footprintWidth, depth: rack.footprintDepth } : null;
  previewRack(request, { rackId: rack.id, source: 'form', footprint: footprint });
});

rackForm.addEventListener('submit', function (event) {
  event.preventDefault();
  const rack = selectedRack();
  if (rack) updateRack(rack, { ...rackToRequest(rack), ...readForm(rackForm) });
  clearPreview();
});

$('btnRemoveRack').addEventListener('click', function () {
  if (selectedRack()) removeRack(selectedRack());
});

$('btnCancelRack').addEventListener('click', function () {
  clearPreview();
  drawSelected();   // điền lại giá trị cũ vào form
});

// Form tường/cột đang chọn
obstacleForm.addEventListener('submit', function (event) {
  event.preventDefault();
  const obstacle = selectedObstacle();
  if (obstacle) updateObstacle(obstacle, readForm(obstacleForm));
});

$('btnRemoveObstacle').addEventListener('click', function () {
  if (selectedObstacle()) removeObstacle(selectedObstacle());
});

// Form thêm tường/cột
addObstacleForm.addEventListener('submit', function (event) {
  event.preventDefault();
  addObstacle(readForm(addObstacleForm));
});

// Form kệ mới: lưu cấu hình để dùng khi đặt bằng chuột
newRackForm.addEventListener('input', function () {
  state.newRack = { ...state.newRack, ...readForm(newRackForm) };
});

// Form đặt dãy: lưu số hàng, số kệ mỗi hàng, khe, lối đi để dùng khi đặt bằng chuột
function drawArrayLive() {
  const { rows, perRow } = state.newArray;
  $('arrayLive').textContent = `Sẽ đặt ${rows} hàng × ${perRow} kệ = ${formatInt(rows * perRow)} kệ.`;
}

newArrayForm.addEventListener('input', function () {
  const values = readForm(newArrayForm);
  const next = { ...state.newArray };
  if (values.rows >= 1) next.rows = values.rows;
  if (values.perRow >= 1) next.perRow = values.perRow;
  if (values.gapAlong >= 0 && newArrayForm.elements.gapAlong.value !== '') next.gapAlong = values.gapAlong;
  if (values.aisle >= 0 && newArrayForm.elements.aisle.value !== '') next.aisle = values.aisle;
  state.newArray = next;
  drawArrayLive();
});

$('btnPlaceArray').addEventListener('click', function () {
  clearPreview();
  setTool('add-array');
});

$('btnPlaceRack').addEventListener('click', function () {
  clearPreview();
  setTool('add-rack');
});
