import { getJson, sendJson } from '../shared/api.js';
import { askConfirm } from '../shared/dialog.js';
import { escapeHtml } from '../shared/format.js';
import { showMessage } from '../shared/message.js';
import { readRoomId, showRoomTabs, showTopBar } from '../shared/nav.js';

// Trang Nhập lô (receive.html?roomId=1). Vào một kho rồi mới nhập lô vào đúng kho đó.
//
// Luồng:
//  0. Mở trang: GET /api/room3d/{roomId} lấy tên kho; thiếu roomId hoặc không có kho → báo lỗi, khoá nút kiểm tra.
//  1. Người dùng nhập các dòng (số lượng + số đo).
//  2. "Kiểm tra kho"     → POST /api/rooms/{roomId}/batches/check   (không ghi gì; server báo kho nhận được bao nhiêu)
//  3. "Nhập vào kho này" → POST /api/batches   (chỉ tạo lô + pallet, CHƯA xếp vào khung)
//  4. "Xếp và xem 3D"    → POST /api/rooms/{roomId}/batches/{id}/allocate, xong thì mở màn 3D cho pallet bay vào.
//     Xếp lỗi thì lô vẫn đã nhập, bấm lại nút này (pallet đã vào khung không bị xếp đôi).
//
// Trang này không tự chọn kho hay khung, không tự đếm chỗ: mọi con số do server trả.

showTopBar('rooms');
const roomId = readRoomId();   // receive.html?roomId=3 → 3; không có → null
let roomName = '';             // tên kho, lấy lúc mở trang

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
let outcome = null;      // { batch }: lô vừa nhập, chờ bấm "Xếp và xem 3D"
let busy = false;        // đang chờ server: không cho bấm thêm

// ---------- 1. Mở trang ----------
addLines(LOT_B36);   // điền sẵn lô b36
loadRoom();
loadProductTypes();

// Lấy tên kho. Thiếu roomId hoặc không có kho thì báo lỗi và khoá nút kiểm tra.
async function loadRoom() {
  const btnCheck = document.getElementById('btnCheck');
  if (roomId == null) {
    document.getElementById('roomName').textContent = '(thiếu roomId trên địa chỉ trang)';
    btnCheck.disabled = true;
    return;
  }
  showRoomTabs(roomId, 'receive');
  try {
    const room = await getJson(`/api/room3d/${roomId}`);
    roomName = room.name;
    document.getElementById('roomName').textContent = room.name;
    document.getElementById('roomTitle').textContent = room.name;
  } catch (err) {
    document.getElementById('roomName').textContent = '(không mở được)';
    btnCheck.disabled = true;
    showMessage(err.message, true);
  }
}

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

// ---------- 5. Kiểm tra kho ----------
document.getElementById('btnCheck').addEventListener('click', function () {
  run(async function () {
    outcome = null;
    lastCheck = await sendJson('POST', `/api/rooms/${roomId}/batches/check`, readBatchRequest());
    drawCheck();
  });
});

// lastCheck: { total, lines: [{typeCode, color, isNewType, quantity, length, width, height}],
//              placeable, missing, notes: [câu lý do] }
function drawCheck() {
  const check = lastCheck;

  const typeRows = check.lines.map((l) => `
    <tr>
      <td><i class="chip" data-color="${l.color}"></i> Type ${escapeHtml(l.typeCode)}${l.isNewType ? ' <span class="muted">(mới)</span>' : ''}</td>
      <td>${l.quantity}</td>
      <td class="muted">${l.length} × ${l.width} × ${l.height} m</td>
    </tr>`).join('');

  const notes = check.notes.length ? `<br><span class="muted">${check.notes.map(escapeHtml).join('<br>')}</span>` : '';

  checkResult.innerHTML = `
    <h3>Lô gồm ${check.total} pallet</h3>
    <table><thead><tr><th>Type</th><th>SL</th><th>Số đo</th></tr></thead><tbody>${typeRows}</tbody></table>

    <h3>Kho ${escapeHtml(roomName)}</h3>
    <p class="${check.missing ? 'warn' : 'ok'}">Nhận được ${check.placeable} / ${check.total} pallet.
      ${check.missing ? `Thiếu ${check.missing} ô.${notes}` : 'Đủ chỗ cho cả lô.'}</p>
    <button type="button" class="primary" id="btnImport" ${check.placeable === 0 ? 'disabled' : ''}>Nhập vào kho này</button>
  `;

  // Tô màu cho các ô vuông type (màu lấy từ server, đặt bằng JS để HTML không chứa style)
  for (const chip of checkResult.querySelectorAll('.chip')) chip.style.background = chip.dataset.color;

  document.getElementById('btnImport').addEventListener('click', importBatch);
}

// ---------- 6. Nhập lô ----------

// Nhập lô vào kho đang mở. Kho thiếu chỗ thì hỏi lại (phần thiếu ở lại trong lô).
async function importBatch() {
  if (lastCheck.missing) {
    const ok = await askConfirm(
      `Kho ${roomName} thiếu ${lastCheck.missing} ô: ${lastCheck.missing} pallet sẽ ở lại trong lô, chưa có khung. Vẫn nhập vào kho này?`,
      { okText: 'Vẫn nhập' });
    if (!ok) return;
  }
  run(async function () {
    const batch = await sendJson('POST', '/api/batches', readBatchRequest());   // { batchId, code, palletCount }
    outcome = { batch: batch };
    clearCheck();
    showMessage(`Đã nhập lô ${batch.code} (${batch.palletCount} pallet). Bấm "Xếp và xem 3D" để xếp vào khung.`);
  });
}

// ---------- 7. Lô vừa nhập: nút "Xếp và xem 3D" ----------
function drawOutcome() {
  if (!outcome) {
    outcomeBox.innerHTML = '';
    return;
  }
  const { batch } = outcome;
  outcomeBox.innerHTML = `
    <h3>Lô ${escapeHtml(batch.code)} đã nhập (${batch.palletCount} pallet), chưa xếp vào khung</h3>
    <button type="button" class="primary" id="btnAllocate">Xếp và xem 3D</button>
  `;
  document.getElementById('btnAllocate').addEventListener('click', allocateAndView);
}

// Xếp lô vào kho đang mở rồi sang màn 3D. Lỗi thì ở lại trang này (run báo lỗi), bấm lại được: không xếp đôi.
// Danh sách pallet vừa xếp cất vào sessionStorage để màn 3D cho pallet bay vào đúng thứ tự server đã xếp
// (sessionStorage: dữ liệu tạm của tab trình duyệt, chuyển trang vẫn còn).
function allocateAndView() {
  run(async function () {
    const allocation = await sendJson('POST', `/api/rooms/${roomId}/batches/${outcome.batch.batchId}/allocate`);
    const placed = allocation.placements.length;
    const rejected = allocation.rejected.length;
    try {
      sessionStorage.setItem(`placements:${roomId}`, JSON.stringify(allocation.placements));
      sessionStorage.setItem(`play:${roomId}`, '1');
      sessionStorage.setItem(`note:${roomId}`, rejected
        ? `Lô ${outcome.batch.code}: đã xếp ${placed} pallet, ${rejected} pallet chưa có khung (${allocation.rejected[0].reason})`
        : `Lô ${outcome.batch.code}: đã xếp ${placed} pallet.`);
    } catch {
      // không có sessionStorage: vẫn sang được, chỉ không có hiệu ứng bay
    }
    location.href = `warehouse3d.html?roomId=${roomId}`;
  });
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
