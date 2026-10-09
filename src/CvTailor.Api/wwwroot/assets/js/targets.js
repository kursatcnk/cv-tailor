// "Yeni CV" (ilan ya da meslek seçimi) ve "CV'lerim" (kayıtlı hedefler) ekranları.
// Her hedefin detayında gereksinim–kanıt matrisi gösteriliyor; matris o anki kasaya göre sunucuda hesaplanıyor.
(() => {
  "use strict";

  const session = window.CvTailorSession;
  const ui = window.CvTailorUi;
  const $ = selector => document.querySelector(selector);
  const esc = value => String(value ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);

  const CATEGORY = { technical: "Teknik", tool: "Araç", experience: "Deneyim", education: "Eğitim", language: "Dil", certification: "Sertifika", soft: "Yetkinlik", other: "Diğer" };
  const SENIORITY = { junior: "Junior", mid: "Mid-level", senior: "Senior" };
  const STRENGTH = { strong: "Güçlü", weak: "Zayıf", missing: "Yok", unknown: "Ölçülemez" };
  const EVIDENCE = { achievement: "İş maddesi", projectAchievement: "Proje maddesi", project: "Proje", experience: "Deneyim", education: "Eğitim", skill: "Beceri", certificate: "Sertifika", summary: "Özet", duration: "Süre" };
  const dateFormat = new Intl.DateTimeFormat("tr-TR", { day: "numeric", month: "long", year: "numeric" });

  // ---- Hedef detayı ----

  function renderMatch(match) {
    const order = { missing: 0, weak: 1, strong: 2, unknown: 3 };
    const rows = [...match.items].sort((a, b) => (a.importance === b.importance ? 0 : a.importance === "must" ? -1 : 1) || order[a.strength] - order[b.strength]);
    return `
      <section class="ct-match">
        <div class="ct-verdict">
          <strong>${esc(match.verdict)}</strong>
          <p>${esc(match.advice)}</p>
          <div class="ct-meter" aria-hidden="true">
            <i class="is-strong" style="flex:${match.mustStrong}"></i><i class="is-weak" style="flex:${match.mustWeak}"></i><i class="is-missing" style="flex:${match.mustMissing}"></i>
          </div>
        </div>
        <h3>Gereksinimler ve kasandaki kanıtlar</h3>
        <ul class="ct-match-list">${rows.map(item => `
          <li class="is-${item.strength}">
            <div class="ct-match-head">
              <span class="ct-badge is-${item.strength}">${STRENGTH[item.strength]}</span>
              <span class="ct-req-text">${esc(item.text)}</span>
              <span class="ct-tag">${item.importance === "must" ? "Zorunlu" : "Tercih"}</span>
              <span class="ct-tag">${CATEGORY[item.category] || "Diğer"}</span>
            </div>
            <p class="ct-match-note">${esc(item.note)}</p>
            ${item.evidence.length ? `<ul class="ct-evidence">${item.evidence.map(e => `
              <li><span class="ct-evidence-kind">${EVIDENCE[e.kind] || e.kind}</span> ${esc(e.text)}${e.where && e.kind !== "experience" ? ` <span class="ct-muted">· ${esc(e.where)}</span>` : ""}${e.term && e.term.includes("→") ? ` <span class="ct-muted">(${esc(e.term)})</span>` : ""}</li>`).join("")}</ul>` : ""}
          </li>`).join("")}
        </ul>
      </section>`;
  }

  function renderGuide(guide) {
    return `
      <section class="ct-guide">
        <h3>Bu meslekte CV yazarken</h3>
        <ul class="ct-plain-list">${guide.tips.map(t => `<li>${esc(t)}</li>`).join("")}</ul>
        <div class="ct-examples">${guide.examples.map(e => `
          <div class="ct-example">
            <p class="ct-example-bad">${esc(e.bad)}</p>
            <p class="ct-example-good">${esc(e.good)}</p>
            <p class="ct-hint">${esc(e.why)}</p>
          </div>`).join("")}</div>
      </section>`;
  }

  function renderTarget(t, match, { withDelete = false } = {}) {
    const a = t.analysis;
    const meta = [t.company, SENIORITY[t.seniority], a.yearsOfExperience != null ? `en az ${a.yearsOfExperience} yıl` : null, t.professionKey ? "Meslek profili" : null, dateFormat.format(new Date(t.createdAt))].filter(Boolean);
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
        <div data-match-slot>${match ? renderMatch(match) : `<p class="ct-muted">Eşleşme hesaplanamadı.</p>`}</div>
        <div data-interview-slot="${t.id}"></div>
        <div data-cvs-slot="${t.id}"></div>
        ${t.guide ? renderGuide(t.guide) : ""}
        ${a.responsibilities.length ? `<section><h3>İş tanımı</h3><ul class="ct-plain-list">${a.responsibilities.map(x => `<li>${esc(x)}</li>`).join("")}</ul></section>` : ""}
        ${a.keywords.length ? `<section><h3>Anahtar kelimeler</h3><p class="ct-hint">İşe alım sistemlerinin CV'nde arayacağı terimler.</p><div class="ct-chips is-static">${a.keywords.map(k => `<span class="ct-chip">${esc(k)}</span>`).join("")}</div></section>` : ""}
        ${t.postingText ? `<details class="ct-paste"><summary>İlan metnini göster</summary><pre class="ct-posting">${esc(t.postingText)}</pre></details>` : ""}
      </article>`;
  }

  // ---- Sorular ----

  const QUESTION_KIND = { requirement: "Eksik nitelik", soft: "Örnek durum", number: "Rakam" };

  function renderInterview(data) {
    if (!data.questions.length)
      return `<section class="ct-interview"><h3>Sorular</h3><p class="ct-muted">${data.answeredCount ? "Sorulacak bir şey kalmadı. Cevapların kasana eklendi." : "Bu hedef için sorulacak bir şey yok; kasandakiler yeterli görünüyor."}</p></section>`;
    const parents = data.parents.map(p => `<option value="${p.id}">${esc(p.label)}</option>`).join("");
    return `
      <section class="ct-interview">
        <h3>Sorular <span>${data.questions.length}</span></h3>
        <p class="ct-hint">Kanıtı eksik ya da zayıf kalan yerler için. Cevabın kasana kendi cümlenle eklenir; CV'de nasıl yazılacağını sonraki adımda birlikte düzenleriz. Yapmadığın bir şey için "Yok" de, uydurmayalım.</p>
        ${data.questions.map(q => `
          <form class="ct-question" data-key="${esc(q.key)}" novalidate>
            <span class="ct-tag">${QUESTION_KIND[q.kind] || "Soru"}</span>
            <p class="ct-question-prompt">${esc(q.prompt)}</p>
            <textarea class="ct-textarea" rows="2" aria-label="Cevap"></textarea>
            <p class="ct-hint">${esc(q.hint)}</p>
            <div class="ct-question-row">
              ${parents ? `<label class="ct-field"><span>Hangi işe ya da projeye eklensin?</span><select class="ct-input" data-parent>${parents}</select></label>` : ""}
              <div class="ct-actions">
                <button class="ct-button ct-button-secondary" type="button" data-skip>Yok, geç</button>
                <button class="ct-button" type="submit">Kasaya ekle</button>
              </div>
            </div>
          </form>`).join("")}
      </section>`;
  }

  async function mountInterview(root, targetId) {
    const slot = root.querySelector(`[data-interview-slot="${targetId}"]`);
    if (!slot) return;
    const { ok, data } = await session.api.get(`/api/targets/${targetId}/interview`);
    if (!ok) return;
    showInterview(root, slot, data);
  }

  function showInterview(root, slot, data) {
    slot.innerHTML = renderInterview(data);
    // Önerilen işi seçili getir.
    data.questions.forEach(q => {
      const select = slot.querySelector(`[data-key="${CSS.escape(q.key)}"] [data-parent]`);
      if (select && q.suggestedParentId) select.value = q.suggestedParentId;
    });
    root.querySelector("[data-match-slot]").innerHTML = renderMatch(data.match);
  }

  async function sendAnswer(form, skip) {
    const root = form.closest("#ct-new-result, #ct-cvs-detail");
    const slot = form.closest("[data-interview-slot]");
    const answer = form.querySelector("textarea").value.trim();
    if (!skip && answer.length < 10) { ui.toast("Bir iki cümleyle anlat ya da \"Yok, geç\" de.", "error"); return; }

    form.querySelectorAll("button").forEach(b => { b.disabled = true; });
    const { ok, data } = await session.api.post(`/api/targets/${slot.dataset.interviewSlot}/interview`, {
      answers: [{ key: form.dataset.key, answer: skip ? null : answer, parentId: form.querySelector("[data-parent]")?.value || null, skip }]
    });
    if (!ok) {
      form.querySelectorAll("button").forEach(b => { b.disabled = false; });
      ui.toast(data?.message || "Cevap kaydedilemedi.", "error");
      return;
    }
    showInterview(root, slot, data);
    ui.toast(skip ? "Bu soru bir daha sorulmayacak." : "Cevabın kasana eklendi; eşleşme güncellendi.");
  }

  ["#ct-new-result", "#ct-cvs-detail"].forEach(selector => {
    const root = $(selector);
    root.addEventListener("submit", event => {
      const form = event.target.closest(".ct-question");
      if (!form) return;
      event.preventDefault();
      sendAnswer(form, false);
    });
    root.addEventListener("click", event => {
      const skip = event.target.closest("[data-skip]");
      if (skip) sendAnswer(skip.closest(".ct-question"), true);
    });
  });

  // ---- Bu hedef için hazırlanan CV'ler ----

  async function mountCvs(root, targetId) {
    const slot = root.querySelector(`[data-cvs-slot="${targetId}"]`);
    if (!slot) return;
    const { ok, data } = await session.api.get(`/api/cvs?targetId=${targetId}`);
    const versions = ok ? data : [];
    slot.innerHTML = `
      <section class="ct-cv-block">
        <h3>CV</h3>
        <p class="ct-hint">Seçilen maddeler bu ilana göre yeniden yazılır, tek sayfaya sığdırılır. Her değişikliği provada tek tek onaylarsın; kasandaki bilgiler değişmez.</p>
        <div class="ct-actions">
          <button class="ct-button" type="button" data-generate-cv="${targetId}">${versions.length ? "Yeni sürüm hazırla" : "Bu hedef için CV hazırla"}</button>
        </div>
        <div class="ct-busy" data-generate-busy hidden>Maddeler yeniden yazılıyor ve doğrulanıyor...</div>
        ${versions.length ? `<ul class="ct-versions">${versions.map((v, i) => `
          <li><a href="#review:${v.id}">${i === 0 ? "Son sürüm" : `Sürüm ${versions.length - i}`}</a>
            <span class="ct-badge ${v.status === "ready" ? "is-strong" : "is-unknown"}">${v.status === "ready" ? "Onaylandı" : "Taslak"}</span>
            <span class="ct-muted">${dateFormat.format(new Date(v.createdAt))}</span></li>`).join("")}</ul>` : ""}
      </section>`;
  }

  async function generateCv(button) {
    const slot = button.closest("[data-cvs-slot]");
    button.disabled = true;
    slot.querySelector("[data-generate-busy]").hidden = false;
    const { ok, status, data } = await session.api.post("/api/cvs", { targetId: slot.dataset.cvsSlot });
    ui.refreshUsage();
    if (!ok) {
      button.disabled = false;
      slot.querySelector("[data-generate-busy]").hidden = true;
      ui.toast(status === 429 && data?.code === "quota_exceeded" ? "Bu ayki AI hakkın doldu." : (data?.message || "CV hazırlanamadı."), "error");
      return;
    }
    location.hash = `#review:${data.id}`;
  }

  ["#ct-new-result", "#ct-cvs-detail"].forEach(selector => $(selector).addEventListener("click", event => {
    const button = event.target.closest("[data-generate-cv]");
    if (button) generateCv(button);
  }));

  async function loadMatch(id) {
    const { ok, data } = await session.api.get(`/api/targets/${id}/match`);
    return ok ? data : null;
  }

  async function showResult(target) {
    const result = $("#ct-new-result");
    result.innerHTML = renderTarget(target, await loadMatch(target.id));
    result.hidden = false;
    mountInterview(result, target.id);
    mountCvs(result, target.id);
    result.scrollIntoView({ block: "start" });
    loadList();
  }

  // ---- Yeni CV: ilan ya da meslek ----

  let mode = "posting";
  let vaultReady = false;

  function showForms() {
    $("#ct-new-empty").hidden = vaultReady;
    $("#ct-new-tabs").hidden = !vaultReady;
    $("#ct-target-form").hidden = !vaultReady || mode !== "posting";
    $("#ct-profession-form").hidden = !vaultReady || mode !== "profession";
  }

  window.addEventListener("ct:vault", event => {
    vaultReady = event.detail.ready;
    showForms();
  });

  $("#ct-new-tabs").addEventListener("click", event => {
    const tab = event.target.closest("[data-mode]");
    if (!tab) return;
    mode = tab.dataset.mode;
    document.querySelectorAll("#ct-new-tabs [data-mode]").forEach(b => {
      b.classList.toggle("is-active", b === tab);
      b.setAttribute("aria-selected", b === tab);
    });
    showForms();
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
    await showResult(data);
    ui.toast("İlan çözümlendi ve CV'lerim'e eklendi.");
  });

  let professions = [];
  async function loadProfessions() {
    const { ok, data } = await session.api.get("/api/targets/professions");
    if (!ok) return;
    professions = data;
    $("#ct-profession").innerHTML = data.map(p => `<option value="${esc(p.key)}">${esc(p.title)}</option>`).join("");
    showProfessionSummary();
  }
  function showProfessionSummary() {
    $("#ct-profession-summary").textContent = professions.find(p => p.key === $("#ct-profession").value)?.summary || "";
  }
  $("#ct-profession").addEventListener("change", showProfessionSummary);

  $("#ct-profession-form").addEventListener("submit", async event => {
    event.preventDefault();
    const button = $("#ct-profession-submit");
    button.disabled = true;
    const { ok, data } = await session.api.post("/api/targets/profession", {
      professionKey: $("#ct-profession").value,
      seniority: document.querySelector("[name=ct-seniority]:checked")?.value
    });
    button.disabled = false;
    if (!ok) { ui.toast(data?.message || "Hedef oluşturulamadı.", "error"); return; }
    await showResult(data);
    ui.toast("Hedef oluşturuldu ve CV'lerim'e eklendi.");
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
    const [{ ok, data }, match] = await Promise.all([session.api.get(`/api/targets/${id}`), loadMatch(id)]);
    if (!ok) { ui.toast(data?.message || "İlan açılamadı.", "error"); return; }
    $("#ct-target-list").hidden = true;
    const detail = $("#ct-cvs-detail");
    detail.innerHTML = `<button class="ct-link-button" type="button" data-back>← Bütün hedefler</button>${renderTarget(data, match, { withDelete: true })}`;
    detail.hidden = false;
    mountInterview(detail, id);
    mountCvs(detail, id);
    window.scrollTo({ top: 0 });
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
    if (!del || !confirm("Bu hedef ve onun için hazırladığın CV'ler silinsin mi?")) return;
    const { ok, data } = await session.api.del(`/api/targets/${del.dataset.deleteTarget}`);
    if (!ok) { ui.toast(data?.message || "Silinemedi.", "error"); return; }
    closeTarget();
    loadList();
    ui.toast("Hedef silindi.");
  });

  // Başka ekrandan CV'lerim'e dönünce liste görünsün, açık kalan detay değil.
  window.addEventListener("hashchange", () => { if (location.hash === "#cvs") closeTarget(); });

  loadProfessions();
  loadList();
})();
