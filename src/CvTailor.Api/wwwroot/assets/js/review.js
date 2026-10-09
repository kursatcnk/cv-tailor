// Prova ekranı (#review:<id>): üretilen CV'nin her maddesi önce/sonra yan yana, nedeniyle birlikte.
// Kullanıcı yeni hâli kabul ediyor, orijinali seçiyor, elle düzenliyor ya da alternatiflerden birini seçiyor.
// Uydurma koruması uyarı verdiyse madde onay bekliyor; onaylanmadıkça CV'ye orijinali giriyor.
(() => {
  "use strict";

  const session = window.CvTailorSession;
  const ui = window.CvTailorUi;
  const root = document.querySelector("#ct-review-root");
  const esc = value => String(value ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);

  const DROP_KIND = { bullet: "Madde", experience: "Deneyim", project: "Proje" };
  let cv = null;

  const currentText = c => c.editedText ?? c.after;
  const isUnchanged = c => c.before === c.after && !c.editedText;

  function decisionLabel(c) {
    if (c.decision === "pending") return "Onay bekliyor · CV'de orijinali var";
    if (c.decision === "rejected") return "CV'de orijinali var";
    if (c.editedText && c.alternatives.includes(c.editedText)) return "CV'de seçtiğin yazım var";
    return c.editedText ? "CV'de senin düzenlemen var" : "CV'de yeni hâli var";
  }

  function renderChange(c) {
    if (isUnchanged(c))
      return `<div class="ct-change is-unchanged"><p>${esc(c.before)}</p><span class="ct-hint">Değiştirilmedi</span></div>`;
    const reqs = c.requirements.map(r => cv.requirementLabels[r]).filter(Boolean);
    return `
      <div class="ct-change is-${c.decision}" data-change="${esc(c.id)}">
        <div class="ct-change-cols">
          <div class="ct-change-before"><span>Önce</span><p>${c.before ? esc(c.before) : "<em>Yoktu</em>"}</p></div>
          <div class="ct-change-after"><span>Sonra</span><p>${esc(currentText(c))}</p></div>
        </div>
        <p class="ct-change-reason"><strong>Neden:</strong> ${esc(c.reason)}</p>
        ${reqs.length ? `<div class="ct-chips is-static">${reqs.map(r => `<span class="ct-chip">${esc(r)}</span>`).join("")}</div>` : ""}
        ${c.guard.status === "flagged" ? `
          <div class="ct-guard">
            <strong>Kasandaki bilgilerle doğrulanamadı</strong>
            <ul>${c.guard.issues.map(i => `<li>${esc(i)}</li>`).join("")}</ul>
            <p>${c.decision === "accepted"
              ? "Bu metni sen onayladın. Doğru olduğundan emin ol; mülakatta bu cümleyi savunman gerekebilir."
              : "Bu bilgi doğruysa kasana ekle ya da yine de kullanmak için onayla. Onaylamazsan CV'ye orijinali girer."}</p>
          </div>` : ""}
        <div class="ct-change-foot">
          <span class="ct-change-state">${decisionLabel(c)}</span>
          <div class="ct-actions">
            ${c.decision !== "accepted" ? `<button class="ct-button" type="button" data-act="accept">${c.guard.status === "flagged" ? "Yine de kullan" : "Yeni hâlini kullan"}</button>` : ""}
            ${c.decision !== "rejected" ? `<button class="ct-button ct-button-secondary" type="button" data-act="reject">Orijinali kullan</button>` : ""}
            <button class="ct-button ct-button-secondary" type="button" data-act="edit">Düzenle</button>
            ${c.alternatives.length ? `<button class="ct-button ct-button-secondary" type="button" data-act="alts">Başka yazımlar</button>` : ""}
          </div>
        </div>
        <div class="ct-change-alts" hidden>
          ${c.alternatives.map((a, i) => `<button type="button" class="ct-alt" data-alt="${i}">${esc(a)}</button>`).join("")}
        </div>
        <div class="ct-change-edit" hidden>
          <textarea class="ct-textarea" rows="3">${esc(currentText(c))}</textarea>
          <div class="ct-actions">
            <button class="ct-button" type="button" data-act="save">Kaydet</button>
            <button class="ct-button ct-button-secondary" type="button" data-act="cancel">Vazgeç</button>
          </div>
        </div>
      </div>`;
  }

  function renderEntry(entry, byId) {
    return `
      <div class="ct-review-entry">
        <div class="ct-review-entry-head">
          <strong>${esc(entry.title)}</strong>
          <span class="ct-muted">${esc([entry.subtitle, entry.period].filter(Boolean).join(" · "))}</span>
        </div>
        ${entry.bulletChangeIds.map(id => byId[id] ? renderChange(byId[id]) : "").join("")}
      </div>`;
  }

  function render() {
    const byId = Object.fromEntries(cv.changes.map(c => [c.id, c]));
    const doc = cv.document;
    const rewritten = cv.changes.filter(c => !isUnchanged(c)).length;
    const pending = cv.changes.filter(c => c.decision === "pending").length;
    const notes = [cv.notice, ...(cv.warnings || [])].filter(Boolean);

    root.innerHTML = `
      <header class="ct-view-head ct-view-head-row">
        <div>
          <h1>CV provası</h1>
          <p>${esc(cv.targetTitle)} · <span class="ct-badge ${cv.status === "ready" ? "is-strong" : "is-unknown"}">${cv.status === "ready" ? "Onaylandı" : "Taslak"}</span></p>
        </div>
        <div class="ct-actions">
          <a class="ct-button ct-button-secondary" href="#cvs">CV'lerim</a>
          <button class="ct-button" type="button" data-act="approve">${cv.status === "ready" ? "Onaylandı" : "CV'yi onayla"}</button>
        </div>
      </header>
      ${notes.length ? `<div class="ct-review"><ul>${notes.map(n => `<li>${esc(n)}</li>`).join("")}</ul></div>` : ""}
      <p class="ct-summary-line">${rewritten} madde yeniden yazıldı${pending ? ` · <strong>${pending} madde onay bekliyor</strong>` : ""} · ${doc.dropped.length} öğe CV'ye girmedi</p>

      ${doc.summaryChangeId && byId[doc.summaryChangeId] ? `<section class="ct-card"><h2>Özet</h2>${renderChange(byId[doc.summaryChangeId])}</section>` : ""}
      ${doc.experiences.length ? `<section class="ct-card"><h2>Deneyim</h2>${doc.experiences.map(e => renderEntry(e, byId)).join("")}</section>` : ""}
      ${doc.projects.length ? `<section class="ct-card"><h2>Projeler</h2>${doc.projects.map(e => renderEntry(e, byId)).join("")}</section>` : ""}
      ${doc.dropped.length ? `
        <section class="ct-card">
          <h2>CV'ye girmeyenler</h2>
          <p class="ct-hint">Kasandan silinmediler; sadece bu ilan için CV'ye alınmadılar.</p>
          <ul class="ct-dropped">${doc.dropped.map(d => `<li><span class="ct-tag">${DROP_KIND[d.kind] || "Öğe"}</span> ${esc(d.text)}<span class="ct-muted"> — ${esc(d.reason)}</span></li>`).join("")}</ul>
        </section>` : ""}`;
  }

  async function load(id) {
    root.innerHTML = `<p class="ct-loading">CV yükleniyor...</p>`;
    const { ok, data } = await session.api.get(`/api/cvs/${id}`);
    if (!ok) { root.innerHTML = `<p class="ct-muted">${esc(data?.message || "CV bulunamadı.")}</p>`; return; }
    cv = data;
    render();
  }

  async function update(changeId, body) {
    const { ok, data } = await session.api.patch(`/api/cvs/${cv.id}/changes/${changeId}`, body);
    if (!ok) { ui.toast(data?.message || "Kaydedilemedi.", "error"); return; }
    cv = data;
    render();
    const changed = cv.changes.find(c => c.id === changeId);
    if (changed?.guard.status === "flagged" && body.text) ui.toast("Kaydedildi, ama yazdığın metinde kasada olmayan bilgi var.", "error");
  }

  root.addEventListener("click", async event => {
    const button = event.target.closest("[data-act], [data-alt]");
    if (!button) return;
    const card = button.closest("[data-change]");
    const id = card?.dataset.change;

    if (button.dataset.alt) return update(id, { alternative: Number(button.dataset.alt) });
    switch (button.dataset.act) {
      case "accept": return update(id, { decision: "accepted" });
      case "reject": return update(id, { decision: "rejected" });
      case "alts": card.querySelector(".ct-change-alts").hidden ^= true; return;
      case "edit": card.querySelector(".ct-change-edit").hidden = false; card.querySelector("textarea").focus(); return;
      case "cancel": card.querySelector(".ct-change-edit").hidden = true; return;
      case "save": return update(id, { text: card.querySelector("textarea").value });
      case "approve": {
        if (cv.status === "ready") return;
        const { ok, data } = await session.api.post(`/api/cvs/${cv.id}/approve`);
        if (!ok) { ui.toast(data?.message || "Onaylanamadı.", "error"); return; }
        cv = { ...data, warnings: cv.warnings };
        render();
        ui.toast(data.notice || "CV onaylandı.");
      }
    }
  });

  function route() {
    const [view, id] = location.hash.slice(1).split(":");
    if (view === "review" && id) load(id);
  }
  window.addEventListener("hashchange", route);
  route();
})();
