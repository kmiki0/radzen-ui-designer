// Radzen UI デザイナーの画面操作
// （キャンバスの移動と拡大縮小・選択・ドラッグ＆ドロップ・大きさの変更・キー操作・保存）
//
// キャンバスは無限に広がる面で、画面幅ごとのフレーム（.dz-frame）を並べる。
// 移動と拡大縮小は .dz-world の transform で行い、Blazor の描き直しは起こさない。
//
// フレームの中身は本物の Radzen の部品なので、クリックや入力をそのまま通すとボタンが押され、
// ドロップダウンが開き、リンクで別のページに移ってしまう。フレーム内のマウス操作はここで止め、
// 「部品を選ぶ」「部品を動かす」に置き換える（プレビュー中は止めない）。部品は data-dz-* 属性で見分ける。
//   data-dz-id        部品の Id（ルートは "root"）
//   data-dz-tag       部品のタグ名（RadzenButton）
//   data-dz-container 子を入れられる部品
//   data-dz-selected  選んでいる部品（フレームごとに 1 つずつある）
//   data-dz-frame     フレーム（値は画面の種類）
//   data-dz-palette   部品パネルの項目（値は部品の種類）
//   data-dz-tree      レイヤーの項目・選択ラベル（値は部品の Id）
//   data-dz-view      倍率のボタン（in / out / fit / reset）
//   data-dz-avoid     キャンバスの上に浮いているパネル。全体表示のときに避ける側（left / right / top / bottom）
//
// 選択枠とホバー枠は、部品を包まずに、上に重ねた .dz-overlay に描く。
// 選択枠を出すのは、最後に触ったフレーム（data-dz-active）だけにする。

const DRAG_THRESHOLD = 4;  // これ以上動いたらドラッグとみなす（px）
const MIN_SCALE = 0.1;
const MAX_SCALE = 3;
const GRID = 24;           // 背景の点の間隔（倍率 1 のとき）
const LABEL_SPACE = 30;    // フレームの上の名前の分（画面上の px）

const OVERLAY_HTML = `
  <div class="dz-ov-hover"><span class="dz-ov-chip"></span></div>
  <div class="dz-ov-sel">
    <div class="dz-ov-bar">
      <span class="dz-ov-chip" data-dz-move title="ドラッグで移動"><i class="rzi">open_with</i><b></b></span>
      <button type="button" data-dz-cmd="SelectParent" title="親を選ぶ"><i class="rzi">north_west</i></button>
      <button type="button" data-dz-cmd="Duplicate" title="複製（Ctrl+D）"><i class="rzi">content_copy</i></button>
      <button type="button" data-dz-cmd="Delete" title="削除（Delete）"><i class="rzi">delete</i></button>
    </div>
    <i class="dz-h dz-h-r" data-dz-resize="r" title="幅を変える"></i>
    <i class="dz-h dz-h-b" data-dz-resize="b" title="高さを変える"></i>
    <i class="dz-h dz-h-rb" data-dz-resize="rb" title="大きさを変える"></i>
  </div>`;

export function init(app, dotnet) {
  const view = { x: 0, y: 0, s: 1 };
  let drag = null;     // { source: "palette" | "node", key, label, x, y, fromCanvas, active, target }
  let resize = null;   // { dir, el, id, x, y, w, h, zoom, width, height }
  let pan = null;      // { x, y, vx, vy, moved, click }
  let split = null;    // ボトムバーの上の縁のドラッグ { y, h, max, value }
  let codeSel = null;  // コード欄でいま見せている選択（{ el: コード欄, id: 部品の Id }）
  let suppressClick = false;
  let hovered = null;
  let activeKey = null;
  let space = false;
  let frame = 0;

  const viewport = () => app.querySelector(".dz-viewport");
  const world = () => app.querySelector(".dz-world");
  const frames = () => [...app.querySelectorAll("[data-dz-frame]")];
  const frameOf = el => (el instanceof Element ? el.closest("[data-dz-frame]") : null);
  const screenOf = el => (el instanceof Element ? el.closest(".dz-screen") : null);
  const inCanvas = el => !!screenOf(el) && app.contains(el);
  const inViewport = el => el instanceof Element && !!viewport()?.contains(el);
  const isPreview = () => app.hasAttribute("data-preview");
  const activeFrame = () => { const all = frames(); return all.find(f => f.dataset.dzFrame === activeKey) ?? all[0] ?? null; };

  const indicator = document.createElement("div");
  indicator.className = "dz-indicator";
  document.body.appendChild(indicator);

  const ghost = document.createElement("div");
  ghost.className = "dz-ghost";
  document.body.appendChild(ghost);

  // ---- キャンバスの移動と拡大縮小
  function applyView() {
    const w = world();
    if (w) {
      w.style.transform = `translate(${view.x}px, ${view.y}px) scale(${view.s})`;
      w.style.setProperty("--dz-scale", String(view.s));
    }
    const dots = app.querySelector(".dz-dots");
    if (dots) {
      // 点はキャンバスと一緒に動かす。小さくしすぎると点が詰まるので、間隔を倍にする
      let g = GRID * view.s;
      while (g < 12) g *= 2;
      dots.style.backgroundSize = `${g}px ${g}px`;
      dots.style.backgroundPosition = `${view.x}px ${view.y}px`;
    }
    const label = app.querySelector("[data-dz-zoom]");
    if (label) label.textContent = Math.round(view.s * 100) + "%";
    refreshSoon();
  }

  function zoomAt(cx, cy, scale) {
    const s = Math.min(MAX_SCALE, Math.max(MIN_SCALE, scale));
    view.x = cx - (cx - view.x) * s / view.s;
    view.y = cy - (cy - view.y) * s / view.s;
    view.s = s;
    applyView();
  }

  function zoomCenter(scale) {
    const r = viewport().getBoundingClientRect();
    zoomAt(r.width / 2, r.height / 2, scale);
  }

  // 浮いているパネルにかからない範囲（ビューポートの中の座標）
  function freeArea() {
    const vr = viewport().getBoundingClientRect();
    let l = 24, r = 24, t = 24, b = 24;
    for (const el of app.querySelectorAll("[data-dz-avoid]")) {
      const p = el.getBoundingClientRect();
      if (p.width === 0 && p.height === 0) continue;
      switch (el.dataset.dzAvoid) {
        case "left": l = Math.max(l, p.right - vr.left + 24); break;
        case "right": r = Math.max(r, vr.right - p.left + 24); break;
        case "top": t = Math.max(t, p.bottom - vr.top + 16); break;
        case "bottom": b = Math.max(b, vr.bottom - p.top + 16); break;
      }
    }
    return { l, t, w: Math.max(120, vr.width - l - r), h: Math.max(120, vr.height - t - b) };
  }

  // 要素の位置と大きさを、倍率 1 のときの world の座標で返す
  function worldRect(el) {
    const wr = world().getBoundingClientRect();
    const r = el.getBoundingClientRect();
    return { x: (r.left - wr.left) / view.s, y: (r.top - wr.top) / view.s, w: r.width / view.s, h: r.height / view.s };
  }

  // 全体（または 1 つのフレーム）が、空いている範囲に収まるようにする
  function fit(target) {
    const w = world();
    if (!w || !viewport()) return;
    const box = worldRect(target ?? w);
    if (box.w === 0 || box.h === 0) return;
    const a = freeArea();
    const h = a.h - LABEL_SPACE;
    const s = Math.min(1, Math.max(MIN_SCALE, Math.min(a.w / box.w, h / box.h)));
    view.s = s;
    view.x = a.l + (a.w - box.w * s) / 2 - box.x * s;
    view.y = a.t + LABEL_SPACE + Math.max(0, (h - box.h * s) / 2) - box.y * s;
    applyView();
    fitMark = userMoves;
  }

  // 描き直した直後は、部品の見た目やアイコンの文字がまだ読み込み中で、大きさが変わることがある。
  // すぐに合わせたあと、少し待ってもう一度合わせる。その間に自分で拡大・移動したら、やり直さない
  let userMoves = 0;   // 自分で拡大・移動した回数
  let fitMark = -1;    // 最後に全体表示したときの userMoves（同じなら、全体表示のあと触っていない）
  function fitSettled() {
    fit();
    const mark = userMoves;
    const again = () => { if (userMoves === mark) fit(); };
    requestAnimationFrame(() => requestAnimationFrame(again));
    setTimeout(again, 300);
    document.fonts?.ready.then(again);
  }

  function onWheel(e) {
    e.preventDefault();
    userMoves++;
    const r = viewport().getBoundingClientRect();
    // 量の単位を px にそろえる（行単位・ページ単位で届く環境がある）
    const unit = e.deltaMode === 1 ? 33 : e.deltaMode === 2 ? r.height : 1;
    let dx = e.deltaX * unit;
    let dy = e.deltaY * unit;

    // Ctrl＋ホイールで拡大・縮小。タッチパッドのピンチも ctrlKey 付きで届く。
    // ピンチは 1 回の量が小さいので、量が小さいときは強めに効かせる
    if (e.ctrlKey || e.metaKey) {
      const k = Math.abs(dy) < 50 ? 0.01 : 0.0015;
      zoomAt(e.clientX - r.left, e.clientY - r.top, view.s * Math.exp(-dy * k));
      return;
    }

    // ホイールで上下、Shift＋ホイールで左右に動かす。
    // ブラウザによっては Shift のときに左右の量（deltaX）に入れ替えて届くので、その場合はそのまま使う
    if (e.shiftKey && dx === 0) { dx = dy; dy = 0; }
    view.x -= dx;
    view.y -= dy;
    applyView();
  }

  function startPan(e, click) {
    e.preventDefault();
    pan = { x: e.clientX, y: e.clientY, vx: view.x, vy: view.y, moved: false, click };
    app.dataset.panning = "1";
    hovered = null;
    refresh();
  }

  function viewCommand(cmd) {
    userMoves++;
    if (cmd === "in") zoomCenter(view.s * 1.25);
    else if (cmd === "out") zoomCenter(view.s / 1.25);
    else if (cmd === "reset") zoomCenter(1);
    else if (cmd === "fit") fit();
  }

  // ---- 選択枠・ホバー枠
  function overlay() {
    const host = app.querySelector(".dz-overlay");
    if (host && !host.firstElementChild) host.innerHTML = OVERLAY_HTML;
    return host;
  }

  function place(box, el) {
    const vr = viewport().getBoundingClientRect();
    const r = el.getBoundingClientRect();
    if (r.width === 0 && r.height === 0) { box.style.display = "none"; return null; }
    const top = r.top - vr.top;
    Object.assign(box.style, {
      display: "block",
      left: r.left - vr.left + "px",
      top: top + "px",
      width: r.width + "px",
      height: r.height + "px",
    });
    // 上に余白がなければ、ラベルを枠の内側に出す
    box.classList.toggle("bar-inside", top < 26);
    // ラベルが画面の右端か右のパネルにかかるなら、かからない所まで左に寄せる（左端は越えない）
    const bar = box.querySelector(".dz-ov-bar, .dz-ov-chip");
    if (bar) {
      bar.style.transform = "";
      const b = bar.getBoundingClientRect();
      const panel = app.querySelector(".dz-inspector")?.getBoundingClientRect();
      const limit = panel && b.bottom > panel.top && b.top < panel.bottom ? Math.min(vr.right, panel.left) : vr.right;
      const shift = Math.min(b.right - (limit - 4), b.left - (vr.left + 4));
      if (shift > 0) bar.style.transform = `translateX(${-shift}px)`;
    }
    return r;
  }

  // プレビューから編集に戻ったら、開いたままの Radzen の一覧（ドロップダウンや日付）を閉じる。
  // Radzen は一覧を body の直下に移して出すので、フレームの部品が編集用に戻っても一覧だけが残ってしまう
  let wasPreview = false;
  function closePopupsAfterPreview() {
    const now = isPreview();
    if (wasPreview && !now) window.Radzen?.closeAllPopups?.();
    wasPreview = now;
  }

  function refresh() {
    closePopupsAfterPreview();
    const host = overlay();
    if (!host) return;
    const current = activeFrame();
    for (const f of frames()) f.toggleAttribute("data-dz-active", f === current);

    const selBox = host.querySelector(".dz-ov-sel");
    const hovBox = host.querySelector(".dz-ov-hover");
    const sel = current?.querySelector("[data-dz-selected]");
    const busy = !!drag?.active || !!pan;

    if (sel && !busy && !isPreview()) {
      place(selBox, sel);
      selBox.querySelector("b").textContent = sel.dataset.dzTag ?? "";
      const move = selBox.querySelector("[data-dz-move]");
      move.dataset.dzTree = sel.dataset.dzId;
      move.dataset.dzLabel = sel.dataset.dzTag ?? "";
    } else {
      selBox.style.display = "none";
    }

    if (hovered && hovered.isConnected && hovered !== sel && !busy && !resize && !isPreview()) {
      place(hovBox, hovered);
      hovBox.querySelector(".dz-ov-chip").textContent = hovered.dataset.dzTag ?? "";
    } else {
      hovBox.style.display = "none";
    }

    const label = app.querySelector("[data-dz-zoom]");
    if (label && !label.textContent) label.textContent = Math.round(view.s * 100) + "%";

    revealCode();
  }

  // 選んだ部品のコードの行が見えるように、コード欄をスクロールする。
  // 選択が変わったときと、コード欄を開いたときだけ動かす（自分でスクロールした位置は崩さない）
  function revealCode() {
    const code = app.querySelector(".dz-source-code");
    const first = code?.querySelector(".dz-ln.sel-first");
    const id = first?.dataset.dzNode ?? null;
    if (code && first && (codeSel?.el !== code || codeSel?.id !== id)) {
      const last = code.querySelector(".dz-ln.sel-last") ?? first;
      const top = first.offsetTop;
      const bottom = last.offsetTop + last.offsetHeight;
      if (top < code.scrollTop || bottom > code.scrollTop + code.clientHeight) {
        const height = bottom - top;
        const to = height < code.clientHeight ? top - (code.clientHeight - height) / 2 : top - 8;
        code.scrollTo({ top: Math.max(0, to), behavior: "smooth" });
      }
    }
    codeSel = code ? { el: code, id } : null;
  }

  function refreshSoon() {
    cancelAnimationFrame(frame);
    frame = requestAnimationFrame(refresh);
  }

  function activate(el) {
    const f = frameOf(el);
    if (f) activeKey = f.dataset.dzFrame;
  }

  // ---- マウス操作
  function onPointerDown(e) {
    const t = e.target;

    // 中ボタンのドラッグは、どこでもキャンバスの移動
    if (e.button === 1 && inViewport(t)) { startPan(e, null); return; }
    if (e.button !== 0) return;

    // 選択枠のボタン（クリックで処理する）
    if (t.closest("[data-dz-cmd], [data-dz-view]")) return;

    // ボトムバーの上の縁：ドラッグで高さを変える。動かしている間は CSS 変数だけを書き換え、
    // 離したときに Blazor に高さを渡す
    const splitter = t.closest("[data-dz-split]");
    if (splitter) {
      e.preventDefault();
      const bottom = splitter.closest(".dz-bottom");
      const stage = app.querySelector(".dz-stage");
      split = { y: e.clientY, h: bottom.getBoundingClientRect().height, max: Math.max(160, stage.clientHeight - 160) };
      document.body.classList.add("dz-splitting");
      return;
    }

    // Space を押しているときは、キャンバスの移動
    if (inViewport(t) && space) { startPan(e, null); return; }

    // 大きさを変えるつまみ
    const handle = t.closest("[data-dz-resize]");
    if (handle) {
      const el = activeFrame()?.querySelector("[data-dz-selected]");
      if (!el) return;
      e.preventDefault();
      const r = el.getBoundingClientRect();
      resize = { dir: handle.dataset.dzResize, el, id: el.dataset.dzId, x: e.clientX, y: e.clientY, w: r.width, h: r.height, zoom: view.s };
      document.body.classList.add("dz-resizing");
      return;
    }

    // プレビュー中は、フレームの中の操作をそのまま部品に渡す
    if (isPreview()) {
      if (inViewport(t) && !inCanvas(t)) startPan(e, null);
      return;
    }

    if (inCanvas(t)) {
      // 部品のフォーカスや文字の選択をさせない
      e.preventDefault();
      e.stopPropagation();
      // preventDefault するとフォーカスが動かないので、パネルの入力欄から自分で外す
      // （外さないと Delete や Ctrl+Z が入力欄に入る）
      if (document.activeElement instanceof HTMLElement && document.activeElement !== document.body)
        document.activeElement.blur();
      activate(t);
      const node = t.closest("[data-dz-id]");
      const id = node?.dataset.dzId ?? "root";
      drag = { source: "node", key: id, label: node?.dataset.dzTag ?? "", x: e.clientX, y: e.clientY, fromCanvas: true, active: false };
      return;
    }
    const pal = t.closest("[data-dz-palette]");
    const tree = t.closest("[data-dz-tree]");
    if (pal) { drag = { source: "palette", key: pal.dataset.dzPalette, label: pal.dataset.dzLabel, x: e.clientX, y: e.clientY, active: false }; return; }
    if (tree) { drag = { source: "node", key: tree.dataset.dzTree, label: tree.dataset.dzLabel, x: e.clientX, y: e.clientY, active: false }; return; }

    // フレームの名前や余白は、ドラッグで移動、クリックで選択を外す
    if (inViewport(t)) {
      if (document.activeElement instanceof HTMLElement && document.activeElement !== document.body)
        document.activeElement.blur();
      activate(t);
      startPan(e, "deselect");
    }
  }

  function onPointerMove(e) {
    if (split) {
      split.value = Math.round(Math.min(split.max, Math.max(120, split.h - (e.clientY - split.y))));
      app.style.setProperty("--dz-bottom-h", split.value + "px");
      refreshSoon();
      return;
    }
    if (pan) {
      const dx = e.clientX - pan.x, dy = e.clientY - pan.y;
      if (!pan.moved && Math.hypot(dx, dy) < DRAG_THRESHOLD) return;
      pan.moved = true;
      userMoves++;
      view.x = pan.vx + dx;
      view.y = pan.vy + dy;
      applyView();
      return;
    }
    if (resize) {
      const z = resize.zoom;
      if (resize.dir.includes("r")) { resize.width = Math.max(16, (resize.w + e.clientX - resize.x) / z); resize.el.style.width = resize.width + "px"; }
      if (resize.dir.includes("b")) { resize.height = Math.max(16, (resize.h + e.clientY - resize.y) / z); resize.el.style.height = resize.height + "px"; }
      refreshSoon();
      return;
    }
    if (!drag) {
      updateHover(e.target);
      return;
    }
    if (!drag.active) {
      if (Math.hypot(e.clientX - drag.x, e.clientY - drag.y) < DRAG_THRESHOLD) return;
      if (drag.source === "node" && drag.key === "root") { drag = null; return; }
      drag.active = true;
      ghost.innerHTML = `<span class="dz-ghost-plus">+</span><b></b>`;
      ghost.querySelector("b").textContent = drag.label;
      ghost.style.display = "flex";
      document.body.classList.add("dz-dragging");
      window.getSelection()?.removeAllRanges();
      hovered = null;
      refresh();
    }
    ghost.style.left = e.clientX + "px";
    ghost.style.top = e.clientY + "px";
    drag.target = findDrop(e.clientX, e.clientY);
    showIndicator(drag.target);
  }

  function onPointerUp(e) {
    if (split) {
      const v = split.value;
      split = null;
      document.body.classList.remove("dz-splitting");
      if (v !== undefined) dotnet.invokeMethodAsync("SetCodeHeight", v);
      return;
    }
    if (pan) {
      const p = pan;
      pan = null;
      delete app.dataset.panning;
      if (p.moved) { suppressClick = true; setTimeout(() => { suppressClick = false; }, 0); }
      else if (p.click === "deselect") dotnet.invokeMethodAsync("Select", "root");
      refresh();
      return;
    }
    if (resize) {
      const r = resize;
      resize = null;
      document.body.classList.remove("dz-resizing");
      suppressClick = true;
      setTimeout(() => { suppressClick = false; }, 0);
      if (r.width !== undefined || r.height !== undefined)
        dotnet.invokeMethodAsync("Resize", r.id, r.width ?? null, r.height ?? null);
      return;
    }
    if (!drag) return;
    const d = drag;
    drag = null;
    if (d.active) {
      suppressClick = true;
      setTimeout(() => { suppressClick = false; }, 0);
      endDrag();
      const t = d.target;
      if (t) {
        activate(t.container);
        dotnet.invokeMethodAsync("Drop", d.source, d.key, t.parentId, t.index);
      } else refresh();
    } else if (d.fromCanvas) {
      dotnet.invokeMethodAsync("Select", d.key);
    }
    if (inCanvas(e.target)) e.stopPropagation();
  }

  function onPointerCancel() {
    if (drag?.active) endDrag();
    drag = null;
    resize = null;
    pan = null;
    split = null;
    delete app.dataset.panning;
    document.body.classList.remove("dz-resizing", "dz-splitting");
  }

  function endDrag() {
    ghost.style.display = "none";
    showIndicator(null);
    document.body.classList.remove("dz-dragging");
  }

  // フレーム内のクリック・ダブルクリック・マウスボタンは、部品に届く前に止める
  function swallow(e) {
    if (!isPreview() && inCanvas(e.target)) { e.preventDefault(); e.stopPropagation(); }
  }

  // キャンバスの上の中ボタンは移動に使うので、ブラウザの自動スクロールを出さない
  function onMouseDown(e) {
    if (e.button === 1 && inViewport(e.target)) e.preventDefault();
    swallow(e);
  }

  function onClick(e) {
    if (suppressClick) { suppressClick = false; e.preventDefault(); e.stopPropagation(); return; }
    const t = e.target;
    const v = t.closest("[data-dz-view]");
    if (v && app.contains(v)) { e.preventDefault(); viewCommand(v.dataset.dzView); return; }
    const cmd = t.closest("[data-dz-cmd]");
    if (cmd && app.contains(cmd)) {
      e.preventDefault();
      dotnet.invokeMethodAsync("Key", cmd.dataset.dzCmd);
      return;
    }
    // コードの行をクリックしたら、その行の部品を選ぶ（コピーのために文字を選んでいるときは選ばない）
    const ln = t.closest(".dz-source-code .dz-ln[data-dz-node]");
    if (ln && app.contains(ln)) {
      const range = window.getSelection();
      if (range && !range.isCollapsed) return;
      dotnet.invokeMethodAsync("Select", ln.dataset.dzNode);
      return;
    }
    // プレビュー中でも、リンクで別のページに移らないようにする
    if (isPreview() && inCanvas(t) && t.closest("a[href]")) { e.preventDefault(); e.stopPropagation(); return; }
    swallow(e);
  }

  // フレームの名前をダブルクリックすると、そのフレームに合わせる
  function onDblClick(e) {
    const label = e.target.closest?.("[data-dz-frame-label]");
    if (label) { e.preventDefault(); fit(frameOf(label)); return; }
    swallow(e);
  }

  // フレームの入力欄にフォーカスが入ったら外す（キーボードで文字が入らないように）
  function onFocusIn(e) {
    if (!isPreview() && inCanvas(e.target) && typeof e.target.blur === "function") e.target.blur();
  }

  function updateHover(el) {
    // 選択枠の上にいるときは、その下の部品のホバーを続ける
    if (el instanceof Element && el.closest(".dz-overlay")) return;
    const next = inCanvas(el) ? el.closest("[data-dz-id]:not([data-dz-id='root'])") : null;
    if (next === hovered) return;
    hovered = next;
    refresh();
  }

  // ---- ドロップ先を決める
  function childrenOf(container) {
    const seen = new Set();
    return [...container.querySelectorAll("[data-dz-id]")].filter(el => {
      if (el === container || el.parentElement?.closest("[data-dz-container]") !== container) return false;
      const id = el.dataset.dzId;
      if (seen.has(id)) return false;
      seen.add(id);
      return true;
    });
  }

  function isHorizontal(container, kids) {
    const host = kids[0]?.parentElement ?? container;
    const cs = getComputedStyle(host);
    return cs.display.includes("flex") && cs.flexDirection.startsWith("row");
  }

  function findDrop(x, y) {
    const el = document.elementFromPoint(x, y);
    // フレームの外（余白や浮いているパネルの上）には落とせない
    if (!el || !inCanvas(el)) return null;
    const scr = screenOf(el);
    let container = el.closest("[data-dz-container]") ?? scr.querySelector("[data-dz-id='root']");
    if (!container || !scr.contains(container)) container = scr.querySelector("[data-dz-id='root']");
    if (!container) return null;

    // 自分自身や自分の子孫には入れられない。その場合は自分の親に入れる
    if (drag.source === "node") {
      const self = scr.querySelector(`[data-dz-id="${CSS.escape(drag.key)}"]`);
      while (self && container && (container === self || self.contains(container)))
        container = self.parentElement?.closest("[data-dz-container]");
      if (!container) return null;
    }

    const kids = childrenOf(container).filter(k => k.dataset.dzId !== drag.key);
    const horizontal = isHorizontal(container, kids);
    let index = kids.length;
    for (let i = 0; i < kids.length; i++) {
      const b = kids[i].getBoundingClientRect();
      const before = horizontal
        ? y < b.top || (y <= b.bottom && x < b.left + b.width / 2)
        : y < b.top + b.height / 2;
      if (before) { index = i; break; }
    }
    return { container, parentId: container.dataset.dzId, index, kids, horizontal };
  }

  // 入れる位置に線（横並びなら縦線）と「+」を出す。空の入れ物なら枠ごと示す
  function showIndicator(t) {
    if (!t) { indicator.style.display = "none"; return; }
    const s = indicator.style;
    const empty = t.kids.length === 0;
    indicator.classList.toggle("box", empty);
    indicator.classList.toggle("vertical", !empty && t.horizontal);
    if (empty) {
      const b = t.container.getBoundingClientRect();
      Object.assign(s, { left: b.left + "px", top: b.top + "px", width: b.width + "px", height: b.height + "px" });
    } else {
      const last = t.index >= t.kids.length;
      const b = t.kids[last ? t.kids.length - 1 : t.index].getBoundingClientRect();
      if (t.horizontal)
        Object.assign(s, { left: (last ? b.right + 2 : b.left - 3) + "px", top: b.top + "px", width: "2px", height: b.height + "px" });
      else
        Object.assign(s, { left: b.left + "px", top: (last ? b.bottom + 2 : b.top - 3) + "px", width: b.width + "px", height: "2px" });
    }
    s.display = "block";
  }

  // ---- キー操作（パネルで入力しているときは使わない）
  function isTyping(t) {
    if (!(t instanceof Element) || !t.closest("input, textarea, select, [contenteditable='true']")) return false;
    // 編集中のフレームの入力欄はフォーカスを外しているので、入力中とはみなさない
    return isPreview() || !inCanvas(t);
  }

  function onKeyDown(e) {
    if (isTyping(e.target)) return;
    if (e.key === " ") {
      // Space を押している間は、ドラッグでキャンバスを動かす
      e.preventDefault();
      if (!space) { space = true; app.dataset.space = "1"; }
      return;
    }
    if (e.shiftKey && !e.ctrlKey && !e.altKey && e.code === "Digit1") { e.preventDefault(); fit(); return; }
    if (isPreview()) {
      if (e.key === "Escape") dotnet.invokeMethodAsync("Key", "ExitPreview");
      return;
    }
    const ctrl = e.ctrlKey || e.metaKey;
    const k = e.key.toLowerCase();
    let cmd = null;
    if (e.key === "Delete" || e.key === "Backspace") cmd = "Delete";
    else if (e.key === "Escape") cmd = "Escape";
    else if (ctrl && !e.shiftKey && k === "z") cmd = "Undo";
    else if (ctrl && (k === "y" || (e.shiftKey && k === "z"))) cmd = "Redo";
    else if (ctrl && k === "d") cmd = "Duplicate";
    else if (e.altKey && e.key === "ArrowUp") cmd = "MoveUp";
    else if (e.altKey && e.key === "ArrowDown") cmd = "MoveDown";
    if (!cmd) return;
    e.preventDefault();
    dotnet.invokeMethodAsync("Key", cmd);
  }

  function onKeyUp(e) {
    if (e.key === " " && space) {
      space = false;
      delete app.dataset.space;
      e.preventDefault();
    }
  }

  function onBlur() {
    space = false;
    delete app.dataset.space;
  }

  const onResize = () => refreshSoon();
  const observer = new ResizeObserver(() => refreshSoon());
  const observeWorld = () => { const w = world(); if (w) observer.observe(w); };
  observeWorld();
  // キャンバスの大きさが変わったとき（ウィンドウの大きさ、サイドバーやボトムバーの開閉）。
  // 全体表示のあと自分で拡大・移動していなければ、全体表示をやり直す。触っていれば枠だけ合わせ直す
  let vpSize = "";
  const vpObserver = new ResizeObserver(() => {
    const r = viewport()?.getBoundingClientRect();
    if (!r) return;
    const size = Math.round(r.width) + "x" + Math.round(r.height);
    if (size === vpSize) return;
    vpSize = size;
    if (fitMark === userMoves) fit();
    else refreshSoon();
  });
  vpObserver.observe(viewport());

  const vp = viewport();
  vp.addEventListener("wheel", onWheel, { passive: false });
  app.addEventListener("pointerdown", onPointerDown, true);
  app.addEventListener("mousedown", onMouseDown, true);
  app.addEventListener("dblclick", onDblClick, true);
  app.addEventListener("click", onClick, true);
  app.addEventListener("focusin", onFocusIn, true);
  window.addEventListener("pointermove", onPointerMove, true);
  window.addEventListener("pointerup", onPointerUp, true);
  window.addEventListener("pointercancel", onPointerCancel, true);
  window.addEventListener("resize", onResize);
  window.addEventListener("blur", onBlur);
  document.addEventListener("keydown", onKeyDown);
  document.addEventListener("keyup", onKeyUp);
  applyView();

  return {
    // Blazor が描き直したあとに呼ぶ
    refresh() {
      observeWorld();
      if (hovered && !hovered.isConnected) hovered = null;
      refresh();
    },
    // 全体を表示する（読み込んだあと・並べる画面を変えたあと）
    fit() {
      observeWorld();
      fitSettled();
    },
    dispose() {
      vp.removeEventListener("wheel", onWheel, { passive: false });
      app.removeEventListener("pointerdown", onPointerDown, true);
      app.removeEventListener("mousedown", onMouseDown, true);
      app.removeEventListener("dblclick", onDblClick, true);
      app.removeEventListener("click", onClick, true);
      app.removeEventListener("focusin", onFocusIn, true);
      window.removeEventListener("pointermove", onPointerMove, true);
      window.removeEventListener("pointerup", onPointerUp, true);
      window.removeEventListener("pointercancel", onPointerCancel, true);
      window.removeEventListener("resize", onResize);
      window.removeEventListener("blur", onBlur);
      document.removeEventListener("keydown", onKeyDown);
      document.removeEventListener("keyup", onKeyUp);
      observer.disconnect();
      vpObserver.disconnect();
      cancelAnimationFrame(frame);
      indicator.remove();
      ghost.remove();
      document.body.classList.remove("dz-dragging", "dz-resizing", "dz-splitting");
    },
  };
}

// ---- 保存（このブラウザの localStorage。使えない環境では何もしない）
export function save(key, json) {
  try { localStorage.setItem(key, json); } catch { /* プライベートウィンドウなど */ }
}

export function load(key) {
  try { return localStorage.getItem(key); } catch { return null; }
}

export async function copyText(text) {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    const ta = document.createElement("textarea");
    ta.value = text;
    ta.style.position = "fixed";
    ta.style.opacity = "0";
    document.body.appendChild(ta);
    ta.select();
    const ok = document.execCommand("copy");
    ta.remove();
    return ok;
  }
}
