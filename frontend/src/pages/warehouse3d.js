import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { sendJson, getJson } from '../shared/api.js';
import { askConfirm } from '../shared/dialog.js';
import { escapeHtml } from '../shared/format.js';
import { showMessage } from '../shared/message.js';
import { readRoomId, showRoomTabs, showTopBar } from '../shared/nav.js';
import { bayFootprint, rackPoint } from '../shared/rackGeometry.js';
import { levelsOf, slotWorldPosition } from '../shared/slotPosition.js';

// Trang Kho 3D (warehouse3d.html?roomId=1).
//
// Luồng: mở trang → gọi server → vẽ.
//     GET /api/room3d/{id}         phòng + tổng số kệ, số khung    → vẽ sàn, chọn cách tải
//     GET /api/room3d/{id}/racks   kệ (+ các tầng)                  → vẽ khung sắt kệ
//     GET /api/room3d/{id}/slots   khung (+ pallet trong khung)     → vẽ ô khung và khối pallet
//   Kho nhỏ (≤ LOAD_ALL_LIMIT khung): tải mọi khung một lần.
//   Kho lớn: chỉ giữ các kệ quanh ĐIỂM CAMERA ĐANG NHÌN (controls.target), trong một hình vuông
//   (…/racks?x1=&y1=&x2=&y2= và …/slots?x1=&y1=&x2=&y2=): không bao giờ tải danh sách kệ của cả kho.
//   Camera kiểu bản đồ, cố định: góc nhìn và khoảng cách không đổi, kéo chuột chỉ dời trên mặt sàn (không xoay, không zoom).
//   Kéo xong (thả chuột) thì tải kệ mới lọt vào, GỠ kệ đã ra ngoài; phần xa mờ dần bằng sương mù.
//   Thêm ?lazy=1 trên địa chỉ để ép chế độ này khi thử trên kho nhỏ.
//
// Trục: mặt bằng x → 3D x, mặt bằng y → 3D z, chiều cao → 3D y. 1 đơn vị = 1 mét.
// Trang này không tự chọn khung cho pallet: vị trí lấy từ server (slot.rackId, slot.level, slot.bay).

// ---------- kích thước vẽ (mét) ----------
const WALL_HEIGHT = 3;      // chiều cao vẽ tường, cột
const DOOR_WIDTH = 4;       // cửa ra vào: rộng 4 m, ở giữa tường trái (x = 0), đúng chỗ pallet bay vào
const DOOR_HEIGHT = 4;
const POST = 0.06;          // cạnh thanh đứng của kệ
const BOARD = 0.04;         // độ dày tấm ngang của kệ
const SHRINK = 0.9;         // vẽ ô khung nhỏ hơn 10% để thấy khe giữa các ô
const TOP_PLATE = 0.02;     // khung nóc: tấm phẳng mỏng (nóc không có trần)

// ---------- tải theo vùng camera ----------
const LOAD_ALL_LIMIT = 5000;   // kho có tới 5.000 khung: tải hết một lần (vài nghìn object Three.js vẫn mượt)
const RADIUS_PER_DISTANCE = 0.6; // kho lớn: nửa cạnh vùng giữ kệ = 0,6 × khoảng cách camera (ở 30 m: hình vuông 36 × 36 m)
const VIEW_TILT = 60;          // kho lớn: góc nhìn cố định, ngả 60° so với nhìn thẳng xuống (nhìn chéo, thấy mặt bên của kệ và hàng trong khung)
const VIEW_TURN = -20;         // …và xiên 20° từ phía cửa: camera lệch về bên cửa (tường trái), nhìn chéo vào trong; trục x vẫn chạy sang PHẢI màn hình
const DOOR_INSIDE = 12;        // lúc mở trang: điểm nhìn cách cửa 12 m vào trong, ngang giữa chiều sâu kho (cửa ở giữa tường trái)
const VIEW_DISTANCE = 30;      // camera cách điểm nhìn ít nhất 30 m; người dùng không zoom được (zoom ra là vùng tải phình to → lag)
const DISTANCE_PER_HEIGHT = 4;  // kệ cao thì lùi xa hơn: khoảng cách = 4 × độ cao kệ cao nhất (kệ 11,8 m → khoảng 47 m), để không nhìn sát nóc kệ
const VIEW_FOV = 30;           // ống kính hẹp (mặc định 50°): kệ gần và xa chênh nhau ít, phòng trông phẳng, không méo
const LOAD_DELAY = 200;        // thả chuột xong chờ 200 ms rồi mới tải (kéo tiếp thì huỷ lần tải này)

// ---------- hiệu ứng pallet bay ----------
const START_DELAY = 0.35;   // giây giữa hai pallet liên tiếp (ở tốc độ ×1)
const FLIGHT_TIME = 1.2;    // giây một pallet bay (ở tốc độ ×1)
const ARC_HEIGHT = 2.0;     // đỉnh đường bay cao hơn đường thẳng bao nhiêu mét

// ---------- màu theo loại hàng: tính từ Id nên cùng loại hàng luôn cùng màu ----------
const PRODUCT_COLORS = ['#e6194b', '#3cb44b', '#4363d8', '#f58231', '#911eb4', '#42d4f4', '#f032e6', '#9a6324'];
const UNCLASSIFIED = '#9aa3b2';
const productColor = (id) => (id == null ? UNCLASSIFIED : PRODUCT_COLORS[(id - 1) % PRODUCT_COLORS.length]);

const view = document.getElementById('view');
const slotInfo = document.getElementById('slotInfo');
const roomId = readRoomId();
let room = null;                 // dữ liệu phòng từ server: kích thước + rackCount, slotCount (không có danh sách kệ)
const racksById = new Map();     // kệ đang có trong máy: rackId → kệ (kho nhỏ: mọi kệ; kho lớn: kệ quanh điểm nhìn)
let slots = [];                  // dữ liệu khung từ server
let colorMode = 'size';          // 'size' (màu theo cỡ pallet) hoặc 'product' (theo loại hàng)
const clickable = [];            // các ô khung bấm được: { mesh, slot }
const palletMeshes = [];         // các khối pallet đang vẽ: { mesh, slot }
const palletBySlotCode = new Map();   // mã khung → khối pallet (để cho pallet bay vào đúng khung)
const rackGroups = new Map();    // chế độ kho lớn: rackId → { group, các InstancedMesh của kệ, khung, pallet } (xem drawRackInstanced)
let viewDistance = VIEW_DISTANCE;   // khoảng cách camera hiện tại (tự tăng theo độ cao kệ ở lần tải đầu)
let drawTarget = null;           // drawBox thêm vật vào đây: cả cảnh, hoặc nhóm của kệ đang vẽ
let lazy = false;                // true = kho lớn, tải theo vùng camera
let viewRequest = 0;             // số thứ tự lần tải theo vùng: kết quả của lần cũ về muộn thì bỏ
let viewTimer = null;
let flights = [];                // pallet đang bay
let speed = 1;                   // tốc độ hiệu ứng (×1, ×2…)

showTopBar('rooms');

const scene = new THREE.Scene();
scene.background = new THREE.Color('#eef1f5');
drawTarget = scene;

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

const clock = new THREE.Clock();
renderer.setAnimationLoop(function () {
  const seconds = Math.min(0.1, clock.getDelta()) * speed;   // tab ẩn lâu rồi quay lại: không nhảy cóc cả chục giây
  stepFlights(seconds);
  controls.update();
  renderer.render(scene, camera);
});
start();
async function start() {
  if (roomId == null) {
    document.getElementById('roomName').textContent = 'Thiếu roomId trên địa chỉ trang.';
    return;
  }
  showRoomTabs(roomId, 'warehouse3d');
  document.getElementById('linkReceive').href = `receive.html?roomId=${roomId}`;
  try {
    room = await getJson(`/api/room3d/${roomId}`);          // phòng + tổng số kệ, số khung
    document.getElementById('roomTitle').textContent = room.name;
    drawFloor();
    drawDoor();
    (room.obstacles ?? []).forEach(drawObstacle);   // API room3d hiện chưa trả tường/cột
    lazy = room.slotCount > LOAD_ALL_LIMIT || new URLSearchParams(location.search).get('lazy') === '1';
    pointCamera();
    if (lazy) {
      setupMapCamera();
      await loadVisible();                                     // kho lớn: chỉ phần camera đang thấy
      controls.addEventListener('end', scheduleLoadVisible);   // thả chuột sau khi xoay / kéo
    } else {
      const [allRacks, allSlots] = await Promise.all([          // kho nhỏ: mọi kệ, mọi khung một lần
        getJson(`/api/room3d/${roomId}/racks`),
        getJson(`/api/room3d/${roomId}/slots`),
      ]);
      slots = allSlots;
      for (const rack of allRacks) racksById.set(rack.id, rack);
      allRacks.forEach(drawRack);
      drawSlots(slots);
    }
    drawSummary();
    drawMinimap();
    controls.addEventListener('change', drawMinimap);   // kéo camera: ô "đang xem" trên bản đồ nhỏ chạy theo

    // Vừa bấm "Xếp và xem 3D": báo kết quả xếp và cho pallet bay vào (một lần)
    const note = readSession(`note:${roomId}`);
    if (note) {
      removeSession(`note:${roomId}`);
      showMessage(note);
    }
    if (readSession(`play:${roomId}`)) {
      removeSession(`play:${roomId}`);
      replay();
    }
    watchAllocation();   // vừa bấm "Xếp và xem 3D": worker đang xếp ngầm → tự cập nhật cảnh
  } catch (err) {
    document.getElementById('roomName').textContent = err.message;
    showMessage(err.message, true);
  }
}


function drawBox(centerX, centerY, centerZ, sizeX, sizeY, sizeZ, color, opacity = 1) {
  const mesh = new THREE.Mesh(
    new THREE.BoxGeometry(sizeX, sizeY, sizeZ),
    new THREE.MeshStandardMaterial({ color: color, transparent: opacity < 1, opacity: opacity }),
  );
  mesh.position.set(centerX, centerY, centerZ);
  drawTarget.add(mesh);
  return mesh;
}

// Thêm đường viền quanh hộp cho dễ nhìn
function addEdges(mesh, color) {
  mesh.add(new THREE.LineSegments(
    new THREE.EdgesGeometry(mesh.geometry),
    new THREE.LineBasicMaterial({ color: color }),
  ));
}

// Cửa ra vào: khung xanh lá ở giữa tường trái (x = 0) + chữ "CỬA" nổi phía trên, nhìn từ đâu cũng đọc được
function drawDoor() {
  const z = room.depth / 2;
  const green = '#16a34a';
  drawBox(0, DOOR_HEIGHT / 2, z - DOOR_WIDTH / 2, 0.3, DOOR_HEIGHT, 0.3, green);   // trụ cửa
  drawBox(0, DOOR_HEIGHT / 2, z + DOOR_WIDTH / 2, 0.3, DOOR_HEIGHT, 0.3, green);   // trụ cửa
  drawBox(0, DOOR_HEIGHT, z, 0.3, 0.3, DOOR_WIDTH + 0.3, green);                   // xà ngang
  drawBox(1.5, 0.01, z, 3, 0.02, DOOR_WIDTH, green, 0.35);                          // thảm sàn trước cửa

  // Chữ vẽ lên canvas 2D rồi dán lên sprite (sprite luôn quay mặt về camera)
  const canvas = document.createElement('canvas');
  canvas.width = 256;
  canvas.height = 96;
  const g = canvas.getContext('2d');
  g.fillStyle = green;
  g.fillRect(0, 0, 256, 96);
  g.fillStyle = 'white';
  g.font = 'bold 56px system-ui, sans-serif';
  g.textAlign = 'center';
  g.textBaseline = 'middle';
  g.fillText('CỬA', 128, 50);
  const label = new THREE.Sprite(new THREE.SpriteMaterial({ map: new THREE.CanvasTexture(canvas) }));
  label.scale.set(4, 1.5, 1);
  label.position.set(0, DOOR_HEIGHT + 1.5, z);
  scene.add(label);
}

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
// ---------- Vị trí, kích thước các hộp (dùng chung cho cách vẽ thường và cách vẽ InstancedMesh) ----------
// Mỗi hộp: { x, y, z } là TÂM, { sx, sy, sz } là cạnh theo trục x, y, z của cảnh (mét).

// Khung sắt của kệ: thanh đứng ở mỗi mốc ô (2 phía trước / sau) + tấm ngang ở đáy mỗi tầng và trên cùng
function rackParts(rack) {
  const parts = [];
  const length = rack.bays * rack.bayWidth;           // chiều dài kệ
  const levels = levelsOf(rack);                      // đáy và chiều cao từng tầng (tầng có thể cao khác nhau)
  const height = levels.top;                          // chiều cao tới tấm ngang trên cùng

  for (let i = 0; i <= rack.bays; i++) {
    for (const v of [0, rack.depth]) {
      const p = rackPoint(rack, i * rack.bayWidth, v);   // đổi (dọc kệ, sâu) → (x, z) trên sàn, đã tính xoay 90°
      parts.push({ x: p.x, y: height / 2, z: p.z, sx: POST, sy: height, sz: POST, color: '#2563eb' });
    }
  }

  // Tấm ngang: tầng 0 là đáy, tầng cuối là tấm trên cùng (đáy của nóc nếu có)
  const a = rackPoint(rack, 0, 0);                  // một góc kệ
  const b = rackPoint(rack, length, rack.depth);    // góc đối diện
  for (const y of [...levels.bottoms, levels.top]) {
    parts.push({ x: (a.x + b.x) / 2, y: y, z: (a.z + b.z) / 2,
      sx: Math.abs(b.x - a.x), sy: BOARD, sz: Math.abs(b.z - a.z), color: '#9db8f0' });
  }
  return parts;
}

// Ô khung (hộp trong suốt bao quanh chỗ đặt pallet); khung nóc là tấm phẳng mỏng
function frameBox(rack, slot) {
  const center = slotWorldPosition(rack, slot);
  const size = bayFootprint(rack);
  const level = levelsOf(rack).byNumber.get(slot.level) ?? { bottom: 0, height: 0 };
  const frameHeight = slot.isTop ? TOP_PLATE : level.height * SHRINK;
  const frameBottom = slot.isTop ? level.bottom + BOARD / 2 : level.bottom;
  return { x: center.x, y: frameBottom + frameHeight / 2, z: center.z,
    sx: size.sizeX * SHRINK, sy: frameHeight, sz: size.sizeZ * SHRINK };
}

// Khối pallet đúng kích thước, nằm trên mặt tầng (không lấn ra ngoài ô)
function palletBox(rack, slot) {
  const center = slotWorldPosition(rack, slot);
  const size = bayFootprint(rack);
  const level = levelsOf(rack).byNumber.get(slot.level) ?? { bottom: 0, height: 0 };
  const along = slot.palletAlongBay ?? rack.bayWidth * SHRINK;    // dọc ô
  const across = slot.palletAcrossDepth ?? rack.depth * SHRINK;   // theo chiều sâu kệ
  const height = slot.palletHeight ?? level.height * SHRINK;
  const sizeX = rack.rotation === 90 ? across : along;            // kệ xoay thì đổi chỗ
  const sizeZ = rack.rotation === 90 ? along : across;
  const palletBottom = level.bottom + BOARD / 2 + (slot.isTop ? TOP_PLATE : 0);
  return { x: center.x, y: palletBottom + height / 2, z: center.z,
    sx: Math.min(sizeX, size.sizeX), sy: height, sz: Math.min(sizeZ, size.sizeZ) };
}

// ---------- Cách vẽ thường (kho nhỏ): mỗi hộp một Mesh, có đường viền ----------
function drawRack(rack) {
  for (const b of rackParts(rack)) drawBox(b.x, b.y, b.z, b.sx, b.sy, b.sz, b.color);
}

// Màu của pallet trong một khung, tuỳ chế độ đang chọn
function palletColor(slot) {
  return colorMode === 'product' ? productColor(slot.productTypeId) : (slot.color ?? '#999999');
}

// Vẽ ô khung + khối pallet cho danh sách khung được đưa vào
function drawSlots(list) {
  for (const slot of list) {
    const rack = racksById.get(slot.rackId);
    if (!rack) continue;

    const f = frameBox(rack, slot);
    const frame = drawBox(f.x, f.y, f.z, f.sx, f.sy, f.sz, '#2563eb', slot.isTop ? 0.15 : 0.07);
    addEdges(frame, '#9db8f0');
    clickable.push({ mesh: frame, slot: slot });

    if (slot.palletId != null) {
      const b = palletBox(rack, slot);
      const pallet = drawBox(b.x, b.y, b.z, b.sx, b.sy, b.sz, palletColor(slot));
      addEdges(pallet, '#1d2433');
      palletMeshes.push({ mesh: pallet, slot: slot });
      palletBySlotCode.set(slot.code, pallet);
      clickable.push({ mesh: pallet, slot: slot });   // bấm vào chính khối pallet cũng chọn được khung
    }
  }
}

// ---------- Cách vẽ InstancedMesh (kho lớn): mỗi kệ chỉ 4 lệnh vẽ, không đường viền ----------
// InstancedMesh: một hình hộp + một chất liệu, vẽ N bản sao trong MỘT lệnh; mỗi bản sao có ma trận (vị trí, kích thước) và màu riêng.
// Mọi kệ dùng chung UNIT_BOX (hộp 1 × 1 × 1 m, kéo giãn bằng ma trận) và các chất liệu dưới đây → gỡ kệ không cần huỷ chúng.
const UNIT_BOX = new THREE.BoxGeometry(1, 1, 1);
const RACK_MATERIAL = new THREE.MeshStandardMaterial({ color: 'white' });     // màu thật lấy từ màu từng bản sao
const FRAME_MATERIAL = new THREE.MeshStandardMaterial({ color: '#2563eb', transparent: true, opacity: 0.07, depthWrite: false });
const TOP_MATERIAL = new THREE.MeshStandardMaterial({ color: '#2563eb', transparent: true, opacity: 0.15, depthWrite: false });
const PALLET_MATERIAL = new THREE.MeshStandardMaterial({ color: 'white' });
const tmpMatrix = new THREE.Matrix4();
const tmpPosition = new THREE.Vector3();
const tmpScale = new THREE.Vector3();
const tmpColor = new THREE.Color();
const NO_TURN = new THREE.Quaternion();

// Tạo InstancedMesh đủ chỗ cho `capacity` bản sao; tạo sẵn bảng màu để đổi màu về sau không phải dịch lại shader
function newInstanced(material, capacity) {
  const mesh = new THREE.InstancedMesh(UNIT_BOX, material, Math.max(1, capacity));
  mesh.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(Math.max(1, capacity) * 3), 3);
  mesh.count = 0;
  mesh.frustumCulled = false;   // vài chục kệ quanh điểm nhìn: vẽ hết, khỏi tính lại khối bao mỗi lần thêm pallet
  return mesh;
}

// Thêm một bản sao (hộp b, màu color) vào cuối mesh
function pushInstance(mesh, b, color) {
  const i = mesh.count;
  tmpMatrix.compose(tmpPosition.set(b.x, b.y, b.z), NO_TURN, tmpScale.set(b.sx, b.sy, b.sz));
  mesh.setMatrixAt(i, tmpMatrix);
  mesh.setColorAt(i, tmpColor.set(color));
  mesh.count = i + 1;
  mesh.instanceMatrix.needsUpdate = true;
  mesh.instanceColor.needsUpdate = true;
  mesh.boundingSphere = null;   // tia bấm chuột tự tính lại khối bao
}

// Vẽ một kệ (khung sắt + ô khung + ô nóc + pallet) thành 4 InstancedMesh trong một nhóm; trả về trạng thái để thêm pallet / gỡ kệ
function drawRackInstanced(rack, rackSlots) {
  const group = new THREE.Group();
  const parts = rackParts(rack);
  const rackMesh = newInstanced(RACK_MATERIAL, parts.length);
  for (const b of parts) pushInstance(rackMesh, b, b.color);

  const frameSlots = [];
  const topSlots = [];
  for (const slot of rackSlots) (slot.isTop ? topSlots : frameSlots).push(slot);
  const frames = newInstanced(FRAME_MATERIAL, frameSlots.length);
  for (const slot of frameSlots) pushInstance(frames, frameBox(rack, slot), 'white');
  const tops = newInstanced(TOP_MATERIAL, topSlots.length);
  for (const slot of topSlots) pushInstance(tops, frameBox(rack, slot), 'white');

  const state = {
    rack: rack,
    group: group,
    meshes: [rackMesh, frames, tops],
    pallets: newInstanced(PALLET_MATERIAL, rackSlots.length),   // đủ chỗ: mỗi khung tối đa một pallet
    palletSlots: [],                                            // bản sao thứ i của pallets ↔ palletSlots[i]
    slotById: new Map(),
  };
  state.meshes.push(state.pallets);
  for (const slot of rackSlots) state.slotById.set(slot.id, slot);
  for (const slot of rackSlots) if (slot.palletId != null) addPalletInstance(state, slot);

  // Bấm chuột: tia trúng bản sao thứ instanceId của mesh nào thì biết ngay khung nào
  clickable.push({ mesh: frames, slots: frameSlots, rackId: rack.id });
  clickable.push({ mesh: tops, slots: topSlots, rackId: rack.id });
  clickable.push({ mesh: state.pallets, slots: state.palletSlots, rackId: rack.id });

  for (const mesh of state.meshes) group.add(mesh);
  scene.add(group);
  return state;
}

// Khung vừa có pallet: thêm một bản sao vào mesh pallet của kệ (không vẽ lại cả kệ)
function addPalletInstance(state, slot) {
  if (state.pallets.count >= state.pallets.instanceMatrix.count) return;
  pushInstance(state.pallets, palletBox(state.rack, slot), palletColor(slot));
  state.palletSlots.push(slot);
}

// =============== TẢI THEO VÙNG CAMERA ===============

// Vector từ điểm nhìn tới camera, theo góc cố định: cao lên theo VIEW_TILT, xiên theo VIEW_TURN
function viewOffset(distance) {
  const tilt = THREE.MathUtils.degToRad(VIEW_TILT);
  const turn = THREE.MathUtils.degToRad(VIEW_TURN);
  return new THREE.Vector3(
    distance * Math.sin(tilt) * Math.sin(turn),
    distance * Math.cos(tilt),
    distance * Math.sin(tilt) * Math.cos(turn),
  );
}

// Kho lớn: camera kiểu bản đồ. Tắt xoay; chuột trái / phải đều là dời, dời song song mặt sàn; cuộn để zoom.
// Zoom và dời của OrbitControls giữ nguyên hướng camera, nên góc chéo luôn cố định.
function setupMapCamera() {
  camera.fov = VIEW_FOV;
  camera.updateProjectionMatrix();
  controls.enableRotate = false;
  controls.screenSpacePanning = false;   // dời trên mặt sàn (không bay lên / xuống)
  controls.mouseButtons = { LEFT: THREE.MOUSE.PAN, MIDDLE: THREE.MOUSE.DOLLY, RIGHT: THREE.MOUSE.PAN };
  controls.touches = { ONE: THREE.TOUCH.PAN, TWO: THREE.TOUCH.DOLLY_PAN };
  controls.enableZoom = false;           // camera cố định: chỉ kéo để đi, không phóng to / thu nhỏ
  controls.addEventListener('change', keepTargetInRoom);
  document.querySelector('#view-panel .hint, .hint').textContent = 'Kéo để di chuyển, bấm vào khung để xem.';
}

// Không cho điểm nhìn ra ngoài phòng: kéo quá mép thì đẩy cả điểm nhìn lẫn camera lùi lại cùng một đoạn
function keepTargetInRoom() {
  const t = controls.target;
  const dx = Math.min(room.width, Math.max(0, t.x)) - t.x;
  const dz = Math.min(room.depth, Math.max(0, t.z)) - t.z;
  if (dx === 0 && dz === 0) return;
  t.x += dx; t.z += dz;
  camera.position.x += dx; camera.position.z += dz;
}

// Vùng cần giữ (mét, hệ toạ độ mặt bằng): hình vuông quanh điểm camera đang nhìn (controls.target).
// Nửa cạnh = RADIUS_PER_DISTANCE × khoảng cách camera (camera không zoom được nên vùng chỉ đổi cỡ khi khoảng cách tự chỉnh theo độ cao kệ).
function viewArea() {
  const target = controls.target;
  const radius = viewDistance * RADIUS_PER_DISTANCE;
  // Trục z của cảnh 3D là trục Y của mặt bằng
  return {
    radius: radius,
    x1: Math.max(0, target.x - radius), y1: Math.max(0, target.z - radius),
    x2: Math.min(room.width, target.x + radius), y2: Math.min(room.depth, target.z + radius),
  };
}

// Hình chữ nhật kệ chiếm trên sàn (giống điều kiện SQL ở server): xoay 90° thì đổi chỗ dài và sâu
function rackTouches(rack, r) {
  const length = rack.bays * rack.bayWidth;
  const sizeX = rack.rotation === 90 ? rack.depth : length;
  const sizeY = rack.rotation === 90 ? length : rack.depth;
  return rack.x < r.x2 && rack.x + sizeX > r.x1 && rack.y < r.y2 && rack.y + sizeY > r.y1;
}

// Sương mù: phần ngoài vùng tải mờ dần vào nền, không thấy chỗ kệ bị cắt
function updateFog(radius) {
  const toTarget = camera.position.distanceTo(controls.target);
  scene.fog = new THREE.Fog(scene.background, toTarget + radius * 0.4, toTarget + radius);
}

// Gỡ một kệ khỏi cảnh: bỏ khỏi danh sách bấm, giải phóng bộ nhớ GPU của các InstancedMesh
// (hình hộp và chất liệu dùng chung nên KHÔNG huỷ)
function unloadRack(rackId) {
  const state = rackGroups.get(rackId);
  scene.remove(state.group);
  for (const mesh of state.meshes) mesh.dispose();
  rackGroups.delete(rackId);
  racksById.delete(rackId);

  if (selectedSlot && selectedSlot.rackId === rackId) clearSelection();
  const keep = (item) => (item.rackId ?? item.slot.rackId) !== rackId;
  clickable.splice(0, clickable.length, ...clickable.filter(keep));
  slots = slots.filter((slot) => slot.rackId !== rackId);
}

// Giữ đúng các kệ trong vùng đang nhìn: gỡ kệ đã ra ngoài, tải + vẽ kệ mới lọt vào
async function loadVisible() {
  controls.update();
  const r = viewArea();
  updateFog(r.radius);

  for (const rackId of [...rackGroups.keys()]) {
    const rack = racksById.get(rackId);
    if (!rack || !rackTouches(rack, r)) unloadRack(rackId);
  }

  // Kệ và khung của cùng một vùng, gọi song song
  const area = `x1=${r.x1.toFixed(2)}&y1=${r.y1.toFixed(2)}&x2=${r.x2.toFixed(2)}&y2=${r.y2.toFixed(2)}`;
  const myRequest = ++viewRequest;
  const [visibleRacks, visibleSlots] = await Promise.all([
    getJson(`/api/room3d/${roomId}/racks?${area}`),
    getJson(`/api/room3d/${roomId}/slots?${area}`),
  ]);
  if (myRequest !== viewRequest) return;   // camera đã sang chỗ khác: kết quả này cũ
  for (const rack of visibleRacks) {
    if (!rackGroups.has(rack.id)) racksById.set(rack.id, rack);
  }

  const byRack = new Map();   // rackId → khung của kệ đó (chỉ kệ chưa vẽ)
  for (const slot of visibleSlots) {
    if (rackGroups.has(slot.rackId)) continue;
    if (!byRack.has(slot.rackId)) byRack.set(slot.rackId, []);
    byRack.get(slot.rackId).push(slot);
  }
  for (const [rackId, rackSlots] of byRack) {
    const rack = racksById.get(rackId);
    if (!rack) continue;
    rackGroups.set(rackId, drawRackInstanced(rack, rackSlots));
    for (const slot of rackSlots) slots.push(slot);   // dồn vào danh sách chung để đếm, chú giải
  }
  drawSummary();
  fitDistanceToRacks(visibleRacks);
}

// Lần đầu thấy kệ: kệ cao thì lùi camera ra (giữ nguyên góc nhìn), rồi tải lại vùng mới rộng hơn
function fitDistanceToRacks(visibleRacks) {
  let tallest = 0;
  for (const rack of visibleRacks) {
    const levels = levelsOf(rack);
    let top = levels.top;
    for (const level of levels.byNumber.values()) if (level.isTop) top = Math.max(top, level.bottom + level.height);
    tallest = Math.max(tallest, top);
  }
  const wanted = Math.max(VIEW_DISTANCE, tallest * DISTANCE_PER_HEIGHT);
  if (wanted <= viewDistance + 0.5) return;
  viewDistance = wanted;
  camera.position.copy(controls.target).add(viewOffset(viewDistance));
  drawMinimap();
  scheduleLoadVisible();
}

// Thả chuột: chờ một chút rồi tải (thả rồi kéo tiếp ngay thì chỉ tải một lần)
function scheduleLoadVisible() {
  clearTimeout(viewTimer);
  viewTimer = setTimeout(function () {
    loadVisible().catch((err) => showMessage(err.message, true));
  }, LOAD_DELAY);
}

// Kho nhỏ: nhìn toàn kho. Kho lớn: đứng ở cửa (tường trái) nhìn chéo vào, cách VIEW_DISTANCE, góc cố định VIEW_TILT / VIEW_TURN;
// kéo chuột sang trái để đi dần vào sâu trong kho (sang phải màn hình).
// =============== BẢN ĐỒ NHỎ ===============
// Cả phòng nhìn từ trên xuống, cùng hướng với cảnh 3D: x (chiều dài, cột Width) sang phải, y (chiều rộng, cột Depth) xuống dưới.
// Cửa ở giữa tường trái. Kho lớn: khung xanh là vùng đang giữ kệ, chấm đỏ là điểm camera đang nhìn.
const MINIMAP_MAX_W = 260;   // px, chỗ vẽ phòng tối đa
const MINIMAP_MAX_H = 160;
const MINIMAP_PAD_LEFT = 22; // chừa chỗ cho mũi tên + chữ chiều rộng
const MINIMAP_PAD_BOTTOM = 26;
const MINIMAP_PAD = 8;

function drawMinimap() {
  if (!room) return;
  const canvas = document.getElementById('minimap');
  const scale = Math.min(MINIMAP_MAX_W / room.width, MINIMAP_MAX_H / room.depth);   // px trên 1 m, giữ đúng tỉ lệ dài / rộng
  const w = room.width * scale;
  const h = Math.max(6, room.depth * scale);   // phòng rất hẹp (3 km × 88 m) vẫn thấy được
  const cssW = MINIMAP_PAD_LEFT + w + MINIMAP_PAD;
  const cssH = MINIMAP_PAD + h + MINIMAP_PAD_BOTTOM;
  const dpr = window.devicePixelRatio || 1;
  canvas.width = cssW * dpr;
  canvas.height = cssH * dpr;
  canvas.style.width = `${cssW}px`;
  canvas.style.height = `${cssH}px`;

  const g = canvas.getContext('2d');
  g.setTransform(dpr, 0, 0, dpr, 0, 0);
  g.clearRect(0, 0, cssW, cssH);
  g.font = '11px system-ui, sans-serif';
  const ox = MINIMAP_PAD_LEFT;   // góc trên trái của phòng trên bản đồ
  const oy = MINIMAP_PAD;
  const toX = (x) => ox + x * scale;
  const toY = (y) => oy + y * scale * (h / (room.depth * scale));

  // Phòng
  g.fillStyle = '#f5f7fa';
  g.strokeStyle = '#1d2433';
  g.lineWidth = 1.5;
  g.fillRect(ox, oy, w, h);
  g.strokeRect(ox, oy, w, h);

  // Vùng đang giữ kệ + điểm nhìn
  if (lazy) {
    const a = viewArea();
    g.fillStyle = 'rgba(37, 99, 235, 0.25)';
    g.strokeStyle = '#2563eb';
    g.lineWidth = 1;
    g.fillRect(toX(a.x1), toY(a.y1), (a.x2 - a.x1) * scale, toY(a.y2) - toY(a.y1));
    g.strokeRect(toX(a.x1), toY(a.y1), (a.x2 - a.x1) * scale, toY(a.y2) - toY(a.y1));
  }
  g.fillStyle = '#dc2626';
  g.beginPath();
  g.arc(toX(controls.target.x), toY(controls.target.z), 3, 0, Math.PI * 2);
  g.fill();

  // Cửa: đoạn xanh lá ở giữa tường trái
  const doorHalf = Math.max(4, Math.min(h / 2 - 1, 2 * scale));
  g.strokeStyle = '#16a34a';
  g.lineWidth = 4;
  g.beginPath();
  g.moveTo(ox, oy + h / 2 - doorHalf);
  g.lineTo(ox, oy + h / 2 + doorHalf);
  g.stroke();
  g.fillStyle = '#16a34a';
  g.fillText('Cửa', ox + 5, Math.max(oy + 10, oy + h / 2 - doorHalf - 3));   // chữ ngay trên cửa, không đè chấm điểm nhìn

  // Mũi tên chiều dài (trục x) dưới phòng
  const ay = oy + h + 9;
  drawArrow(g, ox, ay, ox + w, ay);
  g.fillStyle = '#1d2433';
  g.textAlign = 'center';
  g.fillText(`Chiều dài (x, Width) = ${formatMeters(room.width)}`, ox + w / 2, ay + 13);

  // Mũi tên chiều rộng (trục y) bên trái phòng
  const ax = ox - 9;
  drawArrow(g, ax, oy, ax, oy + h);
  g.save();
  g.translate(ax - 4, oy + h / 2);
  g.rotate(-Math.PI / 2);
  g.fillText(h > 60 ? `Rộng (y, Depth) = ${formatMeters(room.depth)}` : formatMeters(room.depth), 0, 0);
  g.restore();
  g.textAlign = 'start';
}

// Mũi tên từ (x1, y1) tới (x2, y2), đầu nhọn ở (x2, y2)
function drawArrow(g, x1, y1, x2, y2) {
  const angle = Math.atan2(y2 - y1, x2 - x1);
  g.strokeStyle = '#1d2433';
  g.fillStyle = '#1d2433';
  g.lineWidth = 1;
  g.beginPath();
  g.moveTo(x1, y1);
  g.lineTo(x2, y2);
  g.stroke();
  g.beginPath();
  g.moveTo(x2, y2);
  g.lineTo(x2 - 6 * Math.cos(angle - 0.4), y2 - 6 * Math.sin(angle - 0.4));
  g.lineTo(x2 - 6 * Math.cos(angle + 0.4), y2 - 6 * Math.sin(angle + 0.4));
  g.closePath();
  g.fill();
}

const formatMeters = (m) => (m >= 1000 ? `${(m / 1000).toFixed(2)} km` : `${Math.round(m * 10) / 10} m`);

function pointCamera() {
  const roomSize = Math.max(room.width, room.depth);
  camera.far = roomSize * 10;
  if (lazy) {
    const cx = Math.min(room.width, DOOR_INSIDE);   // gần cửa
    const cz = room.depth / 2;                      // ngang cửa (cửa ở giữa tường trái, giống chỗ pallet bay vào)
    controls.target.set(cx, 0, cz);
    camera.position.copy(controls.target).add(viewOffset(viewDistance));
    camera.updateProjectionMatrix();
    return;
  }
  controls.target.set(room.width / 2, 0, room.depth / 2);
  camera.position.set(room.width / 2 + roomSize * 0.7, roomSize * 0.8, room.depth / 2 + roomSize * 0.9);
  camera.updateProjectionMatrix();
}

function drawSummary() {
  const filled = slots.filter((s) => s.palletId != null).length;
  document.getElementById('roomName').textContent = room.name;
  document.getElementById('summary').textContent = lazy
    ? `${filled} / ${slots.length} khung có hàng · đang giữ ${rackGroups.size} / ${room.rackCount} kệ quanh điểm nhìn`
    : `${filled} / ${slots.length} khung có hàng`;
  document.getElementById('btnReset').disabled = filled === 0;
  drawLegend();
}

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

function setColorMode(mode) {
  colorMode = mode;
  document.getElementById('btnColorSize').setAttribute('aria-pressed', String(mode === 'size'));
  document.getElementById('btnColorProduct').setAttribute('aria-pressed', String(mode === 'product'));
  for (const { mesh, slot } of palletMeshes) mesh.material.color.set(palletColor(slot));
  for (const state of rackGroups.values()) {                  // kho lớn: đổi màu từng bản sao pallet
    state.palletSlots.forEach((slot, i) => state.pallets.setColorAt(i, tmpColor.set(palletColor(slot))));
    state.pallets.instanceColor.needsUpdate = true;
  }
  drawLegend();
}
document.getElementById('btnColorSize').addEventListener('click', () => setColorMode('size'));
document.getElementById('btnColorProduct').addEventListener('click', () => setColorMode('product'));

// =============== 5. BẤM VÀO Ô KHUNG ===============
// Kéo chuột để xoay cũng sinh ra "click", nên chỉ tính là bấm khi chuột gần như không di chuyển.
let downX = 0;
let downY = 0;
let selectedMesh = null;   // kho nhỏ: mesh đang chọn (sáng lên)
let selectedSlot = null;   // khung đang chọn (cả hai chế độ)

// Kho lớn: bản sao trong InstancedMesh không sáng riêng được → đặt một hộp viền vàng trùm lên khung đang chọn
const selectionBox = new THREE.Mesh(UNIT_BOX, new THREE.MeshBasicMaterial({ color: '#f59e0b', wireframe: true }));
selectionBox.visible = false;
scene.add(selectionBox);

function clearSelection() {
  if (selectedMesh) selectedMesh.material.emissive.setHex(0x000000);
  selectedMesh = null;
  selectedSlot = null;
  selectionBox.visible = false;
  slotInfo.textContent = '';
}

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

  clearSelection();
  if (hits.length === 0) return;

  const picked = clickable.find((c) => c.mesh === hits[0].object);
  let slot;
  if (picked.slots) {                                   // InstancedMesh: bản sao thứ instanceId ↔ slots[instanceId]
    slot = picked.slots[hits[0].instanceId];
    const rack = racksById.get(slot.rackId);
    const f = frameBox(rack, slot);
    selectionBox.position.set(f.x, f.y, f.z);
    selectionBox.scale.set(f.sx, Math.max(f.sy, 0.05), f.sz);
    selectionBox.visible = true;
  } else {
    slot = picked.slot;
    selectedMesh = picked.mesh;
    selectedMesh.material.emissive.setHex(0x553300);   // sáng lên cho biết đang chọn
  }
  selectedSlot = slot;
  slotInfo.textContent = slot.palletId != null
    ? `${slot.code}${slot.isTop ? ' (nóc)' : ''}: pallet ${slot.palletCode} · type ${slot.typeCode} · ${slot.productTypeName ?? 'Chưa phân loại'}`
    : `${slot.code}${slot.isTop ? ' (nóc)' : ''}: khung trống`;
});

// =============== 6. HIỆU ỨNG PALLET BAY ===============
// Danh sách pallet vừa xếp do trang Nhập lô cất vào sessionStorage (theo ĐÚNG thứ tự server đã xếp).
// Vị trí cuối của mỗi pallet chính là chỗ đã vẽ ở bước 3, nên không có toạ độ riêng của hiệu ứng.

// =============== THEO DÕI XẾP NGẦM ===============
// Trang Nhập lô cất { batchId, code } vào "watch:{roomId}" rồi mở trang này ngay (không chờ xếp xong).
// Hỏi tiến độ mỗi 3 giây; số pallet đã xếp tăng thì tải lại pallet của vùng đang nhìn (kho lớn).
// Kho nhỏ: xếp xong mới tải lại cả trang một lần.
const WATCH_EVERY = 3000;   // ms
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function watchAllocation() {
  const text = readSession(`watch:${roomId}`);
  if (!text) return;
  const { batchId, code } = JSON.parse(text);
  let lastPlaced = -1;
  let failures = 0;
  for (;;) {
    let job;
    try {
      job = await getJson(`/api/rooms/${roomId}/batches/${batchId}/allocation`);
      failures = 0;
    } catch (err) {
      if (++failures >= 10) {               // hỏi 10 lần liền không được: thôi theo dõi
        removeSession(`watch:${roomId}`);
        showMessage(err.message, true);
        return;
      }
      await sleep(WATCH_EVERY);
      continue;
    }

    if (job.status === 'Done' || job.status === 'Failed') {
      removeSession(`watch:${roomId}`);
      const note = job.status === 'Failed'
        ? job.error
        : `Lô ${code}: đã xếp ${job.placed} pallet${job.rejectedCount ? `, ${job.rejectedCount} pallet chưa có khung` : ''}.`;
      if (!lazy) {                          // kho nhỏ: tải lại cả trang để vẽ lại từ dữ liệu mới
        try { sessionStorage.setItem(`note:${roomId}`, note); } catch { /* bỏ qua */ }
        location.reload();
        return;
      }
      await reloadVisiblePallets();
      showMessage(note, job.status === 'Failed');
      return;
    }

    showMessage(job.status === 'Queued'
      ? `Lô ${code} đang chờ tới lượt (đang tạo pallet hoặc lô khác đang xếp)…`
      : `Đang xếp lô ${code}: ${job.placed} / ${job.total} pallet… (cảnh tự cập nhật)`);
    if (lazy && job.placed !== lastPlaced) {
      lastPlaced = job.placed;
      await reloadVisiblePallets();
    }
    await sleep(WATCH_EVERY);
  }
}

// Kho lớn: hỏi lại khung của vùng đang nhìn; khung nào vừa có pallet thì CHỈ thêm khối pallet đó
// (không gỡ / vẽ lại kệ → không nháy, không phải dựng lại hàng nghìn hộp)
async function reloadVisiblePallets() {
  const r = viewArea();
  const area = `x1=${r.x1.toFixed(2)}&y1=${r.y1.toFixed(2)}&x2=${r.x2.toFixed(2)}&y2=${r.y2.toFixed(2)}`;
  const fresh = await getJson(`/api/room3d/${roomId}/slots?${area}`);
  let added = 0;
  for (const slot of fresh) {
    if (slot.palletId == null) continue;
    const state = rackGroups.get(slot.rackId);
    const known = state?.slotById.get(slot.id);
    if (!known || known.palletId != null) continue;      // kệ chưa vẽ (sẽ vẽ khi lọt vào vùng) hoặc đã có pallet rồi
    Object.assign(known, slot);                           // cập nhật dữ liệu khung (bấm vào thấy pallet mới, đếm đúng)
    addPalletInstance(state, known);
    added++;
  }
  if (added > 0) drawSummary();
}

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
