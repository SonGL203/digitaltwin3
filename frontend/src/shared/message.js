// Hiện một dòng thông báo ở thanh dưới cùng của trang (thẻ có id="status-message").
// isError = true thì chữ màu đỏ.
export function showMessage(text, isError = false) {
  const element = document.getElementById('status-message');
  if (!element) return;
  element.textContent = text;
  element.className = isError ? 'error' : '';
}
