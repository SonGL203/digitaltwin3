import { isTall } from '../robot/costmap.js';
import { explore, findStart, floorCells } from '../robot/explorer.js';
import { FREE, OCCUPIED, cellCenter, knownRatio } from '../robot/gridMap.js';
import { mapToLayout } from '../robot/mapToLayout.js';
import { createWorld } from '../robot/world.js';
import { createRoomFromScan } from '../state/actions.js';
import { showMessage } from '../state/store.js';
import { formatMeters } from './format.js';

// Màn "Robot quét" của kho đang mở: thả robot giả lập vào phòng ĐÃ LƯU, robot tự đi quét, màn hình dựng dần
// theo đúng thứ tự: robot thấy gì (ô nhỏ góc phải) → bản đồ lưới → mặt bằng nhận ra (kệ, cột, tường).
// Quét xong có thể tạo kho mới từ bản quét (bản nháp, người dùng kiểm tra rồi Lưu nháp).
//
// Toàn bộ logic nằm trong src/robot/ (hàm thuần, có test); file này chỉ gọi explore() theo nhịp và vẽ.
// "Thế giới thật" (nét đứt) chỉ để người xem so sánh; robot không đọc nó.

const TICKS_PER_SECOND = 10;     // 1 nhịp = robot đi 1 ô 0,1 m → ×1 là 1 m/s
const CONVERT_EVERY = 10;        // cứ 10 nhịp nhận dạng lại mặt bằng một lần (vẽ song song lúc đang quét)
const MAX_TICKS_PER_FRAME = 400; // ×8 trên máy chậm: không để một khung hình chạy quá lâu
const ROBOT_RADIUS = 0.35;

const COLORS = {
  outside: '#e9ecf1',
  unknown: '#cfd4dd',
  free: '#ffffff',
  tall: '#3b4252',
  low: '#f59e0b',
  truth: '#8b93a5',
  rack: '#2563eb',
  rackFill: 'rgba(37, 99, 235, 0.12)',
  obstacle: '#c2410c',
  path: '#1f8a4c',
  target: '#7c3aed',
  robot: '#2563eb',
  ray: 'rgba(37, 99, 235, 0.15)',
};

export function createRobotScan(root) {
  root.innerHTML = `
    <div class="robot-toolbar">
      <button type="button" data-act="play">Bắt đầu quét</button>
      <button type="button" data-act="restart">Chạy lại</button>
      <span class="robot-speed">Tốc độ
        <button type="button" data-speed="1">×1</button>
        <button type="button" data-speed="2" aria-pressed="true">×2</button>
        <button type="button" data-speed="4">×4</button>
        <button type="button" data-speed="8">×8</button>
      </span>
      <label><input type="checkbox" data-show="truth" checked> Thế giới thật</label>
      <label><input type="checkbox" data-show="rays" checked> Tia quét</label>
      <label><input type="checkbox" data-show="layout" checked> Mặt bằng nhận ra</label>
    </div>
    <div class="robot-stats" data-stats></div>
    <div class="canvas-wrap robot-canvas">
      <canvas data-main></canvas>
      <div class="robot-inset"><span>Robot thấy</span><canvas data-inset width="180" height="180"></canvas></div>
    </div>
    <div class="robot-result" data-result></div>
  `;
  const canvas = root.querySelector('[data-main]');
  const ctx = canvas.getContext('2d');
  const inset = root.querySelector('[data-inset]');
  const ictx = inset.getContext('2d');
  const wrap = canvas.parentElement;
  const statsEl = root.querySelector('[data-stats]');
  const resultEl = root.querySelector('[data-result]');
  const playBtn = root.querySelector('[data-act="play"]');
  const show = { truth: true, rays: true, layout: true };

  let layout = null;
  let layoutKey = null;
  let world = null;
  let run = null;           // generator explore()
  let step = null;          // nhịp mới nhất
  let pose = null;
  let prevPose = null;
  let distance = 0;
  let floor = null;         // ô sàn thật (chỉ để báo % cho người xem)
  let scanned = null;       // mặt bằng nhận ra gần nhất
  let mapCanvas = null;     // ảnh bản đồ lưới, mỗi ô 1 điểm ảnh, chỉ tô lại ô vừa đổi
  let running = false;
  let finished = false;
  let speed = 2;
  let pending = 0;          // phần nhịp lẻ còn nợ giữa các khung hình
  let active = false;
  let lastFrame = 0;
  let error = null;
  let creating = false;

  // ---------- điều khiển ----------

  root.addEventListener('click', async (e) => {
    const act = e.target.closest('[data-act]')?.dataset.act;
    const sp = e.target.closest('[data-speed]')?.dataset.speed;
    if (sp) {
      speed = Number(sp);
      root.querySelectorAll('[data-speed]').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.speed === sp)));
    } else if (act === 'play') {
      if (finished) return;
      running = !running;
    } else if (act === 'restart') {
      reset();
      running = true;
    } else if (act === 'create' && scanned && !creating) {
      creating = true;
      renderResult();
      try {
        await createRoomFromScan(scanned, layout);
      } catch (err) {
        showMessage(err.message, 'error');
      } finally {
        creating = false;
        renderResult();
      }
    }
    renderControls();
  });
  root.addEventListener('change', (e) => {
    const key = e.target.dataset.show;
    if (key) show[key] = e.target.checked;
  });

  new ResizeObserver(() => resize()).observe(wrap);

  function resize() {
    const dpr = window.devicePixelRatio || 1;
    canvas.width = Math.max(1, Math.round(wrap.clientWidth * dpr));
    canvas.height = Math.max(1, Math.round(wrap.clientHeight * dpr));
  }

  // ---------- chạy ----------

  function reset() {
    running = false;
    finished = false;
    step = null;
    scanned = null;
    distance = 0;
    pending = 0;
    error = null;
    mapCanvas = null;
    try {
      world = createWorld(layout);
      const start = findStart(world, ROBOT_RADIUS);
      if (!start) throw new Error('Phòng không còn chỗ trống nào đủ rộng để đặt robot.');
      pose = start;
      prevPose = start;
      run = explore(world, start, { robotRadius: ROBOT_RADIUS });
      floor = null;
    } catch (err) {
      error = err.message;
      run = null;
    }
    renderControls();
    renderResult();
  }

  function tick() {
    const next = run.next();
    if (next.done) {
      finish();
      return;
    }
    step = next.value;
    prevPose = pose;
    pose = step.pose;
    distance += Math.hypot(pose.x - prevPose.x, pose.y - prevPose.y);
    floor ??= floorCells(world, step.map);
    paintCells(step.changed);
    if (step.tick % CONVERT_EVERY === 0) scanned = mapToLayout(step.map);
    if (step.done) finish();
  }

  function finish() {
    running = false;
    finished = true;
    if (step) scanned = mapToLayout(step.map);
    prevPose = pose;
    renderControls();
    renderResult();
  }

  function frame(time) {
    if (!active) return;
    const dt = lastFrame ? Math.min(0.1, (time - lastFrame) / 1000) : 0;
    lastFrame = time;
    if (running && run) {
      pending += dt * TICKS_PER_SECOND * speed;
      let n = 0;
      while (pending >= 1 && running && n < MAX_TICKS_PER_FRAME) {
        tick();
        pending -= 1;
        n++;
      }
      if (n === MAX_TICKS_PER_FRAME) pending = 0;
    }
    draw(running ? Math.min(1, pending) : 1);
    requestAnimationFrame(frame);
  }

  // ---------- vẽ ----------

  /** Tô lại các ô vừa đổi lên ảnh bản đồ (1 ô = 1 điểm ảnh, phóng to khi vẽ ra màn hình). */
  function paintCells(cells) {
    const map = step.map;
    if (!mapCanvas) {
      mapCanvas = document.createElement('canvas');
      mapCanvas.width = map.cols;
      mapCanvas.height = map.rows;
      const m = mapCanvas.getContext('2d');
      m.fillStyle = COLORS.unknown;
      m.fillRect(0, 0, map.cols, map.rows);
    }
    const m = mapCanvas.getContext('2d');
    for (const i of cells) {
      const s = map.state[i];
      m.fillStyle = s === FREE ? COLORS.free : s === OCCUPIED ? (isTall(step.costmap, map.height[i]) ? COLORS.tall : COLORS.low) : COLORS.unknown;
      m.fillRect(i % map.cols, Math.floor(i / map.cols), 1, 1);
    }
  }

  function draw(frac) {
    const dpr = window.devicePixelRatio || 1;
    const w = canvas.width / dpr;
    const h = canvas.height / dpr;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.fillStyle = COLORS.outside;
    ctx.fillRect(0, 0, w, h);
    if (!layout) return;

    const pad = 24;
    // Màn đủ rộng thì chừa lề phải cho ô "Robot thấy" (180 px) để nó không che bản đồ.
    const padRight = w > 640 ? inset.width + 16 : pad;
    const s = Math.max(1, Math.min((w - pad - padRight) / layout.width, (h - 2 * pad) / layout.depth));
    const ox = pad + (w - pad - padRight - layout.width * s) / 2;
    const oy = (h - layout.depth * s) / 2;
    const X = (x) => ox + x * s;
    const Y = (y) => oy + y * s;

    if (mapCanvas) {
      ctx.imageSmoothingEnabled = false;
      const cs = step.map.cellSize;
      ctx.drawImage(mapCanvas, X(0), Y(0), step.map.cols * cs * s, step.map.rows * cs * s);
    } else {
      ctx.fillStyle = COLORS.unknown;
      ctx.fillRect(X(0), Y(0), layout.width * s, layout.depth * s);
    }
    ctx.strokeStyle = COLORS.tall;
    ctx.lineWidth = 2;
    ctx.strokeRect(X(0), Y(0), layout.width * s, layout.depth * s);

    ctx.font = '12px system-ui, sans-serif';
    if (show.truth && world) {
      ctx.setLineDash([4, 3]);
      ctx.lineWidth = 1;
      ctx.strokeStyle = COLORS.truth;
      for (const b of world.boxes) ctx.strokeRect(X(b.x), Y(b.y), b.width * s, b.depth * s);
      ctx.setLineDash([]);
    }

    if (show.layout && scanned) {
      ctx.lineWidth = 2;
      for (const r of scanned.racks) {
        ctx.fillStyle = COLORS.rackFill;
        ctx.fillRect(X(r.x), Y(r.y), r.footprintWidth * s, r.footprintDepth * s);
        ctx.strokeStyle = COLORS.rack;
        ctx.strokeRect(X(r.x), Y(r.y), r.footprintWidth * s, r.footprintDepth * s);
        ctx.fillStyle = COLORS.rack;
        ctx.fillText(r.code, X(r.x) + 4, Y(r.y) + 14);
      }
      ctx.strokeStyle = COLORS.obstacle;
      for (const o of scanned.obstacles) ctx.strokeRect(X(o.x), Y(o.y), o.width * s, o.depth * s);
    }

    if (step?.path && step.pathIndex < step.path.length) {
      ctx.strokeStyle = COLORS.path;
      ctx.lineWidth = 2;
      ctx.setLineDash([6, 4]);
      ctx.beginPath();
      ctx.moveTo(X(pose.x), Y(pose.y));
      for (let k = step.pathIndex; k < step.path.length; k++) {
        const c = cellCenter(step.map, step.path[k]);
        ctx.lineTo(X(c.x), Y(c.y));
      }
      ctx.stroke();
      ctx.setLineDash([]);
    }
    if (step && step.target >= 0) {
      const t = cellCenter(step.map, step.target);
      ctx.strokeStyle = COLORS.target;
      ctx.lineWidth = 2;
      ctx.beginPath();
      ctx.moveTo(X(t.x) - 6, Y(t.y) - 6); ctx.lineTo(X(t.x) + 6, Y(t.y) + 6);
      ctx.moveTo(X(t.x) + 6, Y(t.y) - 6); ctx.lineTo(X(t.x) - 6, Y(t.y) + 6);
      ctx.stroke();
    }

    // Robot vẽ ở vị trí nội suy giữa hai nhịp cho mượt (logic vẫn đi từng ô).
    const rx = prevPose ? prevPose.x + (pose.x - prevPose.x) * frac : pose?.x;
    const ry = prevPose ? prevPose.y + (pose.y - prevPose.y) * frac : pose?.y;
    if (show.rays && step?.scan) {
      ctx.strokeStyle = COLORS.ray;
      ctx.lineWidth = 1;
      ctx.beginPath();
      for (const r of step.scan.readings) {
        ctx.moveTo(X(step.scan.origin.x), Y(step.scan.origin.y));
        ctx.lineTo(X(step.scan.origin.x + Math.cos(r.angle) * r.distance), Y(step.scan.origin.y + Math.sin(r.angle) * r.distance));
      }
      ctx.stroke();
    }
    if (rx != null) {
      ctx.fillStyle = COLORS.robot;
      ctx.beginPath();
      ctx.arc(X(rx), Y(ry), ROBOT_RADIUS * s, 0, Math.PI * 2);
      ctx.fill();
    }

    if (error) {
      ctx.fillStyle = '#c0392b';
      ctx.font = '14px system-ui, sans-serif';
      ctx.fillText(error, X(0) + 8, Y(0) + 20);
    }
    drawInset();
    renderStats();
  }

  /** "Robot thấy": đúng những gì cảm biến trả về ở nhịp này, đặt robot ở giữa, chưa ghép thành bản đồ. */
  function drawInset() {
    const size = inset.width;
    const c = size / 2;
    ictx.fillStyle = '#111827';
    ictx.fillRect(0, 0, size, size);
    const scan = step?.scan;
    if (!scan) return;
    const k = (c - 6) / scan.range;
    ictx.strokeStyle = 'rgba(255,255,255,0.15)';
    ictx.beginPath();
    ictx.arc(c, c, scan.range * k, 0, Math.PI * 2);
    ictx.stroke();
    for (const r of scan.readings) {
      const dx = Math.cos(r.angle);
      const dy = Math.sin(r.angle);
      for (const low of r.lows) {
        ictx.fillStyle = COLORS.low;
        ictx.fillRect(c + dx * low.from * k - 1.5, c + dy * low.from * k - 1.5, 3, 3);
      }
      if (!r.hit) continue;
      ictx.fillStyle = r.boundary ? '#9ca3af' : '#60a5fa';
      ictx.fillRect(c + dx * r.distance * k - 1.5, c + dy * r.distance * k - 1.5, 3, 3);
    }
    ictx.fillStyle = COLORS.robot;
    ictx.beginPath();
    ictx.arc(c, c, 4, 0, Math.PI * 2);
    ictx.fill();
  }

  function renderStats() {
    const cover = step && floor ? knownRatio(step.map, floor) : 0;
    const counts = scanned
      ? `${scanned.racks.length} kệ, ${scanned.obstacles.filter((o) => o.kind === 'Column').length} cột, ${scanned.obstacles.filter((o) => o.kind === 'Wall').length} tường`
      : 'chưa có';
    const text = `Đã quét ${Math.round(cover * 100)}% sàn · Đi ${formatMeters(distance)} m · ${step ? step.tick + 1 : 0} nhịp · Nhận ra: ${counts}`;
    if (statsEl.textContent !== text) statsEl.textContent = text;
  }

  function renderControls() {
    playBtn.textContent = finished ? 'Đã quét xong' : running ? 'Tạm dừng' : step ? 'Chạy tiếp' : 'Bắt đầu quét';
    playBtn.disabled = finished || !run;
  }

  function renderResult() {
    if (!finished || !scanned) {
      resultEl.innerHTML = '<p class="hint">Robot chưa biết gì về kho: nó chỉ thấy qua cảm biến tầm 6 m, tự đi tới vùng chưa biết gần nhất (dấu ×) cho tới khi hết chỗ để nhìn.</p>';
      return;
    }
    const why = step?.reason === 'timeout' ? ' (dừng vì quá lâu, có thể chưa quét hết)' : '';
    resultEl.innerHTML = `
      <p>Quét xong${why}: nhận ra <strong>${scanned.racks.length} kệ</strong>,
        ${scanned.obstacles.filter((o) => o.kind === 'Column').length} cột, ${scanned.obstacles.filter((o) => o.kind === 'Wall').length} tường.
        Số tầng và số ô của kệ là <strong>đoán</strong> từ chiều cao đo được: kiểm tra lại trước khi lưu.</p>
      <button type="button" data-act="create" ${creating || scanned.racks.length + scanned.obstacles.length === 0 ? 'disabled' : ''}>
        ${creating ? 'Đang tạo…' : 'Tạo kho mới từ bản quét'}</button>
    `;
  }

  return {
    /** Mở màn: layout là phòng ĐÃ LƯU đang mở. Phòng hay phiên bản đổi thì bắt đầu lại từ đầu. */
    show(nextLayout) {
      const key = nextLayout ? `${nextLayout.id}:${nextLayout.version}` : null;
      if (key !== layoutKey) {
        layoutKey = key;
        layout = nextLayout;
        if (layout) reset();
      }
      if (!active) {
        active = true;
        lastFrame = 0;
        resize();
        requestAnimationFrame(frame);
      }
    },
    /** Rời màn: dừng vòng vẽ (không tốn CPU khi khung bị ẩn), giữ nguyên tiến độ quét. */
    hide() {
      active = false;
      running = false;
      renderControls();
    },
  };
}
