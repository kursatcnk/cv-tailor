// Oturum ve API katmanı. Sayfalar fetch'i kendileri çağırmıyor, istekler hep buradan geçiyor.
// Token httpOnly çerezde, JS göremiyor. Burada sadece kullanıcı bilgisi ve oturumun bitiş zamanı tutuluyor.
window.CvTailorSession = (() => {
  "use strict";

  const USER_KEY = "cvtailor.user";
  const EXPIRES_KEY = "cvtailor.expiresAt";
  const FLASH_KEY = "cvtailor.flash";

  // Beni hatırla → localStorage, değilse sessionStorage (sekme kapanınca gidiyor).
  function stores() {
    const list = [];
    try { list.push(window.localStorage); } catch { /* depolama kapalı olabilir */ }
    try { list.push(window.sessionStorage); } catch { /* depolama kapalı olabilir */ }
    return list;
  }

  function read(key) {
    for (const store of stores()) {
      try { const value = store.getItem(key); if (value) return value; } catch { /* yok say */ }
    }
    return null;
  }

  let csrfToken = null;

  function clearSession() {
    csrfToken = null;
    for (const store of stores()) {
      try { store.removeItem(USER_KEY); store.removeItem(EXPIRES_KEY); } catch { /* yok say */ }
    }
  }

  function saveSession(user, expiresAt, remember) {
    clearSession();
    const store = remember ? stores()[0] : stores()[1];
    try {
      store?.setItem(USER_KEY, JSON.stringify(user ?? {}));
      if (expiresAt) store?.setItem(EXPIRES_KEY, expiresAt);
    } catch { /* depolama doluysa oturum sadece bu sayfada yaşar */ }
  }

  // Ad değişince sadece kullanıcı bilgisini güncelle.
  function updateUser(user) {
    for (const store of stores()) {
      try { if (store.getItem(USER_KEY)) store.setItem(USER_KEY, JSON.stringify(user ?? {})); } catch { /* yok say */ }
    }
  }

  // Süresi dolmuşsa hiç istek atmadan çıkış. Çerez yine de geçersizse ilk istekte 401 gelir.
  function isSignedIn() {
    if (!read(USER_KEY)) return false;
    const expiresAt = Date.parse(read(EXPIRES_KEY) || "");
    if (Number.isFinite(expiresAt) && expiresAt <= Date.now()) { clearSession(); return false; }
    return true;
  }

  function getUser() {
    try { return JSON.parse(read(USER_KEY) || "null"); } catch { return null; }
  }

  // app.html'den auth/sign-in.html, auth sayfalarının içinden sign-in.html
  function loginUrl() {
    return location.pathname.includes("/auth/") ? "sign-in.html" : "auth/sign-in.html";
  }

  function requireAuth(url = loginUrl()) {
    if (!isSignedIn()) window.location.replace(url);
  }

  // Sayfa değişirken bırakılan tek seferlik mesaj (örn. şifre sıfırlandıktan sonra giriş ekranında).
  function setFlash(message, type = "success") {
    try { sessionStorage.setItem(FLASH_KEY, JSON.stringify({ message, type })); } catch { /* yok say */ }
  }

  function takeFlash() {
    try {
      const value = JSON.parse(sessionStorage.getItem(FLASH_KEY) || "null");
      sessionStorage.removeItem(FLASH_KEY);
      return value;
    } catch { return null; }
  }

  // CSRF token'ı oturuma bağlı; giriş/çıkışta sıfırlanıyor, ilk değiştirici istekte yeniden alınıyor.
  async function loadCsrfToken() {
    if (csrfToken) return csrfToken;
    try {
      const response = await fetch("/api/csrf", { credentials: "same-origin", headers: { "Accept": "application/json" } });
      csrfToken = response.ok ? (await response.json())?.token ?? null : null;
    } catch { csrfToken = null; }
    return csrfToken;
  }

  async function send(path, method, body) {
    const headers = { "Accept": "application/json" };
    if (body !== undefined) headers["Content-Type"] = "application/json";
    if (method !== "GET") {
      const token = await loadCsrfToken();
      if (token) headers["X-CSRF-TOKEN"] = token;
    }
    return fetch(path, { method, headers, credentials: "same-origin", body: body === undefined ? undefined : JSON.stringify(body) });
  }

  // Her istek { ok, status, data } dönüyor, çağıran taraf fetch detaylarıyla uğraşmıyor.
  async function request(path, { method = "GET", body } = {}) {
    const signedIn = isSignedIn();
    let response;
    try {
      response = await send(path, method, body);
      // Token başka sekmede girişle değişmiş olabilir; bir kez yenileyip tekrar dene.
      if (response.status === 400 && method !== "GET") {
        const peek = await response.clone().json().catch(() => null);
        if (peek?.code === "csrf_invalid") { csrfToken = null; response = await send(path, method, body); }
      }
    } catch {
      return { ok: false, status: 0, data: { message: "Sunucuya ulaşılamadı. API çalışıyor mu?" } };
    }

    // Oturum vardı ama 401 geldi: süresi dolmuş ya da hesap silinmiş. Girişe dön.
    if (response.status === 401 && signedIn) {
      clearSession();
      setFlash("Oturumun sona erdi. Lütfen tekrar giriş yap.", "error");
      window.location.replace(loginUrl());
    }
    const data = response.status === 204 ? null : await response.json().catch(() => null);
    // 429 iki şey olabilir: aylık kota (code: quota_exceeded, içinde usage var) ya da dakikalık istek sınırı (boş gövde).
    if (response.status === 429 && data?.code !== "quota_exceeded") {
      return { ok: false, status: 429, data: { message: "Çok hızlı istek gönderdin. Bir dakika bekleyip tekrar dene." } };
    }
    return { ok: response.ok, status: response.status, data };
  }

  // Çerezi sunucu siliyor (httpOnly, JS dokunamıyor). İstek başarısız olsa da yerel oturum temizleniyor.
  async function signOut() {
    try { await fetch("/api/auth/logout", { method: "POST", credentials: "same-origin" }); } catch { /* yok say */ }
    clearSession();
  }

  const api = {
    get: path => request(path),
    post: (path, body = {}) => request(path, { method: "POST", body }),
    put: (path, body = {}) => request(path, { method: "PUT", body }),
    del: path => request(path, { method: "DELETE" })
  };

  return { isSignedIn, getUser, updateUser, saveSession, clearSession, signOut, requireAuth, request, api, setFlash, takeFlash };
})();
