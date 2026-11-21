// Hiển thị số theo kiểu Việt Nam: 1,2 m; 2 000. Chỉ để hiện ra màn hình, không dùng để tính.
const meters = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 2 });
const integer = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 });

export const formatMeters = (value) => meters.format(value);
export const formatInt = (value) => integer.format(value);

// Chữ do người dùng gõ (tên kho, tên loại hàng) phải đi qua hàm này trước khi chèn vào chuỗi HTML.
// Nếu không, tên như <img onerror=...> sẽ bị trình duyệt chạy như code.
export function escapeHtml(text) {
  return String(text).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
}
