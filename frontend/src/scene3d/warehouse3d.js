import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { bayFootprint, rackPoint } from './rackGeometry.js';
import { slotWorldPosition } from './slotPosition.js';

// Cảnh 3D của một phòng: sàn, tường, cột, kệ và các khung (tô màu theo type pallet đang nằm trong khung).
// Chỉ vẽ dữ liệu server trả về (phòng đã lưu + danh sách khung); không tự chọn khung, không tự kiểm tra luật.
// 1 đơn vị three.js = 1 m. Trục: x = X mặt bằng, z = Y mặt bằng, y = chiều cao.

const WALL_HEIGHT = 3;
const POST = 0.06;     // cạnh thanh đứng của kệ
const BOARD = 0.04;    // độ dày tấm ngang của kệ
const SLOT_SCALE = 0.9;   // khung vẽ nhỏ hơn ô một chút để thấy khe giữa các khung

export function createWarehouse3d(container, { getReplay = () => null } = {}) {
  const scene = new THREE.Scene();
  scene.background = new THREE.Color(0xeef1f5);

  const camera = new THREE.PerspectiveCamera(50, 1, 0.1, 500);
  const renderer = new THREE.WebGLRenderer({ antialias: true });
  renderer.setPixelRatio(window.devicePixelRatio);
  container.appendChild(renderer.domElement);

  const controls = new OrbitControls(camera, renderer.domElement);
  controls.maxPolarAngle = Math.PI / 2 - 0.02;   // không cho chui xuống dưới sàn

  scene.add(new THREE.HemisphereLight(0xffffff, 0x8899aa, 1.2));
  const sun = new THREE.DirectionalLight(0xffffff, 1.5);
  sun.position.set(10, 20, 8);
  scene.add(sun);

  const content = new THREE.Group();   // mọi thứ thuộc phòng; dựng lại mỗi lần dữ liệu đổi
  scene.add(content);

  const overlay = document.createElement('div');
  overlay.className = 'view3d-overlay';
  container.appendChild(overlay);

  // Nút tốc độ hiệu ứng pallet bay (góc dưới trái).
  const speedBar = document.createElement('div');
  speedBar.className = 'view3d-speed';
  speedBar.innerHTML = `
    <span>Tốc độ bay</span>
    <button type="button" data-speed="1">×1</button>
    <button type="button" data-speed="2">×2</button>
    <button type="button" data-speed="4">×4</button>
    <button type="button" data-skip>Bỏ qua</button>
    <button type="button" data-replay title="Cho pallet của lần xếp gần nhất bay vào lại">Phát lại</button>
  `;
  container.appendChild(speedBar);

  let pickables = [];       // { mesh, slot, rack }
  let flights = [];         // pallet đang bay: { flyer, target, finalColor, finalOpacity, from, to, delay, elapsed }
  let speed = 1;
  const clock = new THREE.Clock();
  let selectedMesh = null;
  let shownKey = null;      // phòng + kích thước đang hiện: đổi thì mới đưa camera về giữa phòng
  let current = { layout: null, slots: [] };

  // ---------- dựng ----------

  function clearContent() {
    content.traverse((o) => {
      o.geometry?.dispose();
      if (o.material) [o.material].flat().forEach((m) => m.dispose());
    });
    content.clear();
    pickables = [];
    flights = [];   // dựng lại cảnh thì các pallet đang bay không còn tồn tại
    selectedMesh = null;
  }

  function box(width, height, depth, color, cx, cy, cz, opacity = 1) {
    const mesh = new THREE.Mesh(
      new THREE.BoxGeometry(width, height, depth),
      new THREE.MeshStandardMaterial({ color, transparent: opacity < 1, opacity }),
    );
    mesh.position.set(cx, cy, cz);
    content.add(mesh);
    return mesh;
  }

  function buildRoom(layout) {
    const { width, depth } = layout;

    const floor = new THREE.Mesh(
      new THREE.PlaneGeometry(width, depth),
      new THREE.MeshStandardMaterial({ color: 0xffffff }),
    );
    floor.rotation.x = -Math.PI / 2;
    floor.position.set(width / 2, 0, depth / 2);
    content.add(floor);

    // Vạch mỗi 1 m trên sàn để ước lượng khoảng cách (không liên quan GridSize dùng để snap ở editor).
    const pts = [];
    for (let x = 0; x <= width; x++) pts.push(x, 0.002, 0, x, 0.002, depth);
    for (let z = 0; z <= depth; z++) pts.push(0, 0.002, z, width, 0.002, z);
    const grid = new THREE.BufferGeometry();
    grid.setAttribute('position', new THREE.Float32BufferAttribute(pts, 3));
    content.add(new THREE.LineSegments(grid, new THREE.LineBasicMaterial({ color: 0xd0d5dd })));

    // Mép phòng chính là tường bao: vẽ một viền thấp.
    const edge = [[0, 0], [width, 0], [width, depth], [0, depth]]
      .flatMap(([x, z]) => [x, 0.01, z]);
    const edgeGeo = new THREE.BufferGeometry();
    edgeGeo.setAttribute('position', new THREE.Float32BufferAttribute(edge, 3));
    content.add(new THREE.LineLoop(edgeGeo, new THREE.LineBasicMaterial({ color: 0x1d2433 })));

    for (const o of layout.obstacles) {
      box(o.width, WALL_HEIGHT, o.depth, o.kind === 'Wall' ? 0x3b4252 : 0x8b93a5,
        o.x + o.width / 2, WALL_HEIGHT / 2, o.y + o.depth / 2);
    }
  }

  function buildRack(rack) {
    const length = rack.bays * rack.bayWidth;
    const color = 0x2563eb;

    // Thanh đứng ở mỗi ranh giới ô, phía trước và phía sau; cao đến tấm ngang trên cùng.
    const height = rack.levels * rack.levelHeight;
    for (let i = 0; i <= rack.bays; i++) {
      for (const v of [0, rack.depth]) {
        const p = rackPoint(rack, i * rack.bayWidth, v);
        box(POST, height, POST, color, p.x, height / 2, p.z);
      }
    }

    // Tấm ngang dưới mỗi tầng và tấm trên cùng (đáy khung nóc nếu có).
    const a = rackPoint(rack, 0, 0);
    const b = rackPoint(rack, length, rack.depth);
    for (let level = 0; level <= rack.levels; level++) {
      box(Math.abs(b.x - a.x), BOARD, Math.abs(b.z - a.z), 0x9db8f0,
        (a.x + b.x) / 2, level * rack.levelHeight, (a.z + b.z) / 2);
    }
  }

  function buildSlots(layout, slots) {
    const racks = new Map(layout.racks.map((r) => [r.id, r]));
    let missingPosition = false;

    for (const slot of slots) {
      const rack = racks.get(slot.rackId);
      const center = rack && slotWorldPosition(rack, slot);
      if (!center) {
        missingPosition = true;
        continue;
      }

      const { sizeX, sizeZ } = bayFootprint(rack);
      const occupied = slot.palletId != null;
      const mesh = new THREE.Mesh(
        new THREE.BoxGeometry(sizeX * SLOT_SCALE, rack.levelHeight * SLOT_SCALE, sizeZ * SLOT_SCALE),
        new THREE.MeshStandardMaterial({
          color: occupied ? new THREE.Color(slot.color) : 0x2563eb,
          transparent: true,
          opacity: occupied ? 0.92 : 0.07,
        }),
      );
      mesh.position.set(center.x, center.y, center.z);
      mesh.add(new THREE.LineSegments(
        new THREE.EdgesGeometry(mesh.geometry),
        new THREE.LineBasicMaterial({ color: occupied ? 0x1d2433 : 0x9db8f0 }),
      ));
      content.add(mesh);
      pickables.push({ mesh, slot, rack });
    }

    return missingPosition;
  }

  function fitCamera(layout) {
    const { width, depth } = layout;
    const size = Math.max(width, depth);
    controls.target.set(width / 2, 0, depth / 2);
    camera.position.set(width / 2 + size * 0.7, size * 0.8, depth / 2 + size * 0.9);
    camera.far = size * 10;
    camera.updateProjectionMatrix();
    controls.update();
  }

  // ---------- cập nhật từ dữ liệu ----------

  /** Dựng lại cảnh từ phòng đã lưu và danh sách khung. layout null = chưa mở phòng nào. */
  function update(layout, slots) {
    current = { layout, slots };
    clearContent();

    if (!layout) {
      shownKey = null;
      showOverlay('<p>Chưa mở kho nào. Chọn một kho ở danh sách Kho hàng.</p>');
      return;
    }

    buildRoom(layout);
    layout.racks.forEach(buildRack);
    const missing = buildSlots(layout, slots);

    const key = `${layout.id}:${layout.width}x${layout.depth}`;
    if (key !== shownKey) {
      fitCamera(layout);
      shownKey = key;
    }

    const filled = slots.filter((s) => s.palletId != null).length;
    const types = new Map(slots.filter((s) => s.typeCode).map((s) => [s.typeCode, s.color]));
    const legend = [...types].map(([code, color]) =>
      `<span class="legend-item"><i style="background:${color}"></i>Type ${code}</span>`).join('');
    showOverlay(`
      <strong>${escapeHtml(layout.name)}</strong>
      <p>${filled} / ${slots.length} khung có hàng</p>
      ${legend ? `<p class="legend">${legend}</p>` : ''}
      ${missing && slots.length ? '<p class="warn">slotWorldPosition chưa viết: chưa vẽ được khung.</p>' : ''}
      <p class="hint">Kéo để xoay, cuộn để phóng to, bấm vào khung để xem.</p>
      <div data-role="slot-info"></div>
    `);
  }

  function showOverlay(html) {
    overlay.innerHTML = html;
  }

  function showSlotInfo(picked) {
    const holder = overlay.querySelector('[data-role="slot-info"]');
    if (!holder) return;
    if (!picked) {
      holder.innerHTML = '';
      return;
    }
    const { slot } = picked;
    holder.innerHTML = `
      <p><strong>${slot.code}</strong>${slot.isTop ? ' (nóc)' : ''}</p>
      <p>${slot.palletId != null
        ? `Pallet ${slot.palletCode} · type ${slot.typeCode}`
        : 'Khung trống'}</p>
    `;
  }

  // ---------- bấm chọn khung ----------
  const raycaster = new THREE.Raycaster();
  let downAt = null;

  renderer.domElement.addEventListener('pointerdown', (e) => { downAt = { x: e.clientX, y: e.clientY }; });
  renderer.domElement.addEventListener('pointerup', (e) => {
    // Kéo để xoay camera thì không tính là bấm.
    if (!downAt || Math.hypot(e.clientX - downAt.x, e.clientY - downAt.y) > 4) return;
    const rect = renderer.domElement.getBoundingClientRect();
    const pointer = new THREE.Vector2(
      ((e.clientX - rect.left) / rect.width) * 2 - 1,
      -((e.clientY - rect.top) / rect.height) * 2 + 1,
    );
    raycaster.setFromCamera(pointer, camera);
    const hit = raycaster.intersectObjects(pickables.map((p) => p.mesh), false)[0];
    const picked = hit ? pickables.find((p) => p.mesh === hit.object) : null;

    if (selectedMesh) selectedMesh.material.emissive.setHex(0x000000);
    selectedMesh = picked?.mesh ?? null;
    if (selectedMesh) selectedMesh.material.emissive.setHex(0x553300);
    showSlotInfo(picked);
  });

  // ---------- khung hình ----------
  function resize() {
    const { clientWidth: w, clientHeight: h } = container;
    if (!w || !h) return;   // đang ẩn (bước khác): chưa có kích thước
    renderer.setSize(w, h);
    camera.aspect = w / h;
    camera.updateProjectionMatrix();
  }
  new ResizeObserver(resize).observe(container);
  resize();

  // ---------- pallet bay vào khung ----------
  const START_DELAY = 0.35;   // giây giữa hai pallet liên tiếp (ở tốc độ ×1)
  const FLIGHT_TIME = 1.2;    // giây một pallet bay (ở tốc độ ×1)
  const ARC_HEIGHT = 2.0;     // đỉnh đường bay cao hơn đường thẳng bao nhiêu mét

  /**
   * Cho pallet bay vào khung theo ĐÚNG thứ tự server đã xếp (placements). Chỉ gọi sau khi server đã trả kết quả:
   * vị trí cuối của mỗi pallet là tâm khung (cùng slotWorldPosition), không có toạ độ riêng của hiệu ứng.
   * Khung đích trông như trống cho tới khi pallet bay tới thì mới hiện màu.
   */
  function play(placements) {
    finishFlights();
    const { layout } = current;
    if (!layout) return;
    const bySlotCode = new Map(pickables.map((p) => [p.slot.code, p]));
    const from = new THREE.Vector3(-2, 0.8, layout.depth / 2);   // cửa kho phía bên trái

    placements.forEach((placement, i) => {
      const target = bySlotCode.get(placement.slotCode);
      if (!target) return;
      const material = target.mesh.material;
      const flight = {
        target, from, to: target.mesh.position.clone(),
        finalColor: material.color.clone(), finalOpacity: material.opacity,
        flyer: new THREE.Mesh(
          target.mesh.geometry.clone(),
          new THREE.MeshStandardMaterial({ color: new THREE.Color(placement.color) }),
        ),
        delay: i * START_DELAY, elapsed: 0,
      };
      material.color.setHex(0x2563eb);
      material.opacity = 0.07;
      flight.flyer.visible = false;
      content.add(flight.flyer);
      flights.push(flight);
    });
  }

  function landed(flight) {
    const material = flight.target.mesh.material;
    material.color.copy(flight.finalColor);
    material.opacity = flight.finalOpacity;
    content.remove(flight.flyer);
    flight.flyer.geometry.dispose();
    flight.flyer.material.dispose();
  }

  /** Cho mọi pallet đang bay hạ cánh ngay (nút Bỏ qua, hoặc trước khi bắt đầu lượt bay mới). */
  function finishFlights() {
    flights.forEach(landed);
    flights = [];
  }

  function stepFlights(seconds) {
    const remaining = [];
    for (const flight of flights) {
      flight.elapsed += seconds;
      const local = flight.elapsed - flight.delay;
      if (local >= FLIGHT_TIME) {
        landed(flight);
        continue;
      }
      if (local > 0) {
        const t = local / FLIGHT_TIME;
        const eased = t * t * (3 - 2 * t);   // vào ra mượt
        flight.flyer.visible = true;
        flight.flyer.position.lerpVectors(flight.from, flight.to, eased);
        flight.flyer.position.y += Math.sin(Math.PI * t) * ARC_HEIGHT;
      }
      remaining.push(flight);
    }
    flights = remaining;
  }

  speedBar.addEventListener('click', (e) => {
    const button = e.target.closest('button');
    if (!button) return;
    if ('skip' in button.dataset) finishFlights();
    if ('replay' in button.dataset) {
      const placements = getReplay();
      if (placements?.length) play(placements);
    }
    if (button.dataset.speed) speed = Number(button.dataset.speed);
    speedBar.querySelectorAll('[data-speed]').forEach((b) =>
      b.setAttribute('aria-pressed', String(Number(b.dataset.speed) === speed)));
  });
  speedBar.querySelector('[data-speed="1"]').setAttribute('aria-pressed', 'true');

  renderer.setAnimationLoop(() => {
    stepFlights(clock.getDelta() * speed);
    controls.update();
    renderer.render(scene, camera);
  });

  return { update, play, get current() { return current; } };
}

function escapeHtml(text) {
  return String(text).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
}
