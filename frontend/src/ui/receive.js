import { api } from '../api/client.js';
import { getState, loadSlots, setState, showMessage, subscribe } from '../state/store.js';
import { askConfirm } from './confirm.js';

// Bảng Nhập hàng gắn ngay trong màn Kho 3D (góc phải, thu gọn được): nhập lô (nhiều dòng "số lượng + số đo") → Xem trước → Xác nhận và xếp → bảng kết quả → xem pallet bay vào 3D.
// Frontend không chọn khung, không khớp type A/B, không tự đếm chỗ trống: tất cả do server trả lời
// (xem trước = POST …/batches/preview; xác nhận = POST /batches rồi POST …/batches/{id}/allocate).
// Dữ liệu đang nhập giữ trong biến của module và form luôn vẽ từ đó, nên vẽ lại không làm mất dòng đã nhập.

const SAMPLE = [   // lô ví dụ 6A + 2B của kế hoạch: 6 pallet thấp, 2 pallet cao chỉ lên được nóc
  { quantity: 6, length: 1.2, width: 0.8, height: 1.0 },
  { quantity: 2, length: 1.2, width: 0.8, height: 1.6 },
];

const blankLine = () => ({ quantity: 1, length: '', width: '', height: '', weight: '', productTypeId: '' });

export function createReceive(root) {
  let lines = [blankLine()];
  let types = [];
  let preview = null;     // kết quả xem trước của đúng các dòng đang nhập; null khi chưa xem hoặc đã sửa dòng
  let outcome = null;     // { batch: {batchId, code, palletCount}, allocation | null } sau khi bấm xác nhận
  let busy = false;
  let collapsed = false;
  let shownRoomId;        // phòng form đang vẽ cho; undefined = chưa vẽ

  root.addEventListener('click', onClick);
  root.addEventListener('input', onInput);
  root.addEventListener('submit', onSubmit);

  subscribe((state) => {
    if (state.step !== 'warehouse') {
      shownRoomId = undefined;   // quay lại bước này thì vẽ lại cho kịp dữ liệu mới
      return;
    }
    if (shownRoomId !== state.currentRoomId) {
      shownRoomId = state.currentRoomId;
      preview = null;
      outcome = null;
      render();
      if (state.currentRoomId != null) loadTypes();
    }
  });

  // ---------- vẽ ----------
  function render() {
    const state = getState();
    if (state.currentRoomId == null) {
      root.innerHTML = `${dockHeader('Nhập hàng')}<div class="dock-body" ${collapsed ? 'hidden' : ''}><p class="hint">Chưa mở phòng nào. Mở một phòng ở bước 1 rồi quay lại.</p></div>`;
      return;
    }
    root.innerHTML = `
      ${dockHeader(`Nhập hàng · ${escapeHtml(state.layout?.name ?? '')}`)}
      <div class="dock-body" ${collapsed ? 'hidden' : ''}>
      <p class="hint">Mỗi dòng là một nhóm pallet cùng số đo (dài, rộng, cao bằng mét; kg có thể để trống). Type A, B… do hệ thống tự khớp hoặc tạo.</p>
      <table class="lines">
        <thead><tr><th title="Số pallet">SL</th><th title="Chiều dài (m)">Dài</th><th title="Chiều rộng (m)">Rộng</th><th title="Chiều cao (m)">Cao</th><th title="Khối lượng một pallet (kg), để trống nếu không kiểm tra tải">kg</th><th>Loại hàng</th><th></th></tr></thead>
        <tbody>${lines.map(lineRow).join('')}</tbody>
      </table>
      <div class="receive-actions">
        <button type="button" data-action="add-line">+ Thêm dòng</button>
        <button type="button" data-action="sample">Lô ví dụ 6A + 2B</button>
        <button type="button" data-action="random">Sinh ngẫu nhiên 30 pallet</button>
        <button type="button" class="primary" data-action="preview" ${busy ? 'disabled' : ''}>Xem trước</button>
      </div>
      <div data-role="preview">${previewHtml()}</div>
      <div data-role="outcome">${outcomeHtml()}</div>
      <div class="receive-footer">
        <form data-form="product-type" class="inline-form">
          <strong>Loại hàng mới</strong>
          <input name="code" placeholder="Mã (OV)" maxlength="20" required />
          <input name="name" placeholder="Tên (Ốc vít)" maxlength="50" required />
          <button type="submit">Thêm</button>
        </form>
        <button type="button" class="danger" data-action="reset" ${busy ? 'disabled' : ''}>Đặt lại kho (lấy mọi pallet ra khỏi khung)</button>
      </div>
      </div>
    `;
  }

  function dockHeader(title) {
    return `<div class="dock-header"><strong>${title}</strong>
      <button type="button" data-action="toggle-dock">${collapsed ? 'Mở ▸' : 'Thu gọn ▾'}</button></div>`;
  }

  function lineRow(line, i) {
    const options = ['<option value="">Chưa phân loại</option>',
      ...types.map((t) => `<option value="${t.id}" ${String(t.id) === String(line.productTypeId) ? 'selected' : ''}>${escapeHtml(t.code)} · ${escapeHtml(t.name)}</option>`)];
    const cell = (name, value, step = 'any', min = '0') =>
      `<td><input type="number" name="${name}" value="${value}" min="${min}" step="${step}" data-line="${i}" /></td>`;
    return `<tr>
      ${cell('quantity', line.quantity, '1', '1')}
      ${cell('length', line.length)}${cell('width', line.width)}${cell('height', line.height)}${cell('weight', line.weight)}
      <td><select name="productTypeId" data-line="${i}">${options.join('')}</select></td>
      <td><button type="button" class="icon danger" title="Xoá dòng" data-action="remove-line" data-line="${i}" ${lines.length === 1 ? 'disabled' : ''}>×</button></td>
    </tr>`;
  }

  function previewHtml() {
    if (!preview) return '';
    const rows = preview.lines.map((l) => `
      <tr class="status-${l.status}">
        <td><i class="chip" style="background:${l.color}"></i> Type ${escapeHtml(l.typeCode)}${l.isNewType ? ' <span class="muted">(mới)</span>' : ''}</td>
        <td>${l.quantity}</td>
        <td>${l.placeable}</td>
        <td>${escapeHtml(l.message)}</td>
      </tr>`).join('');
    const all = preview.placeable === preview.total;
    return `
      <h3>Xem trước</h3>
      <table class="result"><thead><tr><th>Type</th><th>Cần</th><th>Xếp được</th><th>Kết quả</th></tr></thead><tbody>${rows}</tbody></table>
      <p class="${all ? 'ok' : 'warn'}">Xếp được ${preview.placeable} / ${preview.total} pallet${all ? '.' : '; phần còn lại sẽ ở lại trong lô, chưa có khung.'}</p>
      <button type="button" class="primary" data-action="confirm" ${busy ? 'disabled' : ''}>Xác nhận và xếp</button>
    `;
  }

  function outcomeHtml() {
    if (!outcome) return '';
    const { batch, allocation } = outcome;
    if (!allocation) {
      return `
        <h3>Lô ${escapeHtml(batch.code)} đã nhập (${batch.palletCount} pallet) nhưng chưa xếp</h3>
        <p class="warn">Việc xếp bị lỗi. Bấm xếp lại: pallet nào đã vào khung sẽ không bị xếp đôi.</p>
        <button type="button" class="primary" data-action="retry-allocate" ${busy ? 'disabled' : ''}>Xếp lại lô ${escapeHtml(batch.code)}</button>
      `;
    }
    const top = allocation.placements.filter((p) => p.isTop).length;
    const placed = allocation.placements.map((p) => `
      <tr><td>${escapeHtml(p.palletCode)}</td><td><i class="chip" style="background:${p.color}"></i> ${escapeHtml(p.typeCode)}</td>
      <td>${escapeHtml(p.slotCode)}${p.isTop ? ' (nóc)' : ''}</td></tr>`).join('');
    const rejected = allocation.rejected.map((r) => `<tr class="status-NoRack"><td>${escapeHtml(r.palletCode)}</td><td colspan="2">${escapeHtml(r.reason)}</td></tr>`).join('');
    return `
      <h3>Lô ${escapeHtml(batch.code)}: đã xếp ${allocation.placements.length} / ${batch.palletCount} pallet${top ? ` (${top} lên nóc)` : ''}${allocation.alreadyPlaced ? `, ${allocation.alreadyPlaced} pallet đã có khung từ trước` : ''}</h3>
      <table class="result"><thead><tr><th>Pallet</th><th>Type</th><th>Khung</th></tr></thead><tbody>${placed}${rejected}</tbody></table>
      ${allocation.placements.length ? '<button type="button" data-action="replay">Phát lại hiệu ứng bay</button>' : ''}
    `;
  }

  // ---------- dữ liệu ----------
  async function loadTypes() {
    try {
      types = await api.listProductTypes();
      if (getState().step === 'warehouse') render();
    } catch (err) {
      showMessage(err.message, 'error');
    }
  }

  function batchRequest() {
    const num = (v) => (v === '' || v == null ? null : Number(v));
    return {
      lines: lines.map((l) => ({
        quantity: Number(l.quantity), length: Number(l.length), width: Number(l.width), height: Number(l.height),
        weight: num(l.weight), productTypeId: l.productTypeId === '' ? null : Number(l.productTypeId),
      })),
    };
  }

  const roomId = () => getState().currentRoomId;

  async function run(work) {
    busy = true;
    render();
    try {
      await work();
    } catch (err) {
      showMessage(err.message, 'error');
    } finally {
      busy = false;
      render();
    }
  }

  // ---------- sự kiện ----------
  function onInput(e) {
    const el = e.target;
    if (el.dataset.line === undefined) return;
    lines[Number(el.dataset.line)][el.name] = el.value;
    preview = null;   // sửa dòng thì kết quả xem trước cũ không còn đúng
    root.querySelector('[data-role="preview"]').innerHTML = '';
  }

  async function onSubmit(e) {
    e.preventDefault();
    if (e.target.dataset.form !== 'product-type') return;
    const data = new FormData(e.target);
    await run(async () => {
      await api.createProductType({ code: data.get('code'), name: data.get('name') });
      types = await api.listProductTypes();
      showMessage('Đã thêm loại hàng.');
    });
  }

  function onClick(e) {
    const button = e.target.closest('button[data-action]');
    if (!button) return;
    const action = button.dataset.action;

    if (action === 'add-line') { lines.push(blankLine()); preview = null; render(); }
    else if (action === 'remove-line') { lines.splice(Number(button.dataset.line), 1); preview = null; render(); }
    else if (action === 'sample') { lines = SAMPLE.map((l) => ({ ...blankLine(), ...l })); preview = null; render(); }
    else if (action === 'random') { lines = randomLines(30); preview = null; render(); }
    else if (action === 'preview') doPreview();
    else if (action === 'confirm') doConfirm();
    else if (action === 'retry-allocate') doAllocate(outcome.batch);
    else if (action === 'replay') setState({ pendingPlacements: outcome.allocation.placements });
    else if (action === 'toggle-dock') { collapsed = !collapsed; render(); }
    else if (action === 'reset') doReset();
  }

  function doPreview() {
    return run(async () => {
      preview = await api.previewBatch(roomId(), batchRequest());
      outcome = null;
    });
  }

  function doConfirm() {
    return run(async () => {
      const batch = await api.receiveBatch(batchRequest());
      outcome = { batch, allocation: null };
      preview = null;
      outcome.allocation = await api.allocateBatch(roomId(), batch.batchId);   // lỗi ở đây: lô vẫn đã nhập, có nút xếp lại
      await loadSlots();
      rememberPlacements(outcome.allocation);
      showMessage(`Đã xếp lô ${batch.code}.`);
    });
  }

  function doAllocate(batch) {
    return run(async () => {
      outcome = { batch, allocation: await api.allocateBatch(roomId(), batch.batchId) };
      await loadSlots();
      rememberPlacements(outcome.allocation);
    });
  }

  async function doReset() {
    if (!await askConfirm('Lấy mọi pallet ra khỏi khung của phòng này? Pallet và lô vẫn còn, chỉ chưa có khung.', { okText: 'Đặt lại', danger: true })) return;
    await run(async () => {
      const { cleared } = await api.resetPlacements(roomId());
      outcome = null;
      setState({ lastPlacements: null, pendingPlacements: null });   // không còn gì để phát lại
      await loadSlots();
      showMessage(`Đã lấy ${cleared} pallet ra khỏi khung.`);
    });
  }

  // Nhớ danh sách vị trí server vừa xếp (theo đúng thứ tự) và hẹn phát hiệu ứng ngay: loadSlots ở trên đã dựng lại cảnh
  // với khung mới, nên pallet bay vào ngay trong màn này. Muốn xem lại thì dùng nút "Phát lại".
  function rememberPlacements(allocation) {
    const placements = allocation.placements;
    setState({ lastPlacements: placements.length ? placements : null, pendingPlacements: placements.length ? placements : null });
  }

}

/** Sinh ngẫu nhiên các dòng tổng cộng `total` pallet từ vài số đo thường gặp. Chỉ là dữ liệu nhập mẫu, không phải luật. */
function randomLines(total) {
  const sizes = [
    { length: 1.2, width: 0.8, height: 1.0 }, { length: 1.2, width: 0.8, height: 1.6 },
    { length: 1.0, width: 0.8, height: 0.9 }, { length: 0.8, width: 0.6, height: 0.7 },
  ];
  const lines = [];
  let left = total;
  sizes.forEach((size, i) => {
    const quantity = i === sizes.length - 1 ? left : Math.min(left, 1 + Math.floor(Math.random() * (left / 2)));
    left -= quantity;
    if (quantity > 0) lines.push({ ...blankLine(), ...size, quantity });
  });
  return lines;
}

function escapeHtml(text) {
  return String(text).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
}
