// Sinh các kho 3D (.glb) để test chức năng "Số hoá kho cũ" (ui/robotDigitize.js).
// Chạy: node test-models/generate.mjs   (trong thư mục frontend)
// Không cần thư viện: tự dựng hình hộp/trụ và tự ghi file GLB.
// Quy ước giống kho mẫu (robot/sampleWarehouse.js): mét, Y hướng lên, sàn y = 0, góc phòng (0; 0),
// X mô hình = X phòng, Z mô hình = Y phòng. Kèm <tên>.truth.json là đáp án để so với kết quả robot.

import { writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const OUT = dirname(fileURLToPath(import.meta.url));

// ---------- dựng hình ----------

function createModel() {
  const byColor = new Map();   // màu → { positions, normals, indices }
  const part = (color) => {
    if (!byColor.has(color)) byColor.set(color, { positions: [], normals: [], indices: [] });
    return byColor.get(color);
  };

  // Biến đổi điểm cục bộ: xoay quanh Y (góc độ, chiều như sampleWarehouse: rotation.y = -deg) rồi dời.
  const transform = (frame, [x, y, z]) => {
    const a = -(frame.angle ?? 0) * Math.PI / 180;
    const c = Math.cos(a), s = Math.sin(a);
    return [frame.x + x * c + z * s, y, frame.z - x * s + z * c];
  };
  const rotateNormal = (frame, [x, y, z]) => {
    const a = -(frame.angle ?? 0) * Math.PI / 180;
    const c = Math.cos(a), s = Math.sin(a);
    return [x * c + z * s, y, -x * s + z * c];
  };

  /** Hộp w × h × d, tâm (cx, cy, cz) trong hệ toạ độ frame. */
  function box(color, w, h, d, cx, cy, cz, frame = { x: 0, z: 0, angle: 0 }) {
    const p = part(color);
    const faces = [
      [[1, 0, 0], [[1, -1, -1], [1, 1, -1], [1, 1, 1], [1, -1, 1]]],
      [[-1, 0, 0], [[-1, -1, 1], [-1, 1, 1], [-1, 1, -1], [-1, -1, -1]]],
      [[0, 1, 0], [[-1, 1, -1], [-1, 1, 1], [1, 1, 1], [1, 1, -1]]],
      [[0, -1, 0], [[-1, -1, 1], [-1, -1, -1], [1, -1, -1], [1, -1, 1]]],
      [[0, 0, 1], [[1, -1, 1], [1, 1, 1], [-1, 1, 1], [-1, -1, 1]]],
      [[0, 0, -1], [[-1, -1, -1], [-1, 1, -1], [1, 1, -1], [1, -1, -1]]],
    ];
    for (const [n, corners] of faces) {
      const base = p.positions.length / 3;
      for (const [sx, sy, sz] of corners) {
        p.positions.push(...transform(frame, [cx + sx * w / 2, cy + sy * h / 2, cz + sz * d / 2]));
        p.normals.push(...rotateNormal(frame, n));
      }
      p.indices.push(base, base + 1, base + 2, base, base + 2, base + 3);
    }
  }

  /** Trụ tròn đứng: bán kính r, cao h, tâm đáy (x, z). */
  function cylinder(color, r, h, x, z, segments = 24) {
    const p = part(color);
    for (let i = 0; i < segments; i++) {
      const a0 = (i / segments) * Math.PI * 2, a1 = ((i + 1) / segments) * Math.PI * 2;
      const [c0, s0, c1, s1] = [Math.cos(a0), Math.sin(a0), Math.cos(a1), Math.sin(a1)];
      // mặt bên
      let base = p.positions.length / 3;
      p.positions.push(x + r * c0, 0, z + r * s0, x + r * c1, 0, z + r * s1, x + r * c1, h, z + r * s1, x + r * c0, h, z + r * s0);
      p.normals.push(c0, 0, s0, c1, 0, s1, c1, 0, s1, c0, 0, s0);
      p.indices.push(base, base + 2, base + 1, base, base + 3, base + 2);
      // nắp trên
      base = p.positions.length / 3;
      p.positions.push(x, h, z, x + r * c1, h, z + r * s1, x + r * c0, h, z + r * s0);
      p.normals.push(0, 1, 0, 0, 1, 0, 0, 1, 0);
      p.indices.push(base, base + 1, base + 2);
    }
  }

  return { box, cylinder, byColor };
}

/** Một kho: sàn + tường bao + các thứ thêm qua api. Trả { model, truth }. */
function warehouse(width, depth) {
  const m = createModel();
  const truth = { width, depth, racks: [], columns: [], walls: [], clutter: [] };
  m.box('#f1efe8', width, 0.02, depth, width / 2, -0.01, depth / 2);          // sàn
  m.box('#b4b2a9', width, 3, 0.2, width / 2, 1.5, 0.1);                       // tường bao
  m.box('#b4b2a9', width, 3, 0.2, width / 2, 1.5, depth - 0.1);
  m.box('#b4b2a9', 0.2, 3, depth, 0.1, 1.5, depth / 2);
  m.box('#b4b2a9', 0.2, 3, depth, width - 0.1, 1.5, depth / 2);

  const api = {
    truth,
    model: m,
    /** Kệ giống sampleWarehouse: tấm ngang mỗi tầng + nóc, chân ở mỗi ranh giới ô. fill: tỉ lệ khung có thùng hàng. */
    rack(code, cx, cz, bays, bayWidth, rackDepth, levels, levelHeight, angle = 0, fill = 0) {
      const frame = { x: cx, z: cz, angle };
      const length = bays * bayWidth;
      for (let k = 0; k <= levels; k++) m.box('#85b7eb', length, 0.05, rackDepth, 0, 0.1 + k * levelHeight, 0, frame);
      const topY = 0.1 + levels * levelHeight + 0.025;
      for (let i = 0; i <= bays; i++) {
        for (const s of [-1, 1]) m.box('#185fa5', 0.06, topY, 0.06, -length / 2 + i * bayWidth, topY / 2, s * rackDepth / 2, frame);
      }
      // Thùng hàng trên kệ (tất định: theo vị trí ô, không dùng Math.random).
      let n = 0;
      for (let k = 0; k < levels; k++) {
        for (let b = 0; b < bays; b++) {
          n++;
          if (((n * 37) % 100) / 100 >= fill) continue;
          const h = Math.min(levelHeight - 0.25, 0.8);
          m.box('#ba7517', bayWidth - 0.25, h, rackDepth - 0.15,
            -length / 2 + (b + 0.5) * bayWidth, 0.125 + k * levelHeight + h / 2, 0, frame);
        }
      }
      truth.racks.push({ code, cx, cy: cz, length, depth: rackDepth, bays, levels, levelHeight, angle });
    },
    roundColumn(x, z, radius = 0.25) {
      m.cylinder('#888780', radius, 3, x, z);
      truth.columns.push({ x, y: z, radius });
    },
    squareColumn(x, z, size = 0.4) {
      m.box('#888780', size, 3, size, x, 1.5, z);
      truth.columns.push({ x: x - size / 2, y: z - size / 2, width: size, depth: size });
    },
    /** Tường ngăn: (x, z) góc trên trái, w theo X, d theo Z. */
    wall(x, z, w, d) {
      m.box('#888780', w, 3, d, x + w / 2, 1.5, z + d / 2);
      truth.walls.push({ x, y: z, width: w, depth: d });
    },
    clutter(x, z, w, d, h) {
      m.box('#ba7517', w, h, d, x, h / 2, z);
      truth.clutter.push({ x, y: z, width: w, depth: d, height: h });
    },
  };
  return api;
}

// ---------- ghi GLB ----------

function hexToRgba(hex) {
  const v = parseInt(hex.slice(1), 16);
  return [((v >> 16) & 255) / 255, ((v >> 8) & 255) / 255, (v & 255) / 255, 1];
}

function toGlb(model) {
  const json = {
    asset: { version: '2.0', generator: 'warehouse test-models' },
    scene: 0, scenes: [{ nodes: [] }], nodes: [], meshes: [], materials: [],
    accessors: [], bufferViews: [], buffers: [{ byteLength: 0 }],
  };
  const chunks = [];
  let offset = 0;
  const addView = (typed, target) => {
    const bytes = Buffer.from(typed.buffer, typed.byteOffset, typed.byteLength);
    chunks.push(bytes);
    json.bufferViews.push({ buffer: 0, byteOffset: offset, byteLength: bytes.length, target });
    offset += bytes.length;   // mọi mảng là 4 byte/phần tử nên luôn thẳng hàng 4
    return json.bufferViews.length - 1;
  };

  for (const [color, p] of model.byColor) {
    const pos = new Float32Array(p.positions);
    const min = [Infinity, Infinity, Infinity], max = [-Infinity, -Infinity, -Infinity];
    for (let i = 0; i < pos.length; i += 3) {
      for (let k = 0; k < 3; k++) { min[k] = Math.min(min[k], pos[i + k]); max[k] = Math.max(max[k], pos[i + k]); }
    }
    const count = pos.length / 3;
    json.accessors.push({ bufferView: addView(pos, 34962), componentType: 5126, count, type: 'VEC3', min, max });
    const posAcc = json.accessors.length - 1;
    json.accessors.push({ bufferView: addView(new Float32Array(p.normals), 34962), componentType: 5126, count, type: 'VEC3' });
    const normAcc = json.accessors.length - 1;
    json.accessors.push({ bufferView: addView(new Uint32Array(p.indices), 34963), componentType: 5125, count: p.indices.length, type: 'SCALAR' });
    const idxAcc = json.accessors.length - 1;

    json.materials.push({ pbrMetallicRoughness: { baseColorFactor: hexToRgba(color), metallicFactor: 0, roughnessFactor: 0.9 } });
    json.meshes.push({ primitives: [{ attributes: { POSITION: posAcc, NORMAL: normAcc }, indices: idxAcc, material: json.materials.length - 1 }] });
    json.nodes.push({ mesh: json.meshes.length - 1 });
    json.scenes[0].nodes.push(json.nodes.length - 1);
  }
  json.buffers[0].byteLength = offset;

  let jsonBytes = Buffer.from(JSON.stringify(json));
  jsonBytes = Buffer.concat([jsonBytes, Buffer.alloc((4 - (jsonBytes.length % 4)) % 4, 0x20)]);
  const bin = Buffer.concat(chunks);
  const header = Buffer.alloc(12);
  header.writeUInt32LE(0x46546c67, 0);   // 'glTF'
  header.writeUInt32LE(2, 4);
  header.writeUInt32LE(12 + 8 + jsonBytes.length + 8 + bin.length, 8);
  const chunkHead = (len, type) => { const b = Buffer.alloc(8); b.writeUInt32LE(len, 0); b.writeUInt32LE(type, 4); return b; };
  return Buffer.concat([header, chunkHead(jsonBytes.length, 0x4e4f534a), jsonBytes, chunkHead(bin.length, 0x004e4942), bin]);
}

function save(name, w, note) {
  writeFileSync(join(OUT, `${name}.glb`), toGlb(w.model));
  writeFileSync(join(OUT, `${name}.truth.json`), JSON.stringify({ note, ...w.truth }, null, 2));
  console.log(`${name}.glb  (${w.truth.racks.length} kệ, ${w.truth.columns.length} cột, ${w.truth.walls.length} tường ngăn, ${w.truth.clutter.length} vật cản)`);
}

// ---------- các kho test ----------

// 1. Dễ nhất: phòng nhỏ, 3 kệ thẳng hàng, lối đi rộng, không vật cản.
{
  const w = warehouse(12, 8);
  w.rack('K1', 3.5, 2, 4, 1.2, 1, 3, 1.2);
  w.rack('K2', 3.5, 5, 4, 1.2, 1, 2, 1.5);
  w.rack('K3', 9, 3.5, 3, 1.2, 1, 4, 1.0, 90);
  save('01-kho-nho-co-ban', w, 'Cơ bản: 3 kệ, một kệ xoay 90°, lối đi rộng. Robot phải ra đủ 3 kệ với đúng số tầng 3, 2, 4.');
}

// 2. Có cột vuông, cột tròn, tường ngăn; kệ có hàng trên các tầng (dễ đếm nhầm tầng vì mặt thùng cũng nằm ngang).
{
  const w = warehouse(16, 10);
  w.rack('K1', 4, 2, 4, 1.2, 1, 3, 1.2, 0, 0.6);
  w.rack('K2', 11, 2, 4, 1.2, 1, 3, 1.2, 0, 0.6);
  w.rack('K3', 4, 5.5, 4, 1.2, 1, 2, 1.6, 0, 0.8);
  w.rack('K4', 11, 5.5, 4, 1.2, 1, 4, 1.0, 0, 0.3);
  w.squareColumn(8, 3.8);
  w.roundColumn(8, 7.5);
  w.wall(0.2, 8, 5, 0.2);
  save('02-kho-co-hang-va-cot', w, 'Kệ có thùng hàng trên tầng (mặt thùng không được tính là tầng), cột vuông, cột tròn, một tường ngăn.');
}

// 3. Kho lớn: 3 dãy × 4 kệ, nhiều cột theo lưới nhà xưởng. Test quãng đường quét dài.
{
  const w = warehouse(30, 20);
  let n = 1;
  for (const z of [3, 9, 15]) {
    for (const x of [4, 11, 18, 25]) w.rack(`K${n++}`, x, z, 4, 1.3, 1.1, 2 + (n % 3), 1.3, 0, 0.4);
  }
  for (const x of [7.5, 14.5, 21.5]) for (const z of [6, 12]) w.squareColumn(x, z, 0.5);
  save('03-kho-lon-nhieu-day', w, 'Kho lớn 30 × 20 m: 12 kệ thành 3 dãy, 6 cột theo lưới. Test quét đường dài và số kệ nhiều.');
}

// 4. Khó: kệ đặt chéo 30° và 45°, đồ vương vãi trên sàn (thấp, robot có thể lầm là kệ).
{
  const w = warehouse(18, 14);
  w.rack('K1', 4, 2.5, 4, 1.2, 1, 3, 1.2);
  w.rack('K2', 12, 3, 3, 1.3, 1, 3, 1.1, 30);
  w.rack('K3', 5, 9, 3, 1.2, 1, 2, 1.4, 45);
  w.rack('K4', 14, 10, 4, 1.2, 1, 3, 1.2, 90, 0.5);
  w.clutter(9, 7, 1.2, 1, 0.15);    // pallet rỗng
  w.clutter(2, 12.5, 0.6, 0.6, 0.5); // thùng lẻ
  w.roundColumn(9, 11, 0.3);
  save('04-kho-ke-cheo-va-do-vuong', w, 'Kệ chéo 30° và 45° (app chỉ có 0/90° nên phải báo hoặc làm tròn), pallet rỗng và thùng lẻ trên sàn (không phải kệ).');
}

// 5. Phòng hình chữ L (góc bị tường chiếm) + một lối đi hẹp 0,7 m mà robot (bán kính 0,35 m) không lọt.
{
  const w = warehouse(16, 12);
  w.wall(9, 0.2, 6.8, 5.8);          // khối tường lấp góc trên phải → phòng hình chữ L
  w.rack('K1', 3.5, 2.5, 4, 1.2, 1, 3, 1.2);
  w.rack('K2', 3.5, 7, 4, 1.2, 1, 3, 1.2);
  // K3 và K4 cách nhau 0,7 m: robot không đi qua khe này, phải vòng ngoài.
  w.rack('K3', 11, 8, 3, 1.2, 1, 2, 1.5);
  w.rack('K4', 11, 9.7, 3, 1.2, 1, 2, 1.5);
  save('05-kho-chu-L-loi-hep', w, 'Phòng chữ L (khối tường lấp một góc) và khe 0,7 m giữa K3, K4: robot không lọt, phải quét từ hai phía.');
}
