import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { sendJson, getJson } from '../shared/api.js';
import { askConfirm } from '../shared/dialog.js';
import { escapeHtml } from '../shared/format.js';
import { showMessage } from '../shared/message.js';
import { readRoomId, showRoomTabs, showTopBar } from '../shared/nav.js';
import { bayFootprint, rackPoint } from '../shared/rackGeometry.js';
import { slotWorldPosition } from '../shared/slotPosition.js';

// Trang Kho 3D (warehouse3d.html?roomId=1).
//
// Luồng: mở trang → 2 lần gọi server → vẽ.
//     GET /api/rooms/{id}         phòng + tường/cột + kệ        → vẽ sàn, tường, khung sắt kệ
//     GET /api/rooms/{id}/slots   mọi khung (+ pallet trong khung) → vẽ ô khung và khối pallet
//
// Trục: mặt bằng x → 3D x, mặt bằng y → 3D z, chiều cao → 3D y. 1 đơn vị = 1 mét.
// Trang này không tự chọn khung cho pallet: vị trí lấy từ server (slot.rackId, slot.level, slot.bay).

// ---------- kích thước vẽ (mét) ----------
const WALL_HEIGHT = 3;      // chiều cao vẽ tường, cột
const POST = 0.06;          // cạnh thanh đứng của kệ
const BOARD = 0.04;         // độ dày tấm ngang của kệ
const SHRINK = 0.9;         // vẽ ô khung nhỏ hơn 10% để thấy khe giữa các ô
const TOP_PLATE = 0.02;     // khung nóc: tấm phẳng mỏng (nóc không có trần)

// ---------- hiệu ứng pallet bay ----------
const START_DELAY = 0.35;   // giây giữa hai pallet liên tiếp (ở tốc độ ×1)
const FLIGHT_TIME = 1.2;    // giây một pallet bay (ở tốc độ ×1)
const ARC_HEIGHT = 2.0;     // đỉnh đường bay cao hơn đường thẳng bao nhiêu mét

// ---------- màu theo loại hàng: tính từ Id nên cùng loại hàng luôn cùng màu ----------
const PRODUCT_COLORS = ['#e6194b', '#3cb44b', '#4363d8', '#f58231', '#911eb4', '#42d4f4', '#f032e6', '#9a6324'];
const UNCLASSIFIED = '#9aa3b2';
const productColor = (id) => (id == null ? UNCLASSIFIED : PRODUCT_COLORS[(id - 1) % PRODUCT_COLORS.length]);

// ---------- các phần tử trên trang ----------
const view = document.getElementById('view');
const slotInfo = document.getElementById('slotInfo');

// ---------- biến nhớ ----------
const roomId = readRoomId();
let room = null;                 // dữ liệu phòng từ server
let slots = [];                  // dữ liệu khung từ server
let colorMode = 'size';          // 'size' (màu theo cỡ pallet) hoặc 'product' (theo loại hàng)
const clickable = [];            // các ô khung bấm được: { mesh, slot }
const palletMeshes = [];         // các khối pallet đang vẽ: { mesh, slot }
const palletBySlotCode = new Map();   // mã khung → khối pallet (để cho pallet bay vào đúng khung)
let flights = [];                // pallet đang bay
let speed = 1;                   // tốc độ hiệu ứng (×1, ×2…)

showTopBar('rooms');

// =============== 1. DỰNG CẢNH TRỐNG ===============
const scene = new THREE.Scene();
scene.background = new THREE.Color('#eef1f5');

const camera = new THREE.PerspectiveCamera(50, 1, 0.1, 500);

const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setPixelRatio(window.devicePixelRatio);
view.appendChild(renderer.domElement);

scene.add(new THREE.HemisphereLight('white', '#8899aa', 1.2));   // ánh sáng đều khắp nơi
const sun = new THREE.DirectionalLight('white', 1.5);            // ánh sáng chiếu chéo, tạo mặt sáng/tối
sun.position.set(10, 20, 8);
scene.add(sun);

const controls = new OrbitControls(camera, renderer.domElement);   // kéo để xoay, cuộn để phóng to
controls.maxPolarAngle = Math.PI / 2 - 0.02;                       // không cho chui xuống dưới sàn

// Đổi kích thước khung thì canvas đổi theo
function resize() {
  const width = view.clientWidth;
  const height = view.clientHeight;
  if (!width || !height) return;
  renderer.setSize(width, height);
  camera.aspect = width / height;
  camera.updateProjectionMatrix();
}
new ResizeObserver(resize).observe(view);
resize();

// Vòng vẽ: mỗi khung hình vẽ lại một lần (~60 lần/giây)
const clock = new THREE.Clock();
renderer.setAnimationLoop(function () {
  const seconds = Math.min(0.1, clock.getDelta()) * speed;   // tab ẩn lâu rồi quay lại: không nhảy cóc cả chục giây
  stepFlights(seconds);
  controls.update();
  renderer.render(scene, camera);
});

// =============== 2. TẢI DỮ LIỆU RỒI VẼ ===============
start();

async function start() {
  if (roomId == null) {
    document.getElementById('roomName').textContent = 'Thiếu roomId trên địa chỉ trang.';
    return;
  }
  showRoomTabs(roomId, 'warehouse3d');
  try {
    room = await getJson(`/api/rooms/${roomId}`);          // phòng + kệ + tường/cột
    slots = await getJson(`/api/rooms/${roomId}/slots`);   // mọi khung (+ pallet)
    document.getElementById('roomTitle').textContent = room.name;

    drawFloor();
    room.obstacles.forEach(drawObstacle);
    room.racks.forEach(drawRack);
    drawSlots();
    pointCamera();
    drawSummary();

    // Vừa nhập lô xong và bấm "Xem 3D": cho pallet bay vào (một lần)
    if (readSession(`play:${roomId}`)) {
      removeSession(`play:${roomId}`);
      replay();
    }
  } catch (err) {
    document.getElementById('roomName').textContent = err.message;
    showMessage(err.message, true);
  }
}

// =============== 3. CÁC HÀM VẼ ===============

// Vẽ một hộp, đặt theo TÂM (Three.js luôn đặt hình theo tâm)
function drawBox(centerX, centerY, centerZ, sizeX, sizeY, sizeZ, color, opacity = 1) {
  const mesh = new THREE.Mesh(
    new THREE.BoxGeometry(sizeX, sizeY, sizeZ),
    new THREE.MeshStandardMaterial({ color: color, transparent: opacity < 1, opacity: opacity }),
  );
  mesh.position.set(centerX, centerY, centerZ);
  scene.add(mesh);
  return mesh;
}

// Thêm đường viền quanh hộp cho dễ nhìn
function addEdges(mesh, color) {
  mesh.add(new THREE.LineSegments(
    new THREE.EdgesGeometry(mesh.geometry),
    new THREE.LineBasicMaterial({ color: color }),
  ));
}

// Sàn: tấm phẳng, góc (0,0) của phòng nằm ở gốc toạ độ
function drawFloor() {
  const floor = new THREE.Mesh(
    new THREE.PlaneGeometry(room.width, room.depth),
    new THREE.MeshStandardMaterial({ color: 'white' }),
  );
  floor.rotation.x = -Math.PI / 2;                          // mặt phẳng mặc định đứng → xoay nằm xuống
  floor.position.set(room.width / 2, 0, room.depth / 2);    // tâm sàn ở giữa phòng
  scene.add(floor);

  // Vạch mỗi 1 m trên sàn để ước lượng khoảng cách
  const points = [];
  for (let x = 0; x <= room.width; x++) points.push(x, 0.002, 0, x, 0.002, room.depth);
  for (let z = 0; z <= room.depth; z++) points.push(0, 0.002, z, room.width, 0.002, z);
  const grid = new THREE.BufferGeometry();
  grid.setAttribute('position', new THREE.Float32BufferAttribute(points, 3));
  scene.add(new THREE.LineSegments(grid, new THREE.LineBasicMaterial({ color: '#d0d5dd' })));
}

// Tường, cột: dữ liệu là góc trên trái (x, y) + kích thước → đổi ra tâm
function drawObstacle(o) {
  const color = o.kind === 'Wall' ? '#3b4252' : '#8b93a5';
  drawBox(o.x + o.width / 2, WALL_HEIGHT / 2, o.y + o.depth / 2, o.width, WALL_HEIGHT, o.depth, color);
}

// Khung sắt kệ: thanh đứng ở mỗi ranh giới ô (trước + sau), tấm ngang ở mỗi tầng
function drawRack(rack) {
  const length = rack.bays * rack.bayWidth;           // chiều dài kệ
  const height = rack.levels * rack.levelHeight;      // chiều cao tới tấm ngang trên cùng

  for (let i = 0; i <= rack.bays; i++) {
    for (const v of [0, rack.depth]) {
      const p = rackPoint(rack, i * rack.bayWidth, v);   // đổi (dọc kệ, sâu) → (x, z) trên sàn, đã tính xoay 90°
      drawBox(p.x, height / 2, p.z, POST, height, POST, '#2563eb');
    }
  }

  // Tấm ngang: tầng 0 là đáy, tầng cuối là tấm trên cùng (đáy của nóc nếu có)
  const a = rackPoint(rack, 0, 0);                  // một góc kệ
  const b = rackPoint(rack, length, rack.depth);    // góc đối diện
  for (let level = 0; level <= rack.levels; level++) {
    drawBox((a.x + b.x) / 2, level * rack.levelHeight, (a.z + b.z) / 2,
      Math.abs(b.x - a.x), BOARD, Math.abs(b.z - a.z), '#9db8f0');
  }
}

// Màu của pallet trong một khung, tuỳ chế độ đang chọn
function palletColor(slot) {
  return colorMode === 'product' ? productColor(slot.productTypeId) : (slot.color ?? '#999999');
}

// Ô khung + pallet
function drawSlots() {
  for (const slot of slots) {
    const rack = room.racks.find((r) => r.id === slot.rackId);   // khung thuộc kệ nào
    if (!rack) continue;

    const center = slotWorldPosition(rack, slot);         // tâm ô khung (m)
    const size = bayFootprint(rack);                      // kích thước ô theo x, z (đã tính xoay)
    const bottom = (slot.level - 1) * rack.levelHeight;   // độ cao đáy tầng

    // Ô khung: hộp xanh trong suốt. Nóc không giới hạn cao nên chỉ là tấm mỏng.
    const frameHeight = slot.isTop ? TOP_PLATE : rack.levelHeight * SHRINK;
    const frameBottom = slot.isTop ? bottom + BOARD / 2 : bottom;
    const frame = drawBox(center.x, frameBottom + frameHeight / 2, center.z,
      size.sizeX * SHRINK, frameHeight, size.sizeZ * SHRINK, '#2563eb', slot.isTop ? 0.15 : 0.07);
    addEdges(frame, '#9db8f0');
    clickable.push({ mesh: frame, slot: slot });

    // Có pallet thì vẽ khối pallet đúng kích thước, nằm trên mặt tầng
    if (slot.palletId != null) {
      const along = slot.palletAlongBay ?? rack.bayWidth * SHRINK;    // dọc ô
      const across = slot.palletAcrossDepth ?? rack.depth * SHRINK;   // theo chiều sâu kệ
      const height = slot.palletHeight ?? rack.levelHeight * SHRINK;
      const sizeX = rack.rotation === 90 ? across : along;            // kệ xoay thì đổi chỗ
      const sizeZ = rack.rotation === 90 ? along : across;
      const palletBottom = bottom + BOARD / 2 + (slot.isTop ? TOP_PLATE : 0);

      const pallet = drawBox(center.x, palletBottom + height / 2, center.z,
        Math.min(sizeX, size.sizeX), height, Math.min(sizeZ, size.sizeZ), palletColor(slot));
      addEdges(pallet, '#1d2433');
      palletMeshes.push({ mesh: pallet, slot: slot });
      palletBySlotCode.set(slot.code, pallet);
      clickable.push({ mesh: pallet, slot: slot });   // bấm vào chính khối pallet cũng chọn được khung
    }
  }
}

// Camera nhìn chéo xuống giữa phòng
function pointCamera() {
  const size = Math.max(room.width, room.depth);
  controls.target.set(room.width / 2, 0, room.depth / 2);
  camera.position.set(room.width / 2 + size * 0.7, size * 0.8, room.depth / 2 + size * 0.9);
  camera.far = size * 10;
  camera.updateProjectionMatrix();
}

// =============== 4. BẢNG THÔNG TIN, MÀU, CHÚ GIẢI ===============
function drawSummary() {
  const filled = slots.filter((s) => s.palletId != null).length;
  document.getElementById('roomName').textContent = room.name;
  document.getElementById('summary').textContent = `${filled} / ${slots.length} khung có hàng`;
  document.getElementById('btnReset').disabled = filled === 0;
  drawLegend();
}

// Chú giải: mỗi type (hoặc loại hàng) đang có trong kho một ô màu
function drawLegend() {
  const items = new Map();   // khoá → [màu, tên]
  for (const slot of slots) {
    if (slot.palletId == null) continue;
    if (colorMode === 'product') {
      items.set(slot.productTypeId ?? 'none', [productColor(slot.productTypeId), slot.productTypeName ?? 'Chưa phân loại']);
    } else {
      items.set(slot.typeCode, [slot.color, `Type ${slot.typeCode}`]);
    }
  }
  const legend = document.getElementById('legend');
  legend.innerHTML = [...items.values()]
    .map(([color, label]) => `<span class="legend-item"><i data-color="${color}"></i>${escapeHtml(label)}</span>`)
    .join('');
  for (const chip of legend.querySelectorAll('i')) chip.style.background = chip.dataset.color;
}

// Đổi chế độ màu: chỉ tô lại các khối pallet đang có, không gọi server
function setColorMode(mode) {
  colorMode = mode;
  document.getElementById('btnColorSize').setAttribute('aria-pressed', String(mode === 'size'));
  document.getElementById('btnColorProduct').setAttribute('aria-pressed', String(mode === 'product'));
  for (const { mesh, slot } of palletMeshes) mesh.material.color.set(palletColor(slot));
  drawLegend();
}
document.getElementById('btnColorSize').addEventListener('click', () => setColorMode('size'));
document.getElementById('btnColorProduct').addEventListener('click', () => setColorMode('product'));

// =============== 5. BẤM VÀO Ô KHUNG ===============
// Kéo chuột để xoay cũng sinh ra "click", nên chỉ tính là bấm khi chuột gần như không di chuyển.
let downX = 0;
let downY = 0;
let selectedMesh = null;

renderer.domElement.addEventListener('pointerdown', function (e) {
  downX = e.clientX;
  downY = e.clientY;
});

renderer.domElement.addEventListener('click', function (e) {
  if (Math.abs(e.clientX - downX) > 5 || Math.abs(e.clientY - downY) > 5) return;   // là kéo, không phải bấm

  // Đổi vị trí chuột sang toạ độ -1..1 rồi bắn "tia" từ camera xem trúng vật nào
  const rect = renderer.domElement.getBoundingClientRect();
  const mouse = new THREE.Vector2(
    ((e.clientX - rect.left) / rect.width) * 2 - 1,
    -((e.clientY - rect.top) / rect.height) * 2 + 1,
  );
  const ray = new THREE.Raycaster();
  ray.setFromCamera(mouse, camera);
  const hits = ray.intersectObjects(clickable.map((c) => c.mesh), false);

  // Bỏ sáng vật chọn trước
  if (selectedMesh) selectedMesh.material.emissive.setHex(0x000000);
  selectedMesh = null;

  if (hits.length === 0) {
    slotInfo.textContent = '';
    return;
  }
  const picked = clickable.find((c) => c.mesh === hits[0].object);
  selectedMesh = picked.mesh;
  selectedMesh.material.emissive.setHex(0x553300);   // sáng lên cho biết đang chọn

  const slot = picked.slot;
  slotInfo.textContent = slot.palletId != null
    ? `${slot.code}${slot.isTop ? ' (nóc)' : ''}: pallet ${slot.palletCode} · type ${slot.typeCode} · ${slot.productTypeName ?? 'Chưa phân loại'}`
    : `${slot.code}${slot.isTop ? ' (nóc)' : ''}: khung trống`;
});

// =============== 6. HIỆU ỨNG PALLET BAY ===============
// Danh sách pallet vừa xếp do trang Nhập lô cất vào sessionStorage (theo ĐÚNG thứ tự server đã xếp).
// Vị trí cuối của mỗi pallet chính là chỗ đã vẽ ở bước 3, nên không có toạ độ riêng của hiệu ứng.

function readSession(key) {
  try {
    return sessionStorage.getItem(key);
  } catch {
    return null;
  }
}
function removeSession(key) {
  try {
    sessionStorage.removeItem(key);
  } catch {
    // bỏ qua
  }
}

function replay() {
  const text = readSession(`placements:${roomId}`);
  if (text) play(JSON.parse(text));
}

// placements: [{ slotCode, palletCode }]
function play(placements) {
  finishFlights();
  const from = new THREE.Vector3(-2, 0.8, room.depth / 2);   // cửa kho phía bên trái

  placements.forEach(function (placement, i) {
    const pallet = palletBySlotCode.get(placement.slotCode);
    if (!pallet) return;   // khung chưa có khối pallet (dữ liệu chưa tải lại): không có gì để bay

    // Bay một bản sao của khối pallet; khối thật trong khung ẩn đi cho tới khi bản sao bay tới
    const flyer = new THREE.Mesh(
      pallet.geometry.clone(),
      new THREE.MeshStandardMaterial({ color: pallet.material.color.clone() }),
    );
    pallet.visible = false;
    flyer.visible = false;
    scene.add(flyer);
    flights.push({ pallet: pallet, flyer: flyer, from: from, to: pallet.position.clone(), delay: i * START_DELAY, elapsed: 0 });
  });
}

// Pallet bay tới nơi: hiện khối thật, bỏ bản sao
function land(flight) {
  flight.pallet.visible = true;
  scene.remove(flight.flyer);
  flight.flyer.geometry.dispose();
  flight.flyer.material.dispose();
}

// Cho mọi pallet đang bay hạ cánh ngay (nút Bỏ qua, hoặc trước khi bắt đầu lượt bay mới)
function finishFlights() {
  flights.forEach(land);
  flights = [];
}

// Chạy mỗi khung hình: đưa từng pallet đi thêm một đoạn
function stepFlights(seconds) {
  const remaining = [];
  for (const flight of flights) {
    flight.elapsed += seconds;
    const local = flight.elapsed - flight.delay;   // thời gian kể từ lúc pallet này bắt đầu bay
    if (local >= FLIGHT_TIME) {
      land(flight);
      continue;
    }
    if (local > 0) {
      const t = local / FLIGHT_TIME;               // 0 → 1
      const eased = t * t * (3 - 2 * t);           // vào ra mượt
      flight.flyer.visible = true;
      flight.flyer.position.lerpVectors(flight.from, flight.to, eased);
      flight.flyer.position.y += Math.sin(Math.PI * t) * ARC_HEIGHT;   // bay theo đường cong
    }
    remaining.push(flight);
  }
  flights = remaining;
}

// Nút tốc độ, Bỏ qua, Phát lại
for (const button of document.querySelectorAll('[data-speed]')) {
  button.addEventListener('click', function () {
    speed = Number(button.dataset.speed);
    document.querySelectorAll('[data-speed]').forEach((b) => b.setAttribute('aria-pressed', String(b === button)));
  });
}
document.getElementById('btnSkip').addEventListener('click', finishFlights);
document.getElementById('btnReplay').addEventListener('click', replay);

// =============== 7. ĐẶT LẠI KHO ===============
// POST /api/rooms/{id}/placements/reset: lấy mọi pallet ra khỏi khung (pallet và lô vẫn còn, chỉ chưa có khung)
document.getElementById('btnReset').addEventListener('click', async function () {
  const ok = await askConfirm('Lấy mọi pallet ra khỏi khung của kho này? Pallet và lô vẫn còn, chỉ chưa có khung.', { okText: 'Đặt lại', danger: true });
  if (!ok) return;
  try {
    const result = await sendJson('POST', `/api/rooms/${roomId}/placements/reset`);   // { cleared }
    removeSession(`placements:${roomId}`);
    showMessage(`Đã lấy ${result.cleared} pallet ra khỏi khung.`);
    location.reload();   // tải lại trang để vẽ lại cảnh từ dữ liệu mới
  } catch (err) {
    showMessage(err.message, true);
  }
});
