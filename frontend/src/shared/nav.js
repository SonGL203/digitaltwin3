// Thanh điều hướng dùng chung. Mỗi trang gọi hàm này một lần khi mở.
// Chuyển màn bằng thẻ <a> (link) bình thường: bấm là sang trang HTML khác.

function link(href, text, isActive) {
  return `<a href="${href}"${isActive ? ' class="active"' : ''}>${text}</a>`;
}

// Thanh trên cùng. Nhập lô không nằm ở đây: phải vào một kho rồi mới nhập (tab trong kho).
export function showTopBar(active) {
  document.getElementById('topbar').innerHTML = `
    <strong class="brand">WarehouseTwin</strong>
    <nav class="tabs">
      ${link('index.html', 'Kho hàng (mặt bằng, 3D, nhập lô)', active === 'rooms')}
    </nav>
  `;
}

// Ba tab trong một kho: active = 'layout' (Mặt bằng), 'warehouse3d' (Kho 3D) hoặc 'receive' (Nhập lô)
export function showRoomTabs(roomId, active) {
  document.getElementById('roomTabs').innerHTML = `
    ${link(`layout.html?roomId=${roomId}`, 'Mặt bằng', active === 'layout')}
    ${link(`warehouse3d.html?roomId=${roomId}`, 'Kho 3D', active === 'warehouse3d')}
    ${link(`receive.html?roomId=${roomId}`, 'Nhập lô', active === 'receive')}
  `;
}

// Đọc roomId từ địa chỉ trang: layout.html?roomId=3 → 3. Không có thì trả null.
export function readRoomId() {
  const text = new URLSearchParams(location.search).get('roomId');
  const id = Number(text);
  return text && Number.isInteger(id) && id > 0 ? id : null;
}
