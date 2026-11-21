import { getJson, sendJson } from '../shared/api.js';
import { askConfirm } from '../shared/dialog.js';
import { escapeHtml } from '../shared/format.js';
import { showMessage } from '../shared/message.js';
import { showTopBar } from '../shared/nav.js';

// Trang Nhập lô hàng (receive.html). LẬP LÔ TRƯỚC, CHỌN KHO SAU.
//
// Luồng:
//  1. Người dùng nhập các dòng (số lượng + số đo).
//  2. "Kiểm tra các kho"  → POST /api/batches/check   (không ghi gì; server báo từng kho nhận được bao nhiêu)
//  3. Có 2 cách nhập:
//       "Nhập theo đề xuất"  → POST /api/batches  rồi  POST /api/batches/{id}/distribute
//       "Nhập vào kho này"   → POST /api/batches  rồi  POST /api/rooms/{roomId}/batches/{id}/allocate
//     Nhập lô và xếp là HAI bước riêng: nếu bước xếp lỗi thì lô vẫn đã nhập, bấm "Xếp lại" (không xếp đôi).
//  4. Bảng kết quả: mỗi kho nhận bao nhiêu, nút "Xem 3D" mở kho đó và cho pallet bay vào.
//
// Trang này không tự chọn kho hay khung, không tự đếm chỗ: mọi con số do server trả.

showTopBar('receive');

// ---------- dữ liệu mẫu ----------
const SAMPLE = [   // lô 6A + 2B: 6 pallet thấp, 2 pallet cao chỉ lên được nóc
  { quantity: 6, length: 1.2, width: 0.8, height: 1.0 },
  { quantity: 2, length: 1.2, width: 0.8, height: 1.6 },
];
const LOT_B36 = [   // lô dùng làm bài test thuật toán
  { quantity: 15, length: 1.2, width: 0.8, height: 1.0 },
  { quantity: 2, length: 1.2, width: 0.8, height: 1.6 },
  { quantity: 5, length: 1.0, width: 0.8, height: 0.9 },
  { quantity: 8, length: 0.8, width: 0.6, height: 0.7 },
];

// ---------- các phần tử trên trang ----------
const linesBody = document.getElementById('lines');
const checkResult = document.getElementById('checkResult');
const outcomeBox = document.getElementById('outcome');
const typeForm = document.getElementById('typeForm');

// ---------- biến nhớ ----------
let productTypes = [];   // loại hàng, để đổ vào ô chọn
let lastCheck = null;    // kết quả kiểm tra gần nhất
let outcome = null;      // { batch, mode, result }: lô vừa nhập. mode = 'auto' hoặc { roomId, roomName }. result = null nếu chưa xếp được
let busy = false;        // đang chờ server: không cho bấm thêm

// ---------- 1. Mở trang ----------
addLines(LOT_B36);   // điền sẵn lô b36
loadProductTypes();

async function loadProductTypes() {
  try {
    productTypes = await getJson('/api/product-types');   // [{ id, code, name }]
    // Các dòng đã vẽ trước khi tải xong: cập nhật lại ô chọn loại hàng, giữ nguyên lựa chọn
    for (const select of linesBody.querySelectorAll('.f-product')) {
      const chosen = select.value;
      select.innerHTML = productOptions();
      select.value = chosen;
    }
  } catch (err) {
    showMessage(err.message, true);
  }
}

function productOptions() {
  const options = productTypes.map((t) => `<option value="${t.id}">${escapeHtml(t.code)} · ${escapeHtml(t.name)}</option>`);
  return `<option value="">Chưa phân loại</option>${options.join('')}`;
}

// ---------- 2. Các dòng nhập ----------

// Thêm một dòng ô trống (có thể điền sẵn giá trị)
function addLine(line = {}) {
  const row = document.createElement('tr');
  row.innerHTML = `
    <td><input class="f-quantity" type="number" min="1" step="1" value="${line.quantity ?? 1}" /></td>
    <td><input class="f-length" type="number" min="0" step="any" value="${line.length ?? ''}" /></td>
    <td><input class="f-width" type="number" min="0" step="any" value="${line.width ?? ''}" /></td>
    <td><input class="f-height" type="number" min="0" step="any" value="${line.height ?? ''}" /></td>
    <td><input class="f-weight" type="number" min="0" step="any" value="${line.weight ?? ''}" /></td>
    <td><select class="f-product">${productOptions()}</select></td>
    <td><button type="button" class="icon danger" title="Xoá dòng">×</button></td>
  `;
  row.querySelector('button').addEventListener('click', function () {
    row.remove();
    clearCheck();
  });
  linesBody.appendChild(row);
}

function addLines(lines) {
  lines.forEach(addLine);
}

function replaceLines(lines) {
  linesBody.innerHTML = '';
  addLines(lines);
  clearCheck();
}

// Sửa bất kỳ ô nào thì kết quả kiểm tra cũ không còn đúng → xoá đi
linesBody.addEventListener('input', clearCheck);

function clearCheck() {
  lastCheck = null;
  checkResult.innerHTML = '';
}

// Đọc các ô trên màn hình thành dữ liệu gửi server (đọc ngay lúc bấm nút)
function readBatchRequest() {
  const lines = [];
  for (const row of linesBody.querySelectorAll('tr')) {
    const weight = row.querySelector('.f-weight').value;
    const product = row.querySelector('.f-product').value;
    lines.push({
      quantity: Number(row.querySelector('.f-quantity').value),
      length: Number(row.querySelector('.f-length').value),
      width: Number(row.querySelector('.f-width').value),
      height: Number(row.querySelector('.f-height').value),
      weight: weight === '' ? null : Number(weight),        // để trống = không kiểm tra tải
      productTypeId: product === '' ? null : Number(product),  // "Chưa phân loại" = null
    });
  }
  return { lines: lines };
}

// ---------- 3. Các nút điền mẫu ----------
document.getElementById('btnAddLine').addEventListener('click', function () {
  addLine();
  clearCheck();
});
document.getElementById('btnSample').addEventListener('click', () => replaceLines(SAMPLE));
document.getElementById('btnB36').addEventListener('click', () => replaceLines(LOT_B36));
document.getElementById('btnRandom').addEventListener('click', () => replaceLines(randomLines(30)));

// Chia ngẫu nhiên `total` pallet vào vài số đo thường gặp (chỉ là dữ liệu nhập mẫu)
function randomLines(total) {
  const sizes = [
    { length: 1.2, width: 0.8, height: 1.0 }, { length: 1.2, width: 0.8, height: 1.6 },
    { length: 1.0, width: 0.8, height: 0.9 }, { length: 0.8, width: 0.6, height: 0.7 },
  ];
  const lines = [];
  let left = total;
  sizes.forEach((size, i) => {
    const isLast = i === sizes.length - 1;
    const quantity = isLast ? left : Math.min(left, 1 + Math.floor(Math.random() * (left / 2)));
    left -= quantity;
    if (quantity > 0) lines.push({ ...size, quantity: quantity });
  });
  return lines;
}

// ---------- 4. Chạy một việc có gọi server ----------
// Khoá nút lúc chờ (body.busy), báo lỗi nếu có, xong thì vẽ lại bảng kết quả.
async function run(work) {
  if (busy) return;
  busy = true;
  document.body.classList.add('busy');
  try {
    await work();
  } catch (err) {
    showMessage(err.message, true);
  } finally {
    busy = false;
    document.body.classList.remove('busy');
    drawOutcome();
  }
}

// ---------- 5. Kiểm tra các kho ----------
document.getElementById('btnCheck').addEventListener('click', function () {
  run(async function () {
    outcome = null;
    lastCheck = await sendJson('POST', '/api/batches/check', readBatchRequest());
    drawCheck();
  });
});

// lastCheck: { total, lines: [{typeCode, color, isNewType, quantity, length, width, height}],
//              rooms: [{roomId, roomName, placeable, missing, notes}], plan: {total, placed, rooms: [{roomName, count}]} }
function drawCheck() {
  const check = lastCheck;

  const typeRows = check.lines.map((l) => `
    <tr>
      <td><i class="chip" data-color="${l.color}"></i> Type ${escapeHtml(l.typeCode)}${l.isNewType ? ' <span class="muted">(mới)</span>' : ''}</td>
      <td>${l.quantity}</td>
      <td class="muted">${l.length} × ${l.width} × ${l.height} m</td>
    </tr>`).join('');

  const roomRows = check.rooms.map((r) => `
    <tr class="${r.missing ? 'bad' : ''}">
      <td>${escapeHtml(r.roomName)}</td>
      <td>${r.placeable} / ${check.total}</td>
      <td>${r.missing
        ? `Thiếu ${r.missing} ô${r.notes.length ? `<br><span class="muted">${r.notes.map(escapeHtml).join('<br>')}</span>` : ''}`
        : 'Đủ điều kiện'}</td>
      <td><button type="button" data-room-id="${r.roomId}" ${r.placeable === 0 ? 'disabled' : ''}>Nhập vào kho này</button></td>
    </tr>`).join('');

  const plan = check.plan;
  const missing = plan.total - plan.placed;
  const planRows = plan.rooms.map((r) => `<tr><td>${escapeHtml(r.roomName)}</td><td>${r.count} pallet</td></tr>`).join('');

  checkResult.innerHTML = `
    <h3>Lô gồm ${check.total} pallet</h3>
    <table><thead><tr><th>Type</th><th>SL</th><th>Số đo</th></tr></thead><tbody>${typeRows}</tbody></table>

    <h3>Từng kho (nếu nhận riêng cả lô)</h3>
    ${check.rooms.length
      ? `<table><thead><tr><th>Kho</th><th>Nhận được</th><th>Kết quả</th><th></th></tr></thead><tbody>${roomRows}</tbody></table>`
      : '<p class="warn">Chưa có kho nào. Tạo kho ở trang Danh sách kho trước.</p>'}

    <h3>Đề xuất của hệ thống</h3>
    <p class="hint">Ưu tiên kho đã có hàng cùng type; kho chứa được hết thì không chia; không kho nào đủ thì chia ra nhiều kho.</p>
    ${plan.rooms.length ? `<table><thead><tr><th>Kho</th><th>Số pallet</th></tr></thead><tbody>${planRows}</tbody></table>` : ''}
    <p class="${missing ? 'warn' : 'ok'}">${missing
      ? `Theo đề xuất vẫn thiếu ${missing} ô: ${missing} pallet sẽ ở lại trong lô, chưa có khung.`
      : 'Theo đề xuất: đủ chỗ cho cả lô.'}</p>
    <button type="button" class="primary" id="btnAuto" ${plan.placed === 0 ? 'disabled' : ''}>Nhập theo đề xuất</button>
  `;

  // Tô màu cho các ô vuông type (màu lấy từ server, đặt bằng JS để HTML không chứa style)
  for (const chip of checkResult.querySelectorAll('.chip')) chip.style.background = chip.dataset.color;

  // Gắn sự kiện cho các nút vừa vẽ
  document.getElementById('btnAuto').addEventListener('click', importAuto);
  for (const button of checkResult.querySelectorAll('[data-room-id]')) {
    button.addEventListener('click', () => importIntoRoom(Number(button.dataset.roomId)));
  }
}

// ---------- 6. Nhập lô ----------

// Nhập theo đề xuất: lập lô rồi để server chia ra các kho
function importAuto() {
  run(async function () {
    const batch = await sendJson('POST', '/api/batches', readBatchRequest());   // { batchId, code, palletCount }
    outcome = { batch: batch, mode: 'auto', result: null };
    clearCheck();
    // Nếu dòng dưới lỗi: lô đã nhập rồi, màn hình sẽ hiện nút "Xếp lại"
    outcome.result = await sendJson('POST', `/api/batches/${batch.batchId}/distribute`);
    showMessage(`Đã nhập lô ${batch.code}: ${outcome.result.placed} / ${batch.palletCount} pallet vào ${outcome.result.rooms.length} kho.`);
  });
}

// Nhập vào một kho do người dùng chọn. Kho thiếu chỗ thì hỏi lại (phần thiếu ở lại trong lô).
async function importIntoRoom(roomId) {
  const room = lastCheck.rooms.find((r) => r.roomId === roomId);
  if (room.missing) {
    const ok = await askConfirm(
      `Kho ${room.roomName} thiếu ${room.missing} ô: ${room.missing} pallet sẽ ở lại trong lô, chưa có khung. Vẫn nhập vào kho này?`,
      { okText: 'Vẫn nhập' });
    if (!ok) return;
  }
  run(async function () {
    const batch = await sendJson('POST', '/api/batches', readBatchRequest());
    const mode = { roomId: roomId, roomName: room.roomName };
    outcome = { batch: batch, mode: mode, result: null };
    clearCheck();
    outcome.result = await allocateInto(mode, batch);
    showMessage(`Đã nhập lô ${batch.code} vào kho ${room.roomName}.`);
  });
}

// Xếp lại khi bước xếp bị lỗi (pallet nào đã vào khung thì không bị xếp đôi)
function retryAllocate() {
  run(async function () {
    const { batch, mode } = outcome;
    outcome.result = mode === 'auto'
      ? await sendJson('POST', `/api/batches/${batch.batchId}/distribute`)
      : await allocateInto(mode, batch);
  });
}

// Xếp vào một kho. API này trả dạng khác với "distribute", nên đổi về cùng dạng để vẽ chung.
async function allocateInto(mode, batch) {
  const allocation = await sendJson('POST', `/api/rooms/${mode.roomId}/batches/${batch.batchId}/allocate`);
  const count = allocation.placements.length;
  return {
    total: batch.palletCount,
    placed: count,
    rooms: count ? [{ roomId: mode.roomId, roomName: mode.roomName, count: count, placements: allocation.placements }] : [],
    rejected: allocation.rejected,
  };
}

// ---------- 7. Kết quả sau khi nhập ----------
function drawOutcome() {
  if (!outcome) {
    outcomeBox.innerHTML = '';
    return;
  }
  const { batch, result } = outcome;

  // Đã nhập lô nhưng bước xếp lỗi
  if (!result) {
    outcomeBox.innerHTML = `
      <h3>Lô ${escapeHtml(batch.code)} đã nhập (${batch.palletCount} pallet) nhưng chưa xếp</h3>
      <p class="warn">Việc xếp bị lỗi. Bấm xếp lại: pallet nào đã vào khung sẽ không bị xếp đôi.</p>
      <button type="button" class="primary" id="btnRetry">Xếp lại lô ${escapeHtml(batch.code)}</button>
    `;
    document.getElementById('btnRetry').addEventListener('click', retryAllocate);
    return;
  }

  const roomRows = result.rooms.map((r) => `
    <tr>
      <td>${escapeHtml(r.roomName)}</td>
      <td>${r.count} pallet</td>
      <td><button type="button" class="primary" data-view-room="${r.roomId}">Xem 3D</button></td>
    </tr>`).join('');
  const rejectedRows = result.rejected.map((r) => `<tr class="bad"><td>${escapeHtml(r.palletCode)}</td><td>${escapeHtml(r.reason)}</td></tr>`).join('');

  outcomeBox.innerHTML = `
    <h3>Lô ${escapeHtml(batch.code)}: đã xếp ${result.placed} / ${batch.palletCount} pallet</h3>
    ${result.rooms.length ? `<table><thead><tr><th>Kho</th><th>Nhận</th><th></th></tr></thead><tbody>${roomRows}</tbody></table>` : ''}
    ${rejectedRows ? `<p class="warn">Chưa có khung (vẫn thuộc lô, thêm kệ rồi xếp lại sau):</p><table><tbody>${rejectedRows}</tbody></table>` : ''}
  `;
  for (const button of outcomeBox.querySelectorAll('[data-view-room]')) {
    button.addEventListener('click', () => viewRoom(Number(button.dataset.viewRoom)));
  }
}

// Sang trang Kho 3D của kho đó. Danh sách pallet vừa xếp được cất vào sessionStorage để trang 3D cho pallet bay vào
// đúng thứ tự server đã xếp (sessionStorage: dữ liệu tạm của tab trình duyệt, chuyển trang vẫn còn).
function viewRoom(roomId) {
  const room = outcome.result.rooms.find((r) => r.roomId === roomId);
  try {
    sessionStorage.setItem(`placements:${roomId}`, JSON.stringify(room.placements));
    sessionStorage.setItem(`play:${roomId}`, '1');
  } catch {
    // không có sessionStorage: vẫn sang được, chỉ không có hiệu ứng bay
  }
  location.href = `warehouse3d.html?roomId=${roomId}`;
}

// ---------- 8. Thêm loại hàng ----------
typeForm.addEventListener('submit', function (event) {
  event.preventDefault();
  run(async function () {
    await sendJson('POST', '/api/product-types', { code: typeForm.elements.code.value, name: typeForm.elements.name.value });
    typeForm.reset();
    await loadProductTypes();
    showMessage('Đã thêm loại hàng.');
  });
});
