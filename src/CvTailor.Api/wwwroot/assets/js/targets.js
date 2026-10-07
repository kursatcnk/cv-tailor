// "Yeni CV" (ilan yapıştırma ve çözümleme) ve "CV'lerim" (kayıtlı ilanlar) ekranları.
(() => {
  "use strict";

  const session = window.CvTailorSession;
  const ui = window.CvTailorUi;
  const $ = selector => document.querySelector(selector);
  const esc = value => String(value ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);

  const CATEGORY = { technical: "Teknik", tool: "Araç", experience: "Deneyim", education: "Eğitim", language: "Dil", certification: "Sertifika", soft: "Yetkinlik", other: "Diğer" };
  const SENIORITY = { junior: "Junior", mid: "Mid-level", senior: "Senior" };
  const dateFormat = new Intl.DateTimeFormat("tr-TR", { day: "numeric", month: "long", year: "numeric" });

  // ---- Ortak: çözümlenmiş ilanın gösterimi ----

  function requirementList(items, empty) {
    if (!items.length) return `<p class="ct-muted">${empty}</p>`;
    return `<ul class="ct-req-list">${items.map(r => `
      <li>
        <span class="ct-req-text">${esc(r.text)}</span>
        <span class="ct-req-meta"><span class="ct-tag">${CATEGORY[r.category] || "Diğer"}</span>${r.terms?.length ? `<span>${r.terms.map(esc).join(", ")}</span>` : ""}</span>
      </li>`).join("")}</ul>`;
  }

  function renderTarget(t, { withDelete = false } = {}) {
    const a = t.analysis;
    const must = a.requirements.filter(r => r.importance === "must");
    const nice = a.requirements.filter(r => r.importance === "nice");
    const meta = [t.company, SENIORITY[t.seniority], a.yearsOfExperience != null ? `en az ${a.yearsOfExperience} yıl` : null, dateFormat.format(new Date(t.createdAt))].filter(Boolean);
    return `
      <article class="ct-card ct-target">
        <header class="ct-target-head">
          <div>
            <h2>${esc(t.title)}</h2>
            <p class="ct-muted">${meta.map(esc).join(" · ")}</p>
          </div>
          ${withDelete ? `<button class="ct-button ct-button-danger" type="button" data-delete-target="${t.id}">Sil</button>` : ""}
        </header>
        ${t.notice ? `<div class="ct-busy">${esc(t.notice)}</div>` : ""}
        ${a.summary ? `<p>${esc(a.summary)}</p>` : ""}
        <div class="ct-req-columns">
          <section><h3>Zorunlu <span>${must.length}</span></h3>${requirementList(must, "İlanda zorunlu nitelik belirtilmemiş.")}</section>
          <section><h3>Tercih sebebi <span>${nice.length}</span></h3>${requirementList(nice, "İlanda tercih sebebi belirtilmemiş.")}</section>
        </div>
        ${a.responsibilities.length ? `<section><h3>İş tanımı</h3><ul class="ct-plain-list">${a.responsibilities.map(x => `<li>${esc(x)}</li>`).join("")}</ul></section>` : ""}
        ${a.keywords.length ? `<section><h3>Anahtar kelimeler</h3><p class="ct-hint">İşe alım sistemlerinin CV'nde arayacağı terimler.</p><div class="ct-chips is-static">${a.keywords.map(k => `<span class="ct-chip">${esc(k)}</span>`).join("")}</div></section>` : ""}
        ${t.postingText ? `<details class="ct-paste"><summary>İlan metnini göster</summary><pre class="ct-posting">${esc(t.postingText)}</pre></details>` : ""}
      </article>`;
  }

  // ---- Yeni CV ----

  window.addEventListener("ct:vault", event => {
    $("#ct-new-empty").hidden = event.detail.ready;
    $("#ct-target-form").hidden = !event.detail.ready;
  });

  $("#ct-target-form").addEventListener("submit", async event => {
    event.preventDefault();
    const postingText = $("#ct-posting").value.trim();
    if (postingText.length < 150) { ui.toast("İlanın tamamını yapıştır; özellikle aranan nitelikler kısmı olmalı.", "error"); return; }

    const button = $("#ct-analyze");
    button.disabled = true;
    $("#ct-analyze-busy").hidden = false;
    const { ok, status, data } = await session.api.post("/api/targets", {
      postingText,
      title: $("#ct-target-title").value.trim() || null,
      company: $("#ct-target-company").value.trim() || null
    });
    button.disabled = false;
    $("#ct-analyze-busy").hidden = true;
    ui.refreshUsage();

    if (!ok) {
      ui.toast(status === 429 && data?.code === "quota_exceeded" ? "Bu ayki AI hakkın doldu." : (data?.message || "İlan çözümlenemedi."), "error");
      return;
    }
    event.target.reset();
    const result = $("#ct-new-result");
    result.innerHTML = renderTarget(data);
    result.hidden = false;
    result.scrollIntoView({ behavior: "smooth", block: "start" });
    loadList();
    ui.toast("İlan çözümlendi ve CV'lerim'e eklendi.");
  });

  // ---- CV'lerim ----

  async function loadList() {
    const { ok, data } = await session.api.get("/api/targets");
    if (!ok) return;
    $("#ct-cvs-empty").hidden = data.length > 0;
    $("#ct-target-list").innerHTML = data.map(t => `
      <button class="ct-target-row" type="button" data-open-target="${t.id}">
        <span class="ct-target-row-main"><strong>${esc(t.title)}</strong><span>${esc([t.company, SENIORITY[t.seniority]].filter(Boolean).join(" · "))}</span></span>
        <span class="ct-target-row-side"><span>${t.mustCount} zorunlu · ${t.niceCount} tercih</span><span>${dateFormat.format(new Date(t.createdAt))}</span></span>
      </button>`).join("");
  }

  async function openTarget(id) {
    const { ok, data } = await session.api.get(`/api/targets/${id}`);
    if (!ok) { ui.toast(data?.message || "İlan açılamadı.", "error"); return; }
    $("#ct-target-list").hidden = true;
    const detail = $("#ct-cvs-detail");
    detail.innerHTML = `<button class="ct-link-button" type="button" data-back>← Bütün ilanlar</button>${renderTarget(data, { withDelete: true })}`;
    detail.hidden = false;
  }

  function closeTarget() {
    $("#ct-cvs-detail").hidden = true;
    $("#ct-target-list").hidden = false;
  }

  $("#ct-target-list").addEventListener("click", event => {
    const row = event.target.closest("[data-open-target]");
    if (row) openTarget(row.dataset.openTarget);
  });

  $("#ct-cvs-detail").addEventListener("click", async event => {
    if (event.target.closest("[data-back]")) { closeTarget(); return; }
    const del = event.target.closest("[data-delete-target]");
    if (!del || !confirm("Bu ilan ve onun için hazırladığın CV'ler silinsin mi?")) return;
    const { ok, data } = await session.api.del(`/api/targets/${del.dataset.deleteTarget}`);
    if (!ok) { ui.toast(data?.message || "Silinemedi.", "error"); return; }
    closeTarget();
    loadList();
    ui.toast("İlan silindi.");
  });

  // Başka ekrandan CV'lerim'e dönünce liste görünsün, açık kalan detay değil.
  window.addEventListener("hashchange", () => { if (location.hash === "#cvs") closeTarget(); });

  loadList();
})();
