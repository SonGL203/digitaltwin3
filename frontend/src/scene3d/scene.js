import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';

// Khung Three.js tối thiểu: cảnh, camera, ánh sáng, sàn, OrbitControls.
export function createScene(container) {
  const scene = new THREE.Scene();
  scene.background = new THREE.Color(0xeef1f5);

  const camera = new THREE.PerspectiveCamera(50, 1, 0.1, 500);
  camera.position.set(12, 10, 12);

  const renderer = new THREE.WebGLRenderer({ antialias: true });
  renderer.setPixelRatio(window.devicePixelRatio);
  container.appendChild(renderer.domElement);

  const controls = new OrbitControls(camera, renderer.domElement);
  controls.target.set(0, 0, 0);

  scene.add(new THREE.HemisphereLight(0xffffff, 0x8899aa, 1.2));
  const sun = new THREE.DirectionalLight(0xffffff, 1.5);
  sun.position.set(10, 20, 8);
  scene.add(sun);

  scene.add(new THREE.GridHelper(20, 20, 0x99a3b3, 0xd0d5dd));

  function resize() {
    const { clientWidth: w, clientHeight: h } = container;
    renderer.setSize(w, h);
    camera.aspect = w / h;
    camera.updateProjectionMatrix();
  }
  new ResizeObserver(resize).observe(container);
  resize();

  renderer.setAnimationLoop(() => {
    controls.update();
    renderer.render(scene, camera);
  });

  return { scene, camera, renderer, controls };
}
