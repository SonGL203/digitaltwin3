import { api } from './api/client.js';
import { createScene } from './scene3d/scene.js';

createScene(document.getElementById('viewport'));

const status = document.getElementById('api-status');
api.health()
  .then(() => { status.textContent = 'API: kết nối được'; status.className = 'ok'; })
  .catch((err) => { status.textContent = `API lỗi: ${err.message}`; status.className = 'error'; });
