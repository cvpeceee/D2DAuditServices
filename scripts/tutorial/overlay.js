// Motion-graphics overlay injected into the demo app while recording the tutorial.
// Everything lives outside #root so app layout is untouched; zoom transforms #root only.
(() => {
  if (window.__tut) return;
  const css = `
  #tut-layer{position:fixed;inset:0;pointer-events:none;z-index:2147483646;font-family:"Segoe UI",Inter,system-ui,sans-serif}
  #tut-cursor{position:fixed;left:0;top:0;width:30px;height:30px;transform:translate(960px,600px);pointer-events:none;
    transition:transform 700ms cubic-bezier(.22,.8,.24,1);z-index:2147483647;filter:drop-shadow(0 3px 6px rgba(0,0,0,.35))}
  #tut-cursor.down svg{transform:scale(.86);transform-origin:4px 3px}
  .tut-ripple{position:fixed;width:18px;height:18px;margin:-9px 0 0 -9px;border-radius:50%;
    border:3px solid #7c5cff;background:rgba(124,92,255,.25);animation:tutRipple 650ms ease-out forwards}
  @keyframes tutRipple{to{transform:scale(4.2);opacity:0}}
  .tut-box{position:fixed;border:3px solid #ffb020;border-radius:12px;box-shadow:0 0 0 4px rgba(255,176,32,.25),0 10px 30px rgba(0,0,0,.18);
    opacity:0;transform:scale(1.06);transition:opacity 320ms ease,transform 380ms cubic-bezier(.2,.9,.3,1.2),left 300ms,top 300ms,width 300ms,height 300ms}
  .tut-box.on{opacity:1;transform:scale(1)}
  .tut-label{position:absolute;left:-3px;top:-46px;white-space:nowrap;background:#1d1640;color:#fff;font-weight:600;font-size:20px;
    padding:7px 14px;border-radius:10px;box-shadow:0 8px 20px rgba(0,0,0,.25)}
  .tut-label.below{top:auto;bottom:-48px}
  .tut-label .n{display:inline-grid;place-items:center;width:26px;height:26px;border-radius:50%;background:#ffb020;color:#1d1640;margin-right:8px;font-size:16px}
  #tut-spot{position:fixed;border-radius:14px;box-shadow:0 0 0 200vmax rgba(10,8,30,.55);opacity:0;transition:opacity 400ms ease,left 400ms,top 400ms,width 400ms,height 400ms}
  #tut-spot.on{opacity:1}
  #tut-lower{position:fixed;left:56px;bottom:64px;display:flex;align-items:stretch;opacity:0;transform:translateX(-40px);
    transition:opacity 450ms ease,transform 550ms cubic-bezier(.2,.9,.3,1)}
  #tut-lower.on{opacity:1;transform:none}
  #tut-lower .bar{width:8px;border-radius:4px;background:linear-gradient(#7c5cff,#22d3ee);margin-right:14px}
  #tut-lower .txt{background:rgba(17,12,45,.92);color:#fff;padding:14px 24px;border-radius:12px;box-shadow:0 14px 40px rgba(0,0,0,.3)}
  #tut-lower .k{font-size:15px;letter-spacing:.14em;text-transform:uppercase;color:#a9a0ff;font-weight:700}
  #tut-lower .t{font-size:30px;font-weight:700;margin-top:2px}
  #root{transition:transform 900ms cubic-bezier(.45,.05,.25,1);transform-origin:0 0;will-change:transform}
  html.tut-rec ::-webkit-scrollbar{width:0;height:0}
  html.tut-rec *{caret-color:auto}
  `;
  const svg = `<svg width="30" height="30" viewBox="0 0 30 30"><path d="M4 3 L4 25 L10 19.5 L14.2 28 L18.2 26.2 L14 17.8 L22 17.8 Z"
    fill="#fff" stroke="#111" stroke-width="1.8" stroke-linejoin="round"/></svg>`;
  let layer, cursor, spot, lower;
  const boxes = [];
  let pos = { x: 960, y: 600 };
  let zoomState = { s: 1, tx: 0, ty: 0 };

  function ensure() {
    if (layer && document.documentElement.contains(layer)) return;
    const style = document.createElement("style");
    style.textContent = css;
    document.documentElement.appendChild(style);
    document.documentElement.classList.add("tut-rec");
    layer = document.createElement("div");
    layer.id = "tut-layer";
    cursor = document.createElement("div");
    cursor.id = "tut-cursor";
    cursor.innerHTML = svg;
    spot = document.createElement("div");
    spot.id = "tut-spot";
    lower = document.createElement("div");
    lower.id = "tut-lower";
    lower.innerHTML = `<div class="bar"></div><div class="txt"><div class="k"></div><div class="t"></div></div>`;
    layer.append(spot, lower);
    document.documentElement.append(layer, cursor);
    cursor.style.transform = `translate(${pos.x}px,${pos.y}px)`;
  }

  function trackRect(el, node, pad) {
    let alive = true;
    const tick = () => {
      if (!alive) return;
      if (el && el.isConnected) {
        const r = el.getBoundingClientRect();
        node.style.left = `${r.left - pad}px`;
        node.style.top = `${r.top - pad}px`;
        node.style.width = `${r.width + pad * 2}px`;
        node.style.height = `${r.height + pad * 2}px`;
      }
      requestAnimationFrame(tick);
    };
    tick();
    return () => (alive = false);
  }

  window.__tut = {
    ensure,
    move(x, y, ms = 700) {
      ensure();
      pos = { x, y };
      cursor.style.transitionDuration = `${ms}ms`;
      cursor.style.transform = `translate(${x - 4}px,${y - 3}px)`;
    },
    ripple(x, y) {
      ensure();
      const r = document.createElement("div");
      r.className = "tut-ripple";
      r.style.left = `${x}px`;
      r.style.top = `${y}px`;
      layer.appendChild(r);
      cursor.classList.add("down");
      setTimeout(() => cursor.classList.remove("down"), 160);
      setTimeout(() => r.remove(), 800);
    },
    highlight(el, label, opts = {}) {
      ensure();
      if (!el) return false;
      const b = document.createElement("div");
      b.className = "tut-box";
      if (opts.color) b.style.borderColor = opts.color;
      if (label) {
        const l = document.createElement("div");
        const r = el.getBoundingClientRect();
        l.className = "tut-label" + (r.top < 90 || opts.below ? " below" : "");
        l.innerHTML = (opts.n ? `<span class="n">${opts.n}</span>` : "") + label;
        b.appendChild(l);
      }
      layer.appendChild(b);
      const stop = trackRect(el, b, opts.pad ?? 8);
      requestAnimationFrame(() => requestAnimationFrame(() => b.classList.add("on")));
      boxes.push({ b, stop });
      return true;
    },
    spotlight(el, pad = 14) {
      ensure();
      if (!el) return false;
      if (spot._stop) spot._stop();
      spot._stop = trackRect(el, spot, pad);
      spot.classList.add("on");
      return true;
    },
    clear() {
      boxes.splice(0).forEach(({ b, stop }) => {
        b.classList.remove("on");
        setTimeout(() => {
          stop();
          b.remove();
        }, 400);
      });
      if (spot) spot.classList.remove("on");
    },
    lowerThird(kicker, title, ms = 3800) {
      ensure();
      lower.querySelector(".k").textContent = kicker || "";
      lower.querySelector(".t").textContent = title || "";
      lower.classList.add("on");
      clearTimeout(lower._t);
      if (ms > 0) lower._t = setTimeout(() => lower.classList.remove("on"), ms);
    },
    // Zoom so the element's centre lands at the viewport centre, clamped to keep the page edge-to-edge.
    zoom(el, s = 1.6, ms = 900) {
      const root = document.getElementById("root");
      if (!root || !el) return false;
      root.style.transitionDuration = `${ms}ms`;
      root.style.transform = "none";
      const R = root.getBoundingClientRect();
      const r = el.getBoundingClientRect();
      const W = innerWidth, H = innerHeight;
      const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
      let tx = W / 2 - R.left - s * (cx - R.left);
      let ty = H / 2 - R.top - s * (cy - R.top);
      const minX = (W - R.left) * (1 - s), maxX = R.left * (s - 1);
      const minY = (H - R.top) * (1 - s), maxY = R.top * (s - 1);
      tx = Math.min(maxX, Math.max(minX, tx));
      ty = Math.min(maxY, Math.max(minY, ty));
      root.getBoundingClientRect();
      root.style.transform = `translate(${tx}px,${ty}px) scale(${s})`;
      zoomState = { s, tx, ty };
      return true;
    },
    unzoom(ms = 800) {
      const root = document.getElementById("root");
      if (!root) return;
      root.style.transitionDuration = `${ms}ms`;
      root.style.transform = "none";
      zoomState = { s: 1, tx: 0, ty: 0 };
    },
    get zoomed() {
      return zoomState.s !== 1;
    },
  };
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", ensure);
  else ensure();
})();
