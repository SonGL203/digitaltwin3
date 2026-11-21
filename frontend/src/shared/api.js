// Hàm gọi API dùng chung cho mọi trang. Lỗi thì ném Error có sẵn câu lý do để hiện cho người dùng.

// Đọc câu lý do từ thân lỗi của backend (ProblemDetails: { detail }, hoặc lỗi [Range] có "errors").
async function readError(response, method, url) {
  let message = `${method} ${url} → lỗi ${response.status}`;
  try {
    const problem = await response.json();
    if (problem.detail) message = problem.detail;
    else if (problem.errors) message = Object.values(problem.errors).flat().join(' ');
    else if (problem.title) message = problem.title;
  } catch {
    // thân lỗi không phải JSON: giữ câu mặc định
  }
  return new Error(message);
}

// GET: trả về dữ liệu JSON
export async function getJson(url) {
  let response;
  try {
    response = await fetch(url);
  } catch {
    throw new Error('Không kết nối được máy chủ.');
  }
  if (!response.ok) throw await readError(response, 'GET', url);
  return response.json();
}

// POST / PUT / DELETE: gửi `body` dạng JSON (có thể bỏ trống), trả về JSON (hoặc null nếu server không trả gì)
export async function sendJson(method, url, body) {
  let response;
  try {
    response = await fetch(url, {
      method: method,
      headers: body ? { 'Content-Type': 'application/json' } : undefined,
      body: body ? JSON.stringify(body) : undefined,
    });
  } catch {
    throw new Error('Không kết nối được máy chủ.');
  }
  if (!response.ok) throw await readError(response, method, url);
  return response.status === 204 ? null : response.json();
}
