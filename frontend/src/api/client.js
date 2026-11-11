// Mọi lời gọi backend đi qua đây. Frontend không chứa luật nghiệp vụ: kiểm tra đặt kệ, sinh khung,
// tính footprint… đều do server làm; frontend chỉ gửi dữ liệu và vẽ kết quả.

/** Lỗi từ API, mang theo mã HTTP và câu lý do ("detail" của ProblemDetails) để hiện thẳng cho người dùng. */
export class ApiError extends Error {
  constructor(status, message) {
    super(message);
    this.status = status;
  }
}

async function request(method, path, body) {
  let res;
  try {
    res = await fetch(`/api${path}`, {
      method,
      headers: body ? { 'Content-Type': 'application/json' } : undefined,
      body: body ? JSON.stringify(body) : undefined,
    });
  } catch {
    throw new ApiError(0, 'Không kết nối được máy chủ.');
  }

  if (!res.ok) {
    // Lỗi nghiệp vụ là ProblemDetails: { status, detail }. Lỗi [Range] của ASP.NET có thêm "errors".
    let message = `${method} /api${path} → ${res.status}`;
    try {
      const problem = await res.json();
      if (problem.detail) message = problem.detail;
      else if (problem.errors) message = Object.values(problem.errors).flat().join(' ');
      else if (problem.title) message = problem.title;
    } catch { /* body không phải JSON: giữ câu mặc định */ }
    throw new ApiError(res.status, message);
  }

  return res.status === 204 ? null : res.json();
}

export const api = {
  health: () => request('GET', '/health'),

  // ---- mặt bằng (mọi số đo bằng mét) ----
  /** Phòng + kệ (có footprintWidth/footprintDepth, slotCount) + tường/cột. 404 nếu chưa có phòng. */
  getLayout: () => request('GET', '/layout'),
  createRoom: (room) => request('POST', '/layout/room', room),
  updateRoom: (room) => request('PUT', '/layout/room', room),

  addObstacle: (obstacle) => request('POST', '/layout/obstacles', obstacle),
  removeObstacle: (id) => request('DELETE', `/layout/obstacles/${id}`),

  /** Kiểm tra thử (bóng mờ): luôn 200 nếu có phòng, xem isValid và reason. Không lưu gì. */
  validateRack: (rack) => request('POST', '/layout/racks/validate', rack),
  addRack: (rack) => request('POST', '/layout/racks', rack),
  updateRack: (id, rack) => request('PUT', `/layout/racks/${id}`, rack),
};
