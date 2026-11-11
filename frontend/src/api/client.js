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

  // ---- phòng và mặt bằng (mọi số đo bằng mét). Kệ, tường, cột đều nằm dưới /rooms/{roomId} ----
  /** Danh sách phòng: { id, name, width, depth, gridSize, rackCount, obstacleCount, slotCount }. */
  listRooms: () => request('GET', '/rooms'),
  /** Tạo phòng → { id }. Tên trùng thì 409. */
  createRoom: (room) => request('POST', '/rooms', room),
  /** Một phòng + kệ (có footprintWidth/footprintDepth, slotCount) + tường/cột. 404 nếu không có. */
  getRoom: (roomId) => request('GET', `/rooms/${roomId}`),
  updateRoom: (roomId, room) => request('PUT', `/rooms/${roomId}`, room),

  addObstacle: (roomId, obstacle) => request('POST', `/rooms/${roomId}/obstacles`, obstacle),
  removeObstacle: (roomId, id) => request('DELETE', `/rooms/${roomId}/obstacles/${id}`),

  /** Kiểm tra thử (bóng mờ): luôn 200 nếu có phòng, xem isValid và reason. Không lưu gì. */
  validateRack: (roomId, rack) => request('POST', `/rooms/${roomId}/racks/validate`, rack),
  addRack: (roomId, rack) => request('POST', `/rooms/${roomId}/racks`, rack),
  updateRack: (roomId, id, rack) => request('PUT', `/rooms/${roomId}/racks/${id}`, rack),
  /** Xoá kệ và mọi khung của nó. Kệ đang có hàng thì 409. */
  removeRack: (roomId, id) => request('DELETE', `/rooms/${roomId}/racks/${id}`),
};
