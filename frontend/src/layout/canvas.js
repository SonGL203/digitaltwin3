import { formatMeters } from '../shared/format.js';
import { showMessage } from '../shared/message.js';
import {
  addObstacle, addRack, addRacks, arrayRequests, clearPreview, moveRack, nextRackCode, previewRack, previewRackArray,
  rackToRequest, removeObstacle, removeRack, rotateRack, select, selectedObstacle, selectedRack, setShowGrid, setTool,
  snap, state, stopPreviewCheck,
} from './state.js';

// Canvas 2D: vẽ mặt bằng nhìn từ trên xuống bằng <canvas> và các công cụ chuột.
//
// Toạ độ thế giới = mét, gốc (0; 0) ở góc trên trái phòng, X sang phải, Y đi xuống (đúng như server).
// Toạ độ màn hình = px CSS. Đổi qua lại bằng view { scale (px trên 1 m), offsetX, offsetY }:
//     sx = offsetX + x × scale          x = (sx − offsetX) / scale
//
// Canvas chỉ VẼ và GỬI: kích thước kệ (kể cả khi xoay) lấy từ server, đặt được hay không do server trả lời
// qua bóng mờ xanh/đỏ (state.preview). Việc "hít" theo lưới là của giao diện.
//
// Công cụ (state.tool):
//   select      bấm để chọn kệ/vật cản; kéo kệ để dời (bóng mờ theo chuột, thả để đổi vị trí trong bản nháp);
//               kéo chỗ trống để dời màn hình
//   add-rack    bóng mờ kệ mới theo chuột, bấm để đặt (cấu hình lấy từ state.newRack, mã tự sinh)
//   add-array   bóng mờ cả dãy kệ theo chuột (số hàng, số kệ mỗi hàng ở state.newArray), bấm để đặt cả dãy
//   add-column  bấm để đặt cột 0,4 × 0,4 m
// Phím: R xoay (đang kéo / đang đặt → xoay bóng; đang chọn kệ → xoay kệ thật), Esc huỷ, Delete xoá.

const MIN_SCALE = 4;        // px / m: nhìn cả kho rất lớn
const MAX_SCALE = 400;      // px / m: soi chi tiết từng ô kệ
const FIT_PADDING = 32;     // px chừa quanh phòng khi "vừa khung"
const MIN_GRID_PX = 6;      // ô lưới nhỏ hơn thế này trên màn hình thì không vẽ (dày đặc, rối mắt)
const CLICK_TOLERANCE = 4;  // px: chuột xê dịch ít hơn thế giữa nhấn và thả thì coi là bấm, không phải kéo
const COLUMN_SIZE = 0.4;    // m: cột đặt bằng chuột (muốn cỡ khác thì nhập ở bảng bên)

const HINTS = {
  select: 'Kéo kệ để dời · R xoay · Delete xoá kệ/cột đang chọn',
  'add-rack': 'Bấm để đặt · R xoay · Esc huỷ',
  'add-array': 'Bấm để đặt cả dãy · R xoay · Esc huỷ',
  'add-column': 'Bấm để đặt cột · Esc huỷ',
};

// container: thẻ chứa canvas. onCursor(info): gọi khi chuột đi qua (để hiện toạ độ ở thanh dưới).
// Trả về { redraw, fit }: trang gọi redraw() mỗi khi dữ liệu đổi.
export function createCanvas(container, { onCursor } = {}) {
  const canvas = document.createElement('canvas');
  canvas.tabIndex = 0;   // để nhận phím R / Esc khi đang làm việc trên canvas
  container.appendChild(canvas);
  const ctx = canvas.getContext('2d');

  // Các nút trên thanh công cụ (HTML nằm trong layout.html)
  const toolButtons = document.querySelectorAll('[data-tool]');
  const zoomLabel = document.getElementById('zoomLabel');
  const gridCheckbox = document.getElementById('chkGrid');
  const gridLabel = document.getElementById('gridLabel');
  const toolHint = document.getElementById('toolHint');

  const view = { scale: 40, offsetX: 0, offsetY: 0 };
  let size = { width: 0, height: 0 };
  let fittedRoomKey = null;   // phòng nào đã "vừa khung"; đổi kích thước phòng thì vừa khung lại
  let gesture = null;         // thao tác chuột đang diễn ra (xem pointerdown)
  let hoverColumn = null;     // vị trí bóng cột khi dùng công cụ add-column
  let lastAddPos = null;      // vị trí (đã snap) lần cuối hỏi server cho kệ mới: không hỏi lại khi chưa đổi ô
  let lastTool = state.tool;

  // ---------- đổi toạ độ ----------
  const toScreenX = (x) => view.offsetX + x * view.scale;
  const toScreenY = (y) => view.offsetY + y * view.scale;
  const toWorld = (sx, sy) => ({ x: (sx - view.offsetX) / view.scale, y: (sy - view.offsetY) / view.scale });

  function fit() {
    const room = state.room;
    if (!room || size.width === 0) return;
    const sx = (size.width - 2 * FIT_PADDING) / room.width;
    const sy = (size.height - 2 * FIT_PADDING) / room.depth;
    view.scale = clamp(Math.min(sx, sy), MIN_SCALE, MAX_SCALE);
    view.offsetX = (size.width - room.width * view.scale) / 2;
    view.offsetY = (size.height - room.depth * view.scale) / 2;
    draw();
  }

  function zoomAt(factor, sx, sy) {
    const before = toWorld(sx, sy);
    view.scale = clamp(view.scale * factor, MIN_SCALE, MAX_SCALE);
    // Giữ nguyên điểm dưới con trỏ: zoom "vào chỗ đang nhìn" thay vì vào góc màn hình
    view.offsetX = sx - before.x * view.scale;
    view.offsetY = sy - before.y * view.scale;
    draw();
  }

  // ================= VẼ =================
  // draw() chỉ hẹn vẽ ở khung hình kế tiếp: một sự kiện gọi draw() nhiều lần (fit + redraw, chuột di liên tục)
  // vẫn chỉ vẽ một lần. Kho hàng nghìn kệ mà vẽ lại đồng bộ mỗi lần gọi thì trình duyệt đơ.
  let frame = 0;
  function draw() {
    if (frame) return;
    frame = requestAnimationFrame(function () {
      frame = 0;
      drawNow();
    });
  }

  function drawNow() {
    const colors = readColors();
    const dpr = window.devicePixelRatio || 1;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);   // vẽ bằng px CSS, nét vẫn sắc trên màn hình mật độ cao
    ctx.fillStyle = colors.outside;
    ctx.fillRect(0, 0, size.width, size.height);
    zoomLabel.textContent = `${Math.round(view.scale)} px/m`;

    const room = state.room;
    if (!room) return;

    const left = toScreenX(0);
    const top = toScreenY(0);
    const w = room.width * view.scale;
    const h = room.depth * view.scale;

    ctx.fillStyle = colors.floor;
    ctx.fillRect(left, top, w, h);
    if (state.showGrid) drawGrid(room, colors, left, top, w, h);

    for (const o of room.obstacles) drawObstacle(o, colors, o.id === state.selectedObstacleId);

    const movingId = state.preview?.source === 'drag' ? state.preview.rackId : null;
    drawRacks(room.racks, colors, movingId);

    // Mép phòng vẽ sau kệ, đậm: đó chính là tường bao
    ctx.strokeStyle = colors.roomEdge;
    ctx.lineWidth = 2;
    ctx.strokeRect(left, top, w, h);

    if (state.tool === 'add-column' && hoverColumn) drawColumnGhost(hoverColumn, colors);
    if (state.preview) drawPreview(state.preview, colors);
    if (state.arrayPreview) drawArrayPreview(state.arrayPreview, colors);
  }

  function drawGrid(room, colors, left, top, w, h) {
    const step = room.gridSize;
    if (step * view.scale < MIN_GRID_PX) return;

    ctx.lineWidth = 1;
    // Mỗi 1 m vẽ đậm hơn nếu bước lưới chia hết 1 m (ví dụ 0,5 m → cứ 2 ô một vạch đậm), để dễ đọc kích thước
    const perMeter = Math.round(1 / step);
    const hasMajor = perMeter > 1 && Math.abs(perMeter * step - 1) < 1e-9;

    // Chỉ các vạch nằm trong màn hình; vạch thường và vạch đậm mỗi loại gom vào một path
    const px = step * view.scale;
    const cols = Math.floor(room.width / step + 1e-9);
    const rows = Math.floor(room.depth / step + 1e-9);
    const firstCol = Math.max(1, Math.ceil(-left / px));
    const lastCol = Math.min(cols, Math.floor((size.width - left) / px));
    const firstRow = Math.max(1, Math.ceil(-top / px));
    const lastRow = Math.min(rows, Math.floor((size.height - top) / px));
    const y1 = Math.max(top, 0);
    const y2 = Math.min(top + h, size.height);
    const x1 = Math.max(left, 0);
    const x2 = Math.min(left + w, size.width);

    for (const major of hasMajor ? [false, true] : [false]) {
      ctx.strokeStyle = major ? colors.gridMajor : colors.grid;
      ctx.beginPath();
      for (let i = firstCol; i <= lastCol; i++) {
        if (hasMajor && (i % perMeter === 0) !== major) continue;
        const x = Math.round(left + i * px) + 0.5;
        if (x > left + w) break;
        ctx.moveTo(x, y1);
        ctx.lineTo(x, y2);
      }
      for (let j = firstRow; j <= lastRow; j++) {
        if (hasMajor && (j % perMeter === 0) !== major) continue;
        const y = Math.round(top + j * px) + 0.5;
        if (y > top + h) break;
        ctx.moveTo(x1, y);
        ctx.lineTo(x2, y);
      }
      ctx.stroke();
    }
  }

  function drawObstacle(o, colors, selected) {
    const x = toScreenX(o.x);
    const y = toScreenY(o.y);
    const w = o.width * view.scale;
    const h = o.depth * view.scale;
    ctx.fillStyle = o.kind === 'Wall' ? colors.wall : colors.column;
    ctx.fillRect(x, y, w, h);
    if (selected) {
      ctx.strokeStyle = colors.rackSelected;
      ctx.lineWidth = 3;
      ctx.strokeRect(x - 2, y - 2, w + 4, h + 4);
    }
  }

  /**
   * Vẽ mọi kệ theo lô: chỉ kệ nằm trong màn hình, mỗi loại nét (nền, vạch chia ô, viền) gom vào MỘT path rồi
   * fill/stroke một lần, thay vì mỗi kệ, mỗi vạch một lệnh stroke (9 990 kệ × 9 vạch ≈ 90 000 lệnh).
   * Kệ đang chọn và kệ đang kéo vẽ riêng sau cùng bằng drawRack (viền đậm, mờ).
   */
  function drawRacks(racks, colors, movingId) {
    const visible = [];
    let selected = null;
    let moving = null;
    for (const r of racks) {
      if (r.id === movingId) { moving = r; continue; }
      if (r.id === state.selectedRackId) { selected = r; continue; }
      const x = toScreenX(r.x);
      const y = toScreenY(r.y);
      const w = r.footprintWidth * view.scale;
      const h = r.footprintDepth * view.scale;
      if (x > size.width || y > size.height || x + w < 0 || y + h < 0) continue;   // ngoài màn hình
      visible.push({ r, x, y, w, h });
    }

    ctx.fillStyle = colors.rack;
    ctx.beginPath();
    for (const v of visible) ctx.rect(v.x, v.y, v.w, v.h);
    ctx.fill();

    // Vạch chia ô: chỉ khi một ô rộng ít nhất 3 px trên màn hình, nhỏ hơn thì chỉ thấy một mảng đặc
    ctx.strokeStyle = colors.rackBay;
    ctx.lineWidth = 1;
    ctx.beginPath();
    for (const { r, x, y, w, h } of visible) {
      const step = r.bayWidth * view.scale;
      if (step < 3) continue;
      for (let i = 1; i < r.bays; i++) {
        const d = i * step;
        if (r.rotation === 90) { ctx.moveTo(x, y + d); ctx.lineTo(x + w, y + d); }
        else { ctx.moveTo(x + d, y); ctx.lineTo(x + d, y + h); }
      }
    }
    ctx.stroke();

    ctx.strokeStyle = colors.rackEdge;
    ctx.lineWidth = 1.5;
    ctx.beginPath();
    for (const v of visible) ctx.rect(v.x, v.y, v.w, v.h);
    ctx.stroke();

    for (const v of visible) drawLabel(v.r.code, v.x, v.y, v.w, v.h, colors.label);   // tự bỏ khi kệ quá nhỏ

    if (selected) drawRack(selected, colors, true, false);
    if (moving) drawRack(moving, colors, moving.id === state.selectedRackId, true);
  }

  function drawRack(r, colors, selected, moving) {
    const x = toScreenX(r.x);
    const y = toScreenY(r.y);
    const w = r.footprintWidth * view.scale;   // kích thước chiếm sàn do server trả (đã tính xoay)
    const h = r.footprintDepth * view.scale;

    ctx.globalAlpha = moving ? 0.35 : 1;   // kệ đang bị kéo: mờ đi ở chỗ cũ, bóng mờ hiện ở chỗ mới
    ctx.fillStyle = colors.rack;
    ctx.fillRect(x, y, w, h);

    // Vạch chia ô hàng: chạy dọc theo chiều dài kệ (X khi không xoay, Y khi xoay 90°). Chỉ để nhìn
    ctx.strokeStyle = colors.rackBay;
    ctx.lineWidth = 1;
    for (let i = 1; i < r.bays; i++) {
      const d = i * r.bayWidth * view.scale;
      if (r.rotation === 90) line(x, y + d, x + w, y + d);
      else line(x + d, y, x + d, y + h);
    }

    ctx.strokeStyle = selected ? colors.rackSelected : colors.rackEdge;
    ctx.lineWidth = selected ? 3 : 1.5;
    ctx.strokeRect(x, y, w, h);
    drawLabel(r.code, x, y, w, h, colors.label);
    ctx.globalAlpha = 1;
  }

  function drawPreview(p, colors) {
    const x = toScreenX(p.request.x);
    const y = toScreenY(p.request.y);
    // Chưa có kích thước từ server (vừa xoay, đang chờ trả lời): chỉ đánh dấu góc, không đoán kích thước
    if (p.footprintWidth == null) {
      ctx.fillStyle = colors.ghostPendingEdge;
      ctx.beginPath();
      ctx.arc(x, y, 4, 0, Math.PI * 2);
      ctx.fill();
      return;
    }

    const w = p.footprintWidth * view.scale;
    const h = p.footprintDepth * view.scale;
    const [fill, edge] = p.isValid === null
      ? [colors.ghostPending, colors.ghostPendingEdge]
      : p.isValid ? [colors.ghostOk, colors.ghostOkEdge] : [colors.ghostBad, colors.ghostBadEdge];

    ctx.fillStyle = fill;
    ctx.fillRect(x, y, w, h);
    ctx.setLineDash([6, 4]);
    ctx.strokeStyle = edge;
    ctx.lineWidth = 2;
    ctx.strokeRect(x, y, w, h);
    ctx.setLineDash([]);
    drawLabel(p.request.code, x, y, w, h, edge);

    // Lý do không đặt được: hiện ngay trên bóng mờ
    if (p.isValid === false && p.reason) drawTag(p.reason, x, y - 6, edge);
  }

  // Bóng mờ cả dãy: mỗi kệ một ô xám (đang chờ) / xanh / đỏ; lý do của kệ đỏ đầu tiên hiện trên kệ đó
  function drawArrayPreview(a, colors) {
    let tagged = false;
    a.requests.forEach((r, i) => {
      const c = a.results?.[i];
      const x = toScreenX(r.x);
      const y = toScreenY(r.y);
      const w = (c?.footprintWidth ?? r.footprintWidth) * view.scale;
      const h = (c?.footprintDepth ?? r.footprintDepth) * view.scale;
      const [fill, edge] = !c
        ? [colors.ghostPending, colors.ghostPendingEdge]
        : c.isValid ? [colors.ghostOk, colors.ghostOkEdge] : [colors.ghostBad, colors.ghostBadEdge];

      ctx.fillStyle = fill;
      ctx.fillRect(x, y, w, h);
      ctx.setLineDash([6, 4]);
      ctx.strokeStyle = edge;
      ctx.lineWidth = 2;
      ctx.strokeRect(x, y, w, h);
      ctx.setLineDash([]);
      drawLabel(r.code, x, y, w, h, edge);

      if (c && !c.isValid && c.reason && !tagged) {
        drawTag(`${r.code}: ${c.reason}`, x, y - 6, edge);
        tagged = true;
      }
    });
    // Không kệ nào đỏ nhưng bản nháp có lỗi chung (hoặc mất mạng): hiện lý do trên kệ đầu tiên
    if (!tagged && a.reason && a.requests.length) {
      drawTag(a.reason, toScreenX(a.requests[0].x), toScreenY(a.requests[0].y) - 6, colors.ghostBadEdge);
    }
  }

  function drawColumnGhost(c, colors) {
    ctx.fillStyle = colors.ghostPending;
    ctx.strokeStyle = colors.ghostPendingEdge;
    ctx.lineWidth = 1.5;
    const s = COLUMN_SIZE * view.scale;
    ctx.fillRect(toScreenX(c.x), toScreenY(c.y), s, s);
    ctx.strokeRect(toScreenX(c.x), toScreenY(c.y), s, s);
  }

  function drawLabel(text, x, y, w, h, color) {
    // Nhãn ở giữa; quá nhỏ trên màn hình thì bỏ cho đỡ rối
    const fontPx = Math.min(14, Math.min(w, h) * 0.6);
    if (fontPx < 8) return;
    ctx.fillStyle = color;
    ctx.font = `600 ${fontPx}px system-ui, sans-serif`;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText(text, x + w / 2, y + h / 2);
  }

  function drawTag(text, x, bottom, color) {
    ctx.font = '600 12px system-ui, sans-serif';
    const padding = 4;
    const width = ctx.measureText(text).width + padding * 2;
    const tagX = clamp(x, 4, Math.max(4, size.width - width - 4));
    const tagY = Math.max(4, bottom - 20);
    ctx.fillStyle = color;
    ctx.fillRect(tagX, tagY, width, 20);
    ctx.fillStyle = '#ffffff';
    ctx.textAlign = 'left';
    ctx.textBaseline = 'middle';
    ctx.fillText(text, tagX + padding, tagY + 10);
  }

  function line(x1, y1, x2, y2) {
    ctx.beginPath();
    ctx.moveTo(x1, y1);
    ctx.lineTo(x2, y2);
    ctx.stroke();
  }

  // ================= TÌM VẬT DƯỚI CHUỘT =================
  const inside = (p, x, y, w, h) => p.x >= x && p.x <= x + w && p.y >= y && p.y <= y + h;

  /** Kệ dưới điểm (mét). Duyệt ngược để lấy kệ vẽ sau cùng (nằm trên). */
  function rackAt(p) {
    const racks = state.room?.racks ?? [];
    for (let i = racks.length - 1; i >= 0; i--) {
      const r = racks[i];
      if (inside(p, r.x, r.y, r.footprintWidth, r.footprintDepth)) return r;
    }
    return null;
  }

  function obstacleAt(p) {
    const obstacles = state.room?.obstacles ?? [];
    for (let i = obstacles.length - 1; i >= 0; i--) {
      const o = obstacles[i];
      if (inside(p, o.x, o.y, o.width, o.depth)) return o;
    }
    return null;
  }

  // ================= CHUỘT =================
  // gesture (thao tác đang diễn ra):
  //   { kind: 'pending', startX, startY, downAt, rack, obstacle }  vừa nhấn, chưa biết là bấm hay kéo
  //   { kind: 'pan', startX, startY, offsetX, offsetY }            kéo chỗ trống / nút giữa: dời màn hình
  //   { kind: 'move', rack, grabX, grabY, rotation, lastX, lastY } kéo kệ: bóng mờ theo chuột

  canvas.addEventListener('pointerdown', function (e) {
    canvas.focus();
    if (!state.room) return;
    const p = toWorld(e.offsetX, e.offsetY);

    if (e.button === 1) {   // nút giữa: dời màn hình
      canvas.setPointerCapture(e.pointerId);
      gesture = { kind: 'pan', startX: e.offsetX, startY: e.offsetY, offsetX: view.offsetX, offsetY: view.offsetY };
      return;
    }
    if (e.button !== 0) return;

    if (state.tool === 'add-rack') {
      placeNewRack();
      return;
    }
    if (state.tool === 'add-array') {
      placeArray();
      return;
    }
    if (state.tool === 'add-column') {
      placeColumn(p);
      return;
    }

    canvas.setPointerCapture(e.pointerId);
    const rack = rackAt(p);
    gesture = {
      kind: 'pending', startX: e.offsetX, startY: e.offsetY, downAt: p,
      rack: rack, obstacle: rack ? null : obstacleAt(p),
      offsetX: view.offsetX, offsetY: view.offsetY,
    };
  });

  canvas.addEventListener('pointermove', function (e) {
    const p = toWorld(e.offsetX, e.offsetY);
    reportCursor(p);

    if (!gesture) {
      if (state.tool === 'add-rack') {
        updateNewRackPreview(p);
      } else if (state.tool === 'add-array') {
        updateArrayPreview(p);
      } else if (state.tool === 'add-column') {
        hoverColumn = { x: snap(p.x), y: snap(p.y) };
        draw();
      } else {
        canvas.style.cursor = rackAt(p) ? 'move' : '';
      }
      return;
    }

    if (gesture.kind === 'pending') {
      if (Math.hypot(e.offsetX - gesture.startX, e.offsetY - gesture.startY) < CLICK_TOLERANCE) return;
      if (gesture.rack) {
        const r = gesture.rack;
        select(r.id, null);
        // Giữ khoảng cách từ chỗ NHẤN chuột tới góc kệ, để kệ không "nhảy" góc vào con trỏ.
        // Phải dùng điểm lúc nhấn (downAt), không phải điểm hiện tại: lúc này chuột đã đi được một đoạn.
        const { downAt } = gesture;
        gesture = { kind: 'move', rack: r, grabX: downAt.x - r.x, grabY: downAt.y - r.y, rotation: r.rotation, lastX: null, lastY: null };
      } else {
        gesture = { ...gesture, kind: 'pan' };
      }
    }

    if (gesture.kind === 'pan') {
      canvas.style.cursor = 'grabbing';
      view.offsetX = gesture.offsetX + (e.offsetX - gesture.startX);
      view.offsetY = gesture.offsetY + (e.offsetY - gesture.startY);
      draw();
    } else if (gesture.kind === 'move') {
      updateMovePreview(snap(p.x - gesture.grabX), snap(p.y - gesture.grabY));
    }
  });

  canvas.addEventListener('pointerup', function (e) {
    if (!gesture) return;
    const g = gesture;
    gesture = null;
    canvas.style.cursor = '';

    if (g.kind === 'pending') {
      // Bấm (không kéo): chọn kệ, hoặc vật cản, hoặc bỏ chọn
      select(g.rack?.id ?? null, g.obstacle?.id ?? null);
    } else if (g.kind === 'move') {
      // Lấy vị trí ngay lúc thả: kéo nhanh thì lần di chuột cuối có thể còn ở giữa đường
      const p = toWorld(e.offsetX, e.offsetY);
      dropMovedRack(g, snap(p.x - g.grabX), snap(p.y - g.grabY));
    }
    if (canvas.hasPointerCapture(e.pointerId)) canvas.releasePointerCapture(e.pointerId);
  });

  canvas.addEventListener('pointerleave', function () {
    onCursor?.(null);
    if (hoverColumn) {
      hoverColumn = null;
      draw();
    }
  });

  canvas.addEventListener('wheel', function (e) {
    e.preventDefault();
    zoomAt(Math.exp(-e.deltaY * 0.0015), e.offsetX, e.offsetY);
  }, { passive: false });

  // ---------- dời kệ ----------
  function updateMovePreview(x, y) {
    const g = gesture;
    if (x === g.lastX && y === g.lastY) return;   // chưa sang ô lưới khác: không hỏi lại server
    g.lastX = x;
    g.lastY = y;
    const r = g.rack;
    // Kích thước gợi ý = kích thước server đã trả cho chính kệ này (khi chưa xoay), để bóng hiện ngay
    const footprint = g.rotation === r.rotation ? { width: r.footprintWidth, depth: r.footprintDepth } : null;
    previewRack({ ...rackToRequest(r), x, y, rotation: g.rotation }, { rackId: r.id, source: 'drag', footprint });
  }

  function dropMovedRack(g, x, y) {
    const preview = state.preview;
    const r = g.rack;
    if (x === r.x && y === r.y && g.rotation === r.rotation) {   // thả lại đúng chỗ cũ
      clearPreview();
      return;
    }
    // Bóng mờ đúng ở chỗ thả và đã biết chắc là đỏ thì không đổi.
    // Còn lại (đang chờ, hoặc chưa kịp kiểm tra) cứ đổi: server luôn kiểm tra lại lần nữa khi lưu nháp.
    const checkedHere = preview && preview.request.x === x && preview.request.y === y && preview.request.rotation === g.rotation;
    if (checkedHere && preview.isValid === false) {
      showMessage(`Không dời được ${r.code}: ${preview.reason}`, true);
      clearPreview();
      return;
    }
    stopPreviewCheck();   // đã đổi vào bản nháp rồi, không cần hỏi nữa
    moveRack(r, x, y, g.rotation);
    clearPreview();
  }

  // ---------- đặt kệ mới ----------
  function newRackRequest(x, y) {
    return { ...state.newRack, code: nextRackCode(), x, y };
  }

  function updateNewRackPreview(p) {
    const x = snap(p.x);
    const y = snap(p.y);
    if (lastAddPos && lastAddPos.x === x && lastAddPos.y === y) return;
    lastAddPos = { x, y };
    previewRack(newRackRequest(x, y), { source: 'add' });
  }

  function placeNewRack() {
    const preview = state.preview;
    if (!preview || preview.source !== 'add') return;
    if (preview.isValid === false) {
      showMessage(`Không đặt được: ${preview.reason}`, true);
      return;
    }
    addRack(preview.request);
    lastAddPos = null;
    clearPreview();
    setTool('select');
  }

  // ---------- đặt dãy kệ ----------
  function updateArrayPreview(p) {
    const x = snap(p.x);
    const y = snap(p.y);
    if (lastAddPos && lastAddPos.x === x && lastAddPos.y === y) return;
    lastAddPos = { x, y };
    previewRackArray(arrayRequests(x, y));
  }

  function placeArray() {
    const a = state.arrayPreview;
    if (!a) return;
    if (a.results === null) {
      showMessage('Đang kiểm tra dãy kệ, chờ một chút rồi bấm lại.', true);
      return;
    }
    if (a.reason) {
      showMessage(`Không đặt được dãy: ${a.reason}`, true);
      return;
    }
    // Dùng kích thước chính thức server vừa trả cho từng kệ
    addRacks(a.requests.map((r, i) => ({
      ...r, footprintWidth: a.results[i].footprintWidth, footprintDepth: a.results[i].footprintDepth,
    })));
    lastAddPos = null;
    clearPreview();
    setTool('select');
  }

  // ---------- đặt cột ----------
  function placeColumn(p) {
    addObstacle({ kind: 'Column', x: snap(p.x), y: snap(p.y), width: COLUMN_SIZE, depth: COLUMN_SIZE });
    hoverColumn = null;
    setTool('select');
  }

  // ================= PHÍM =================
  window.addEventListener('keydown', function (e) {
    // Đang gõ trong ô nhập (bảng bên) thì phím R là chữ R, không phải lệnh xoay
    if (e.target.closest?.('input, select, textarea')) return;

    // Delete (hoặc Backspace trên máy Mac): xoá kệ / vật cản đang chọn, có hỏi lại
    if ((e.key === 'Delete' || e.key === 'Backspace') && state.tool === 'select' && !gesture) {
      if (selectedRack()) removeRack(selectedRack());
      else if (selectedObstacle()) removeObstacle(selectedObstacle());
      return;
    }

    if (e.key === 'Escape') {
      gesture = null;
      lastAddPos = null;
      hoverColumn = null;
      clearPreview();
      setTool('select');
      return;
    }
    if (e.key !== 'r' && e.key !== 'R') return;
    e.preventDefault();

    if (gesture?.kind === 'move') {
      // Xoay bóng đang kéo; đổi vào bản nháp khi thả chuột
      gesture.rotation = gesture.rotation === 90 ? 0 : 90;
      const x = gesture.lastX ?? gesture.rack.x;
      const y = gesture.lastY ?? gesture.rack.y;
      gesture.lastX = null;
      updateMovePreview(x, y);
    } else if (state.tool === 'add-rack') {
      state.newRack = { ...state.newRack, rotation: state.newRack.rotation === 90 ? 0 : 90 };
      if (lastAddPos) {
        const { x, y } = lastAddPos;
        lastAddPos = null;
        updateNewRackPreview({ x, y });
      }
    } else if (state.tool === 'add-array') {
      state.newRack = { ...state.newRack, rotation: state.newRack.rotation === 90 ? 0 : 90 };
      if (lastAddPos) {
        const { x, y } = lastAddPos;
        lastAddPos = null;
        updateArrayPreview({ x, y });
      }
    } else if (state.tool === 'select' && selectedRack()) {
      rotateRack(selectedRack());   // xoay kệ thật tại chỗ (trong bản nháp)
    }
  });

  function reportCursor(p) {
    const room = state.room;
    if (!room || !onCursor) return;
    const isInside = p.x >= 0 && p.y >= 0 && p.x <= room.width && p.y <= room.depth;
    onCursor(isInside ? { x: p.x, y: p.y, rack: rackAt(p) } : null);
  }

  // ================= THANH CÔNG CỤ =================
  for (const button of toolButtons) {
    button.addEventListener('click', function () {
      clearPreview();
      setTool(button.dataset.tool);
    });
  }
  document.getElementById('btnFit').addEventListener('click', fit);
  document.getElementById('btnZoomIn').addEventListener('click', () => zoomAt(1.25, size.width / 2, size.height / 2));
  document.getElementById('btnZoomOut').addEventListener('click', () => zoomAt(0.8, size.width / 2, size.height / 2));
  gridCheckbox.addEventListener('change', () => setShowGrid(gridCheckbox.checked));

  // Cập nhật thanh công cụ theo dữ liệu hiện tại
  function syncToolbar() {
    toolButtons.forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.tool === state.tool)));
    gridCheckbox.checked = state.showGrid;
    gridLabel.textContent = state.room ? `${formatMeters(state.room.gridSize)} m` : '';
    toolHint.textContent = HINTS[state.tool] ?? '';
    canvas.classList.toggle('placing', state.tool !== 'select');
  }

  // ================= KÍCH THƯỚC, DỮ LIỆU ĐỔI =================
  function resize() {
    const dpr = window.devicePixelRatio || 1;
    size = { width: container.clientWidth, height: container.clientHeight };
    canvas.width = Math.round(size.width * dpr);
    canvas.height = Math.round(size.height * dpr);
    canvas.style.width = `${size.width}px`;
    canvas.style.height = `${size.height}px`;
    fitIfNewRoom();
    draw();
  }

  // Lần đầu có dữ liệu phòng (hoặc đổi kích thước phòng) thì "vừa khung"
  function fitIfNewRoom() {
    const room = state.room;
    const key = room ? `${room.id}:${room.width}x${room.depth}` : null;
    if (key && key !== fittedRoomKey && size.width > 0) {
      fittedRoomKey = key;
      fit();
    }
  }

  new ResizeObserver(resize).observe(container);

  // Trang gọi hàm này mỗi khi dữ liệu đổi
  function redraw() {
    if (state.tool !== lastTool) {   // vừa đổi công cụ: quên bóng mờ của công cụ cũ
      lastTool = state.tool;
      lastAddPos = null;
      hoverColumn = null;
    }
    syncToolbar();
    fitIfNewRoom();
    draw();
  }

  syncToolbar();
  return { redraw, fit };
}

function clamp(v, min, max) {
  return Math.min(max, Math.max(min, v));
}

// Màu lấy từ biến CSS --c2d-* trong base.css, để đổi màu chỉ cần sửa CSS
function readColors() {
  const css = getComputedStyle(document.documentElement);
  const v = (name) => css.getPropertyValue(name).trim();
  return {
    outside: v('--c2d-outside'),
    floor: v('--c2d-floor'),
    grid: v('--c2d-grid'),
    gridMajor: v('--c2d-grid-major'),
    roomEdge: v('--c2d-room-edge'),
    wall: v('--c2d-wall'),
    column: v('--c2d-column'),
    rack: v('--c2d-rack'),
    rackEdge: v('--c2d-rack-edge'),
    rackBay: v('--c2d-rack-bay'),
    rackSelected: v('--c2d-rack-selected'),
    label: v('--c2d-label'),
    ghostOk: v('--c2d-ghost-ok'),
    ghostOkEdge: v('--c2d-ghost-ok-edge'),
    ghostBad: v('--c2d-ghost-bad'),
    ghostBadEdge: v('--c2d-ghost-bad-edge'),
    ghostPending: v('--c2d-ghost-pending'),
    ghostPendingEdge: v('--c2d-ghost-pending-edge'),
  };
}
