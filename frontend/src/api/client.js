// Mọi lời gọi backend đi qua đây. Frontend không chứa luật nghiệp vụ.
async function request(method, path, body) {
  const res = await fetch(`/api${path}`, {
    method,
    headers: body ? { 'Content-Type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!res.ok) throw new Error(`${method} /api${path} → ${res.status}`);
  return res.status === 204 ? null : res.json();
}

export const api = {
  health: () => request('GET', '/health'),
};
