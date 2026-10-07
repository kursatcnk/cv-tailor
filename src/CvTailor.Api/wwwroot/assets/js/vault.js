// Kariyer kasası ekranı: CV yükleme, okunan bilgileri kontrol etme ve kasayı düzenleme.
// Profil tek bir state nesnesinde duruyor. Alanlar data-bind="experiences.0.title" ile bu nesneye bağlı;
// yazarken sadece değer güncelleniyor, ekle/sil/taşı gibi yapısal değişikliklerde form baştan çiziliyor.
(() => {
  "use strict";

  const session = window.CvTailorSession;
  const ui = window.CvTailorUi;
  const $ = selector => document.querySelector(selector);

  const EMPLOYMENT = [["", "Belirtilmemiş"], ["full-time", "Tam zamanlı"], ["part-time", "Yarı zamanlı"], ["internship", "Staj"], ["freelance", "Serbest"]];
  const SKILL_GROUPS = [
    ["technical", "Teknik", "C#, React, SQL..."],
    ["tool", "Araçlar", "Git, Jira, Excel, Logo..."],
    ["language", "Diller", "İngilizce (B2)"],
    ["soft", "Yetkinlikler", "Sunum, müzakere..."]
  ];

  const state = { profile: null, saved: null, exists: false, dirty: false, reviewing: false };

  const esc = value => String(value ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
  const clone = value => JSON.parse(JSON.stringify(value));
  const emptyProfile = () => ({ experiences: [], educations: [], projects: [], skills: [], certificates: [] });

  function getPath(path) { return path.split(".").reduce((obj, key) => obj?.[key], state.profile); }
  function setPath(path, value) {
    const keys = path.split(".");
    const last = keys.pop();
    const target = keys.reduce((obj, key) => obj[key], state.profile);
    target[last] = value;
  }

  // ---- Ekran durumları ----

  function showImport(show) {
    $("#ct-import").hidden = !show;
    $("#ct-import-title").textContent = state.exists ? "Yeni bir CV yükle" : "Kasan henüz boş. CV'ni yükleyerek başla.";
    $("#ct-import-cancel-row").hidden = !state.exists;
    $("#ct-reimport").hidden = show || !state.exists;
  }

  function showEditor(show) {
    $("#ct-editor-layout").hidden = !show;
    if (show) render();
  }

  function setDirty(dirty) {
    state.dirty = dirty;
    $("#ct-savebar").hidden = !dirty;
    $("#ct-savebar-text").textContent = state.reviewing ? "Okunan bilgiler henüz kasaya kaydedilmedi." : "Kaydedilmemiş değişiklikler var.";
  }

  function showReview(data) {
    state.reviewing = Boolean(data);
    $("#ct-review").hidden = !data;
    $("#ct-source").hidden = !data;
    if (!data) return;
    const notes = [...(data.warnings || [])];
    if (data.notice) notes.unshift(data.notice);
    $("#ct-review-notes").innerHTML = notes.map(n => `<li>${esc(n)}</li>`).join("");
    $("#ct-source-text").textContent = data.extractedText || "";
  }

  // ---- Yükleme ----

  async function load() {
    const { ok, data } = await session.api.get("/api/vault");
    $("#ct-vault-loading").hidden = true;
    if (!ok) { ui.toast(data?.message || "Kasa yüklenemedi.", "error"); return; }
    state.exists = data.exists;
    state.saved = data.profile;
    state.profile = clone(data.profile);
    showReview(null);
    setDirty(false);
    showImport(!data.exists);
    showEditor(data.exists);
    announce();
  }

  // "Yeni CV" ekranı kasada CV'ye girecek bir şey olup olmadığını buradan öğreniyor.
  function announce() {
    const saved = state.saved || emptyProfile();
    const ready = saved.experiences.length + saved.projects.length > 0;
    window.dispatchEvent(new CustomEvent("ct:vault", { detail: { ready } }));
  }

  async function importCv(request) {
    if (state.exists && !confirm("Yeni CV'den okunan bilgiler kasandakilerin yerine geçecek. Kaydetmeden önce kontrol edebilirsin. Devam edilsin mi?")) return;
    $("#ct-import-busy").hidden = false;
    $("#ct-drop").classList.add("is-busy");
    const { ok, status, data } = await request();
    $("#ct-import-busy").hidden = true;
    $("#ct-drop").classList.remove("is-busy");
    $("#ct-file").value = "";
    ui.refreshUsage();

    if (!ok) {
      ui.toast(status === 429 && data?.code === "quota_exceeded" ? "Bu ayki AI hakkın doldu." : (data?.message || "CV okunamadı."), "error");
      return;
    }
    // Kasa zaten varsa Id'ler eşleşmiyor; kaydedince yeni CV'nin bilgileri eskilerin yerine geçiyor.
    state.profile = data.profile;
    showImport(false);
    showReview(data);
    showEditor(true);
    setDirty(true);
    window.scrollTo({ top: 0, behavior: "smooth" });
  }

  function uploadFile(file) {
    if (!file) return;
    if (file.size > 5 * 1024 * 1024) { ui.toast("Dosya en fazla 5 MB olabilir.", "error"); return; }
    const form = new FormData();
    form.append("file", file);
    importCv(() => session.api.upload("/api/cv/import", form));
  }

  $("#ct-file").addEventListener("change", event => uploadFile(event.target.files[0]));
  const drop = $("#ct-drop");
  ["dragenter", "dragover"].forEach(type => drop.addEventListener(type, event => { event.preventDefault(); drop.classList.add("is-over"); }));
  ["dragleave", "drop"].forEach(type => drop.addEventListener(type, () => drop.classList.remove("is-over")));
  drop.addEventListener("drop", event => { event.preventDefault(); uploadFile(event.dataTransfer.files[0]); });

  $("#ct-paste-submit").addEventListener("click", () => {
    const text = $("#ct-paste-text").value.trim();
    if (!text) { ui.toast("Önce CV metnini yapıştır.", "error"); return; }
    importCv(() => session.api.post("/api/cv/import-text", { text }));
  });

  $("#ct-reimport").addEventListener("click", () => { showImport(true); window.scrollTo({ top: 0, behavior: "smooth" }); });
  $("#ct-import-cancel").addEventListener("click", () => showImport(false));

  // ---- Kaydet / vazgeç ----

  $("#ct-save").addEventListener("click", async () => {
    const button = $("#ct-save");
    button.disabled = true;
    const { ok, data } = await session.api.put("/api/vault", state.profile);
    button.disabled = false;
    if (!ok) { ui.toast(data?.message || "Kasa kaydedilemedi.", "error"); return; }
    state.exists = true;
    state.saved = data.profile;
    state.profile = clone(data.profile);
    showReview(null);
    setDirty(false);
    showImport(false);
    render();
    announce();
    ui.toast("Kasana kaydedildi.");
  });

  $("#ct-discard").addEventListener("click", () => {
    if (!confirm("Kaydedilmemiş değişiklikler silinecek.")) return;
    state.profile = clone(state.saved || emptyProfile());
    showReview(null);
    setDirty(false);
    showImport(!state.exists);
    showEditor(state.exists);
  });

  window.addEventListener("beforeunload", event => { if (state.dirty) event.preventDefault(); });

  // ---- Form çizimi ----

  function field(label, path, { type = "text", placeholder = "", wide = false, rows = 0 } = {}) {
    const value = getPath(path);
    const input = rows
      ? `<textarea class="ct-textarea" data-bind="${path}" rows="${rows}" placeholder="${esc(placeholder)}">${esc(value)}</textarea>`
      : `<input class="ct-input" type="${type}" data-bind="${path}" value="${esc(value)}" placeholder="${esc(placeholder)}">`;
    return `<label class="ct-field${wide ? " is-wide" : ""}"><span>${label}</span>${input}</label>`;
  }

  function dates(path, withCurrent) {
    const item = getPath(path);
    return `
      ${field("Başlangıç", `${path}.startDate`, { placeholder: "2023-04" })}
      <label class="ct-field"><span>Bitiş</span>
        <input class="ct-input" data-bind="${path}.endDate" value="${esc(item.endDate)}" placeholder="2024-09" ${withCurrent && item.isCurrent ? "disabled" : ""}>
      </label>
      ${withCurrent ? `<label class="ct-check"><input type="checkbox" data-bind="${path}.isCurrent" ${item.isCurrent ? "checked" : ""}> Hâlâ burada çalışıyorum</label>` : ""}`;
  }

  function achievements(path) {
    const items = getPath(path) || [];
    return `
      <div class="ct-bullets">
        <span class="ct-bullets-label">Maddeler</span>
        ${items.map((a, i) => `
          <div class="ct-bullet">
            <textarea class="ct-textarea" data-bind="${path}.${i}.text" rows="2">${esc(a.text)}</textarea>
            <button class="ct-icon-button" type="button" data-action="remove" data-path="${path}" data-index="${i}" aria-label="Maddeyi sil">×</button>
          </div>`).join("")}
        <button class="ct-link-button" type="button" data-action="add-bullet" data-path="${path}">+ Madde ekle</button>
      </div>`;
  }

  function itemTools(listPath, index, count) {
    return `
      <div class="ct-item-tools">
        <button class="ct-icon-button" type="button" data-action="move" data-path="${listPath}" data-index="${index}" data-dir="-1" ${index === 0 ? "disabled" : ""} aria-label="Yukarı taşı">↑</button>
        <button class="ct-icon-button" type="button" data-action="move" data-path="${listPath}" data-index="${index}" data-dir="1" ${index === count - 1 ? "disabled" : ""} aria-label="Aşağı taşı">↓</button>
        <button class="ct-icon-button" type="button" data-action="remove" data-path="${listPath}" data-index="${index}" data-confirm="1" aria-label="Sil">×</button>
      </div>`;
  }

  function section(title, hint, body, addAction, addLabel) {
    return `
      <section class="ct-card ct-editor-section">
        <div class="ct-section-head"><h2>${title}</h2>${hint ? `<span>${hint}</span>` : ""}</div>
        ${body}
        ${addAction ? `<button class="ct-button ct-button-secondary ct-add" type="button" data-action="${addAction}">${addLabel}</button>` : ""}
      </section>`;
  }

  function renderExperiences() {
    const list = state.profile.experiences;
    return list.map((e, i) => `
      <div class="ct-item">
        <div class="ct-item-head"><strong>${esc(e.title || "Yeni deneyim")}${e.company ? ` · ${esc(e.company)}` : ""}</strong>${itemTools("experiences", i, list.length)}</div>
        <div class="ct-grid">
          ${field("Pozisyon", `experiences.${i}.title`)}
          ${field("Şirket", `experiences.${i}.company`)}
          ${field("Yer", `experiences.${i}.location`, { placeholder: "İstanbul" })}
          <label class="ct-field"><span>Çalışma şekli</span>
            <select class="ct-input" data-bind="experiences.${i}.employmentType">
              ${EMPLOYMENT.map(([value, label]) => `<option value="${value}" ${(e.employmentType || "") === value ? "selected" : ""}>${label}</option>`).join("")}
            </select>
          </label>
          ${dates(`experiences.${i}`, true)}
        </div>
        ${achievements(`experiences.${i}.achievements`)}
      </div>`).join("") || `<p class="ct-muted">Henüz deneyim yok.</p>`;
  }

  function renderEducations() {
    const list = state.profile.educations;
    return list.map((e, i) => `
      <div class="ct-item">
        <div class="ct-item-head"><strong>${esc(e.school || "Yeni eğitim")}</strong>${itemTools("educations", i, list.length)}</div>
        <div class="ct-grid">
          ${field("Okul", `educations.${i}.school`)}
          ${field("Bölüm", `educations.${i}.field`)}
          ${field("Derece", `educations.${i}.degree`, { placeholder: "Lisans" })}
          ${field("Not ortalaması", `educations.${i}.gpa`, { placeholder: "3.12/4" })}
          ${dates(`educations.${i}`, false)}
        </div>
      </div>`).join("") || `<p class="ct-muted">Henüz eğitim yok.</p>`;
  }

  function renderProjects() {
    const list = state.profile.projects;
    return list.map((p, i) => `
      <div class="ct-item">
        <div class="ct-item-head"><strong>${esc(p.name || "Yeni proje")}</strong>${itemTools("projects", i, list.length)}</div>
        <div class="ct-grid">
          ${field("Proje adı", `projects.${i}.name`)}
          ${field("Link", `projects.${i}.url`, { placeholder: "github.com/..." })}
          ${field("Kısa tanım", `projects.${i}.description`, { wide: true })}
          ${dates(`projects.${i}`, false)}
        </div>
        ${achievements(`projects.${i}.achievements`)}
      </div>`).join("") || `<p class="ct-muted">Henüz proje yok. İş dışında yaptığın işler (okul, kişisel, açık kaynak) buraya.</p>`;
  }

  function renderSkills() {
    const skills = state.profile.skills;
    return `<div class="ct-skill-groups">${SKILL_GROUPS.map(([category, label, placeholder]) => `
      <div class="ct-skill-group">
        <span class="ct-bullets-label">${label}</span>
        <div class="ct-chips">
          ${skills.map((s, i) => s.category === category ? `
            <span class="ct-chip">${esc(s.name)}${s.level ? ` <em>${esc(s.level)}</em>` : ""}
              <button type="button" data-action="remove" data-path="skills" data-index="${i}" aria-label="${esc(s.name)} becerisini sil">×</button>
            </span>` : "").join("")}
          <input class="ct-chip-input" data-skill-input="${category}" placeholder="${esc(placeholder)}" aria-label="${label} ekle">
        </div>
      </div>`).join("")}</div>`;
  }

  function renderCertificates() {
    const list = state.profile.certificates;
    return list.map((c, i) => `
      <div class="ct-item">
        <div class="ct-item-head"><strong>${esc(c.name || "Yeni sertifika")}</strong>${itemTools("certificates", i, list.length)}</div>
        <div class="ct-grid">
          ${field("Sertifika", `certificates.${i}.name`)}
          ${field("Veren kurum", `certificates.${i}.issuer`)}
          ${field("Tarih", `certificates.${i}.date`, { placeholder: "2024-05" })}
          ${field("Link", `certificates.${i}.url`)}
        </div>
      </div>`).join("") || `<p class="ct-muted">Sertifika yok.</p>`;
  }

  function summaryLine() {
    const p = state.profile;
    const bullets = [...p.experiences, ...p.projects].reduce((sum, x) => sum + (x.achievements?.length || 0), 0);
    return [`${p.experiences.length} deneyim`, `${bullets} madde`, `${p.educations.length} eğitim`, `${p.projects.length} proje`, `${p.skills.length} beceri`].join(" · ");
  }

  function render() {
    $("#ct-editor").innerHTML = `
      <p class="ct-summary-line">${summaryLine()}</p>
      ${section("Kişisel bilgiler", "", `
        <div class="ct-grid">
          ${field("Ad soyad", "fullName")}
          ${field("Unvan", "headline", { placeholder: "Backend Developer" })}
          ${field("E-posta", "email", { type: "email" })}
          ${field("Telefon", "phone")}
          ${field("Şehir", "location", { placeholder: "İstanbul" })}
          ${field("LinkedIn", "linkedInUrl", { placeholder: "linkedin.com/in/..." })}
          ${field("GitHub", "gitHubUrl", { placeholder: "github.com/..." })}
          ${field("Web sitesi", "websiteUrl")}
          ${field("Özet", "summary", { wide: true, rows: 3, placeholder: "CV'nin başındaki kısa tanıtım paragrafı" })}
        </div>`)}
      ${section("Deneyim", "En yeniden eskiye", renderExperiences(), "add-experience", "+ Deneyim ekle")}
      ${section("Eğitim", "", renderEducations(), "add-education", "+ Eğitim ekle")}
      ${section("Projeler", "", renderProjects(), "add-project", "+ Proje ekle")}
      ${section("Beceriler", "Yazıp Enter'a bas", renderSkills())}
      ${section("Sertifikalar", "", renderCertificates(), "add-certificate", "+ Sertifika ekle")}`;
  }

  // ---- Form olayları ----

  const editor = $("#ct-editor");

  editor.addEventListener("input", event => {
    const path = event.target.dataset.bind;
    if (!path) return;
    const value = event.target.type === "checkbox" ? event.target.checked : event.target.value;
    setPath(path, value);
    setDirty(true);
    // "Hâlâ burada çalışıyorum" seçilince bitiş tarihi anlamını yitiriyor.
    if (path.endsWith(".isCurrent")) {
      const end = editor.querySelector(`[data-bind="${path.replace(".isCurrent", ".endDate")}"]`);
      end.disabled = value;
      if (value) { end.value = ""; setPath(path.replace(".isCurrent", ".endDate"), null); }
    }
  });

  editor.addEventListener("keydown", event => {
    const category = event.target.dataset.skillInput;
    if (!category || event.key !== "Enter") return;
    event.preventDefault();
    const raw = event.target.value.trim();
    if (!raw) return;
    // "İngilizce (B2)" → ad ve seviye
    const match = category === "language" ? raw.match(/^(.+?)\s*\((.+)\)$/) : null;
    state.profile.skills.push({ id: null, name: match ? match[1] : raw, category, level: match ? match[2] : null });
    setDirty(true);
    render();
    editor.querySelector(`[data-skill-input="${category}"]`)?.focus();
  });

  editor.addEventListener("click", event => {
    const button = event.target.closest("[data-action]");
    if (!button) return;
    const { action, path } = button.dataset;
    const index = Number(button.dataset.index);
    const p = state.profile;

    if (action === "remove") {
      if (button.dataset.confirm && !confirm("Bu kayıt silinsin mi?")) return;
      getPath(path).splice(index, 1);
    } else if (action === "move") {
      const list = getPath(path);
      const target = index + Number(button.dataset.dir);
      [list[index], list[target]] = [list[target], list[index]];
    } else if (action === "add-bullet") {
      getPath(path).push({ id: null, text: "", source: "manual" });
    } else if (action === "add-experience") {
      p.experiences.unshift({ id: null, company: "", title: "", isCurrent: false, achievements: [] });
    } else if (action === "add-education") {
      p.educations.push({ id: null, school: "" });
    } else if (action === "add-project") {
      p.projects.push({ id: null, name: "", achievements: [] });
    } else if (action === "add-certificate") {
      p.certificates.push({ id: null, name: "" });
    } else return;

    setDirty(true);
    render();
  });

  load();
})();
