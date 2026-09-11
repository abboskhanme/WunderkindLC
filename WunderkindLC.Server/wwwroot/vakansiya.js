/* =====================================================================================
   Wunderkind Career — Telegram Mini App (`/vakansiya`).

   ALOHIDA faylda (HTML ichida inline emas): prod CSP `script-src 'self'` inline skriptni
   bloklaydi — inline bo'lsa sahifa umuman ishlamaydi (landing.js bilan bir xil sabab).

   Autentifikatsiya: Telegram bergan `initData` satri HAR so'rovda `X-Telegram-Init-Data`
   sarlavhasida yuboriladi, server uni karyera boti tokeni bilan tekshiradi. Brauzerda
   ochilsa imzo bo'lmaydi — ilova "faqat ko'rish" rejimida ishlaydi.
   ===================================================================================== */
(function () {
  'use strict';

  var TG = window.Telegram && window.Telegram.WebApp ? window.Telegram.WebApp : null;
  var initData = TG && TG.initData ? TG.initData : '';

  /** Server javobi (bootstrap) — butun ilova shu obyektdan chizadi. */
  var state = {
    authenticated: false,
    name: '',
    phone: '',
    about: null,
    vacancies: [],
    applications: [],
    stages: [],
  };

  var currentJob = null;   // ochiq vakansiya
  var cvFile = null;       // { url, name }
  var viewStack = [];      // orqaga qaytish uchun ekranlar tarixi

  // ---------------------------------------------------------------- yordamchilar

  function $(id) { return document.getElementById(id); }

  /** HTML'ga qo'yiladigan har qanday matn shu yerdan o'tadi (XSS oldini olish). */
  function esc(s) {
    return String(s == null ? '' : s)
      .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
  }

  /** Ko'p qatorli matnni <li> ro'yxatiga aylantiradi (bo'sh qatorlar tashlanadi). */
  function lines(text) {
    return String(text || '')
      .split('\n')
      .map(function (l) { return l.replace(/^[\s•\-–*]+/, '').trim(); })
      .filter(function (l) { return l.length > 0; });
  }

  /** "2026-08-01T14:30:00" → "01.08.2026, 14:30" */
  function fmtDate(iso, withTime) {
    if (!iso) return '';
    var m = /^(\d{4})-(\d{2})-(\d{2})(?:[T ](\d{2}):(\d{2}))?/.exec(iso);
    if (!m) return iso;
    var d = m[3] + '.' + m[2] + '.' + m[1];
    return withTime && m[4] ? d + ', ' + m[4] + ':' + m[5] : d;
  }

  function haptic(type) {
    try {
      if (TG && TG.HapticFeedback) TG.HapticFeedback.impactOccurred(type || 'light');
    } catch (e) { /* muhim emas */ }
  }

  function show(el, visible) {
    if (el) el.classList.toggle('d-none', !visible);
  }

  // ---------------------------------------------------------------- API

  function apiHeaders(extra) {
    var h = extra || {};
    if (initData) h['X-Telegram-Init-Data'] = initData;
    return h;
  }

  function apiGet(path) {
    return fetch(path, { headers: apiHeaders() }).then(readJson);
  }

  function apiPost(path, body) {
    return fetch(path, {
      method: 'POST',
      headers: apiHeaders({ 'Content-Type': 'application/json' }),
      body: JSON.stringify(body),
    }).then(readJson);
  }

  function readJson(res) {
    return res.text().then(function (text) {
      var data = null;
      try { data = text ? JSON.parse(text) : null; } catch (e) { data = null; }
      if (!res.ok) {
        var msg = (data && data.message) ? data.message : 'Xatolik yuz berdi (' + res.status + ')';
        throw new Error(msg);
      }
      return data;
    });
  }

  // ---------------------------------------------------------------- Telegram mavzusi

  function applyTheme() {
    if (!TG) return;
    var p = TG.themeParams || {};
    var root = document.documentElement;
    function set(name, value) { if (value) root.style.setProperty(name, value); }

    set('--tg-bg', p.bg_color);
    set('--tg-secondary-bg', p.secondary_bg_color || p.bg_color);
    set('--tg-text', p.text_color);
    set('--tg-hint', p.hint_color);
    set('--tg-link', p.link_color);
    set('--tg-accent', p.button_color);
    if (p.button_color) {
      set('--tg-accent-soft', hexToRgba(p.button_color, 0.12));
    }
    root.setAttribute('data-bs-theme', TG.colorScheme === 'dark' ? 'dark' : 'light');
  }

  function hexToRgba(hex, alpha) {
    var m = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex || '');
    if (!m) return null;
    return 'rgba(' + parseInt(m[1], 16) + ',' + parseInt(m[2], 16) + ',' + parseInt(m[3], 16) + ',' + alpha + ')';
  }

  // ---------------------------------------------------------------- navigatsiya

  var VIEWS = ['about', 'jobs', 'apps', 'job', 'apply', 'done'];
  var TABS = ['about', 'jobs', 'apps'];

  function goto(name, push) {
    if (VIEWS.indexOf(name) < 0) return;

    if (push !== false) {
      var cur = currentView();
      if (cur && cur !== name) viewStack.push(cur);
    }

    VIEWS.forEach(function (v) { show($('view-' + v), v === name); });

    // Pastki tab faqat asosiy 3 bo'limda "faol" ko'rinadi.
    Array.prototype.forEach.call(document.querySelectorAll('.bottom-nav .nav-item'), function (btn) {
      btn.classList.toggle('active', btn.getAttribute('data-goto') === name);
    });

    var isSub = TABS.indexOf(name) < 0;
    show($('backBtn'), isSub);
    if (TG && TG.BackButton) {
      if (isSub) TG.BackButton.show(); else TG.BackButton.hide();
    }

    window.scrollTo(0, 0);
  }

  function currentView() {
    for (var i = 0; i < VIEWS.length; i++) {
      var el = $('view-' + VIEWS[i]);
      if (el && !el.classList.contains('d-none')) return VIEWS[i];
    }
    return null;
  }

  function goBack() {
    var prev = viewStack.pop();
    goto(prev || 'jobs', false);
  }

  // ---------------------------------------------------------------- 1) Biz haqimizda

  function renderAbout() {
    var a = state.about || {};

    $('brandTitle').textContent = a.title || 'Wunderkind Career';
    $('brandSub').textContent = "Bo'sh ish o'rinlari";
    document.title = (a.title ? a.title + ' — ' : '') + "Bo'sh ish o'rinlari";

    if (a.logoUrl) {
      $('brandMark').innerHTML = '<img src="' + esc(a.logoUrl) + '" alt="">';
      $('aboutLogo').innerHTML = '<img src="' + esc(a.logoUrl) + '" alt="">';
    }

    $('aboutTitle').textContent = a.title || 'Biz haqimizda';
    $('aboutTagline').textContent = a.tagline || '';
    show($('aboutTagline'), !!a.tagline);

    $('aboutText').textContent = a.about || '';
    show($('aboutTextCard'), !!a.about);

    var benefits = lines(a.benefits);
    $('benefitsList').innerHTML = benefits.map(function (b) {
      return '<li>' + esc(b) + '</li>';
    }).join('');
    show($('benefitsCard'), benefits.length > 0);

    $('addressText').textContent = a.address || '';
    show($('addressRow'), !!a.address);
    $('landmarkText').textContent = a.landmark || '';
    show($('landmarkRow'), !!a.landmark);
    $('workTimeText').textContent = a.workTime || '';
    show($('workTimeRow'), !!a.workTime);
    if (a.mapUrl) {
      $('mapBtn').href = a.mapUrl;
      show($('mapBtn'), true);
    }
    show($('addressCard'), !!(a.address || a.landmark || a.workTime || a.mapUrl));

    var contacts = [];
    if (a.phone) contacts.push({ ico: '📞', label: a.phone, href: 'tel:' + a.phone.replace(/\s/g, '') });
    if (a.phone2) contacts.push({ ico: '📞', label: a.phone2, href: 'tel:' + a.phone2.replace(/\s/g, '') });
    if (a.email) contacts.push({ ico: '✉️', label: a.email, href: 'mailto:' + a.email });
    if (a.website) contacts.push({ ico: '🌐', label: a.website.replace(/^https?:\/\//, ''), href: withProto(a.website) });
    $('contactList').innerHTML = contacts.map(function (c) {
      return '<a class="contact-item" href="' + esc(c.href) + '" target="_blank" rel="noopener">'
        + '<span>' + c.ico + '</span><span class="text-break">' + esc(c.label) + '</span></a>';
    }).join('');
    show($('contactCard'), contacts.length > 0);

    var socials = [
      { ico: '✈️', label: 'Telegram', url: a.telegram },
      { ico: '📷', label: 'Instagram', url: a.instagram },
      { ico: '👍', label: 'Facebook', url: a.facebook },
      { ico: '▶️', label: 'YouTube', url: a.youtube },
      { ico: '🎵', label: 'TikTok', url: a.tiktok },
    ].filter(function (s) { return !!s.url; });
    $('socialList').innerHTML = socials.map(function (s) {
      return '<a class="social-item" href="' + esc(withProto(s.url)) + '" target="_blank" rel="noopener">'
        + '<span class="s-ico">' + s.ico + '</span><span>' + esc(s.label) + '</span></a>';
    }).join('');
    show($('socialCard'), socials.length > 0);
  }

  /** Admin "instagram.com/..." deb yozsa ham havola ishlashi uchun. */
  function withProto(url) {
    var u = String(url || '').trim();
    if (!u) return '#';
    return /^https?:\/\//i.test(u) ? u : 'https://' + u;
  }

  // ---------------------------------------------------------------- 2) Vakansiyalar

  var EMPLOYMENT = {
    full: "To'liq bandlik",
    part: 'Yarim stavka',
    shift: 'Smenali',
    remote: 'Masofaviy',
  };

  function renderJobs() {
    var list = state.vacancies || [];
    $('jobsCount').textContent = list.length
      ? list.length + " ta faol vakansiya"
      : '';
    show($('jobsEmpty'), list.length === 0);

    $('jobsList').innerHTML = list.map(function (v) {
      var chips = '';
      chips += '<span class="chip chip-accent">💰 ' + esc(v.salary) + '</span>';
      if (v.employmentType) chips += '<span class="chip">🕒 ' + esc(EMPLOYMENT[v.employmentType] || v.employmentType) + '</span>';
      if (v.location) chips += '<span class="chip">📍 ' + esc(v.location) + '</span>';
      if (v.applied) chips += '<span class="chip chip-green">✓ Ariza yuborilgan</span>';
      else if (v.expired) chips += '<span class="chip chip-red">Muddati tugagan</span>';

      return '<div class="job-card" data-job="' + esc(v.id) + '">'
        + '<h2 class="job-title">' + esc(v.title) + '</h2>'
        + (v.department ? '<p class="job-dept">' + esc(v.department) + '</p>' : '')
        + '<div>' + chips + '</div>'
        + (v.description ? '<p class="job-desc">' + esc(v.description) + '</p>' : '')
        + '</div>';
    }).join('');
  }

  function openJob(id) {
    var v = (state.vacancies || []).filter(function (x) { return x.id === id; })[0];
    if (!v) return;
    currentJob = v;

    var chips = '<span class="chip chip-accent">💰 ' + esc(v.salary) + '</span>';
    if (v.employmentType) chips += '<span class="chip">🕒 ' + esc(EMPLOYMENT[v.employmentType] || v.employmentType) + '</span>';
    if (v.location) chips += '<span class="chip">📍 ' + esc(v.location) + '</span>';
    if (v.deadline) {
      chips += '<span class="chip ' + (v.expired ? 'chip-red' : 'chip-amber') + '">📅 '
        + (v.expired ? 'Muddati tugagan' : 'Oxirgi muddat: ' + fmtDate(v.deadline)) + '</span>';
    }

    var html = ''
      + '<div class="view-head">'
      + '<h1 class="view-title">' + esc(v.title) + '</h1>'
      + (v.department ? '<p class="view-sub">' + esc(v.department) + '</p>' : '')
      + '</div>'
      + '<div class="card soft-card"><div class="card-body">'
      + '<div>' + chips + '</div>'
      + block('Ish haqida', v.description, false)
      + block('Talablar', v.requirements, true)
      + block('Vazifalar', v.responsibilities, true)
      + block('Shart-sharoitlar', v.conditions, true)
      + '</div></div>';

    if (v.applied) {
      html += '<div class="alert alert-success mt-2 mb-0">✓ Siz bu vakansiyaga allaqachon ariza yuborgansiz. '
        + 'Holatini «Arizalarim» bo\'limidan ko\'ring.</div>'
        + '<div class="sticky-cta"><button type="button" class="btn btn-outline-primary btn-lg w-100" data-goto="apps">📄 Arizalarim</button></div>';
    } else if (v.expired) {
      html += '<div class="alert alert-warning mt-2 mb-0">Bu vakansiyaga ariza qabul qilish muddati tugagan.</div>';
    } else if (!state.authenticated) {
      html += '<div class="alert alert-info mt-2 mb-0">Ariza yuborish uchun ilovani <b>Telegram bot</b> ichidan oching.</div>';
    } else {
      html += '<div class="sticky-cta"><button type="button" class="btn btn-primary btn-lg w-100" id="applyStart">📨 Ariza topshirish</button></div>';
    }

    $('jobDetail').innerHTML = html;
    goto('job');
  }

  function block(title, text, asList) {
    var items = lines(text);
    if (items.length === 0) return '';
    var body = asList
      ? '<ul class="detail-list">' + items.map(function (i) { return '<li>' + esc(i) + '</li>'; }).join('') + '</ul>'
      : '<div class="rich-text">' + esc(items.join('\n')) + '</div>';
    return '<div class="detail-block"><h3>' + esc(title) + '</h3>' + body + '</div>';
  }

  // ---------------------------------------------------------------- 3) Arizalarim

  function renderApps() {
    var list = state.applications || [];

    show($('appsGuest'), !state.authenticated);
    show($('appsEmpty'), state.authenticated && list.length === 0);

    var badge = $('appsBadge');
    if (list.length > 0) {
      badge.textContent = list.length;
      show(badge, true);
    } else {
      show(badge, false);
    }

    $('appsList').innerHTML = list.map(function (a) {
      var pillClass = a.status === 'hired' ? 'chip-green'
        : a.status === 'rejected' ? 'chip-red'
        : 'chip-accent';

      return '<div class="app-card">'
        + '<div class="app-num">Ariza #' + esc(a.number) + ' · ' + esc(fmtDate(a.createdAt)) + '</div>'
        + '<h2 class="app-title">' + esc(a.vacancyTitle) + '</h2>'
        + '<span class="status-pill chip ' + pillClass + '">' + esc(a.statusIcon) + ' ' + esc(a.statusLabel) + '</span>'
        + '<div class="status-note">' + esc(a.statusText)
        + (a.statusNote ? '<br><b>' + esc(a.statusNote) + '</b>' : '') + '</div>'
        + roadmap(a)
        + '</div>';
    }).join('');
  }

  /** Bosqichlar yo'l-xaritasi: o'tilgan / joriy / kutilayotgan (rad etilsa — qizil yakun). */
  function roadmap(app) {
    var flow = (state.stages || []).filter(function (s) { return s.key !== 'rejected'; });
    var rejected = app.status === 'rejected';
    var current = rejected ? null : (state.stages || []).filter(function (s) { return s.key === app.status; })[0];
    var currentOrder = current ? current.order : 0;

    // Tarixdagi sanalar: bosqich → birinchi marta qachon qo'yilgan.
    var dates = {};
    (app.history || []).forEach(function (h) {
      if (!dates[h.status]) dates[h.status] = h.createdAt;
    });

    var html = flow.map(function (s) {
      var cls = 'pending', dot = '';
      if (s.order < currentOrder) { cls = 'done'; dot = '✓'; }
      else if (s.order === currentOrder) { cls = 'current'; dot = '●'; }
      else if (rejected && dates[s.key]) { cls = 'done'; dot = '✓'; }

      return '<div class="step ' + cls + '">'
        + '<span class="step-dot">' + dot + '</span>'
        + '<div class="step-body">'
        + '<div class="step-label">' + esc(s.label) + '</div>'
        + (dates[s.key] ? '<div class="step-meta">' + esc(fmtDate(dates[s.key], true)) + '</div>' : '')
        + '</div></div>';
    }).join('');

    if (rejected) {
      html += '<div class="step failed"><span class="step-dot">✕</span><div class="step-body">'
        + '<div class="step-label">Rad etildi</div>'
        + (dates.rejected ? '<div class="step-meta">' + esc(fmtDate(dates.rejected, true)) + '</div>' : '')
        + '</div></div>';
    }

    return '<div class="steps">' + html + '</div>';
  }

  // ---------------------------------------------------------------- 4) Ariza formasi

  function startApply() {
    if (!currentJob) return;
    $('applyVacancy').textContent = currentJob.title;
    $('fFullName').value = state.name || '';
    $('fPhone').value = state.phone || '';
    $('fExperience').value = '';
    $('fMotivation').value = '';
    clearCv();
    $('applyForm').classList.remove('was-validated');
    show($('applyError'), false);
    updateCount();
    goto('apply');
  }

  function updateCount() {
    $('expCount').textContent = $('fExperience').value.length;
    $('motCount').textContent = $('fMotivation').value.length;
  }

  function clearCv() {
    cvFile = null;
    $('fCv').value = '';
    show($('cvEmpty'), true);
    show($('cvPicked'), false);
    show($('cvProgress'), false);
  }

  function pickCv(file) {
    if (!file) return;
    if (!/\.pdf$/i.test(file.name)) {
      alertMsg('CV faqat PDF ko\'rinishida bo\'lishi kerak.');
      $('fCv').value = '';
      return;
    }
    if (file.size > 10000000) {
      alertMsg('Fayl 10 MB dan katta bo\'lmasligi kerak.');
      $('fCv').value = '';
      return;
    }

    show($('cvEmpty'), false);
    show($('cvPicked'), false);
    show($('cvProgress'), true);

    var fd = new FormData();
    fd.append('file', file);
    fetch('/api/career/cv', { method: 'POST', headers: apiHeaders(), body: fd })
      .then(readJson)
      .then(function (data) {
        cvFile = { url: data.url, name: data.name || file.name };
        $('cvName').textContent = cvFile.name;
        $('cvSize').textContent = (file.size / 1024 / 1024).toFixed(2) + ' MB';
        show($('cvProgress'), false);
        show($('cvPicked'), true);
        haptic('light');
      })
      .catch(function (err) {
        clearCv();
        alertMsg(err.message || 'CV yuklanmadi.');
      });
  }

  function alertMsg(text) {
    if (TG && TG.showAlert) { try { TG.showAlert(text); return; } catch (e) { /* pastdagi zaxira */ } }
    var box = $('applyError');
    box.textContent = text;
    show(box, true);
  }

  function submitApply(e) {
    e.preventDefault();
    show($('applyError'), false);

    var name = $('fFullName').value.trim();
    var phone = $('fPhone').value.trim();
    var motivation = $('fMotivation').value.trim();

    $('applyForm').classList.add('was-validated');
    if (name.length < 3 || phone.length < 7 || motivation.length < 10) {
      alertMsg('Yulduzcha bilan belgilangan maydonlarni to\'liq to\'ldiring.');
      return;
    }

    var btn = $('applySubmit');
    btn.disabled = true;
    btn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Yuborilmoqda…';

    apiPost('/api/career/apply', {
      vacancyId: currentJob.id,
      fullName: name,
      phone: phone,
      experience: $('fExperience').value.trim(),
      motivation: motivation,
      cvUrl: cvFile ? cvFile.url : '',
      cvName: cvFile ? cvFile.name : '',
    })
      .then(function (app) {
        state.applications.unshift(app);
        currentJob.applied = true;
        state.name = name;
        state.phone = phone;
        renderJobs();
        renderApps();
        $('doneText').textContent = '«' + currentJob.title + '» vakansiyasiga arizangiz #'
          + app.number + ' raqami bilan qabul qilindi. Har bir bosqich o\'zgarishi haqida '
          + 'botda xabar olasiz.';
        viewStack = [];
        goto('done', false);
        haptic('medium');
      })
      .catch(function (err) {
        alertMsg(err.message || 'Ariza yuborilmadi. Qayta urinib ko\'ring.');
      })
      .finally(function () {
        btn.disabled = false;
        btn.innerHTML = '📨 Arizani yuborish';
      });
  }

  // ---------------------------------------------------------------- yuklash

  function load() {
    show($('loading'), true);
    show($('errorBox'), false);
    show($('app'), false);
    show($('bottomNav'), false);

    apiGet('/api/career/bootstrap')
      .then(function (data) {
        state = data;
        renderAbout();
        renderJobs();
        renderApps();

        show($('loading'), false);
        show($('app'), true);
        show($('bottomNav'), true);
        goto('about', false);
      })
      .catch(function (err) {
        show($('loading'), false);
        $('errorText').textContent = err.message || 'Internet aloqasini tekshirib, qayta urinib ko\'ring.';
        show($('errorBox'), true);
      });
  }

  // ---------------------------------------------------------------- hodisalar

  document.addEventListener('click', function (e) {
    if (!e.target || !e.target.closest) return;
    // Fayl tanlagichning O'ZI: `input.click()` ham click hodisasini ko'taradi — quyidagi
    // `#cvBox` shohobchasi uni qayta chaqirib cheksiz rekursiyaga tushib qolmasin.
    if (e.target.id === 'fCv') return;

    var goBtn = e.target.closest('[data-goto]');
    if (goBtn) {
      var target = goBtn.getAttribute('data-goto');
      if (TABS.indexOf(target) >= 0) viewStack = [];
      goto(target, false);
      haptic('light');
      return;
    }

    var card = e.target.closest('[data-job]');
    if (card) {
      openJob(card.getAttribute('data-job'));
      haptic('light');
      return;
    }

    if (e.target.closest('#applyStart')) { startApply(); return; }
    if (e.target.closest('#cvClear')) { clearCv(); return; }
    if (e.target.closest('#cvBox')) { $('fCv').click(); return; }
    if (e.target.closest('#backBtn')) { goBack(); return; }
    if (e.target.closest('#retryBtn')) { load(); return; }
  });

  $('fCv').addEventListener('change', function () { pickCv(this.files && this.files[0]); });
  $('applyForm').addEventListener('submit', submitApply);
  $('fExperience').addEventListener('input', updateCount);
  $('fMotivation').addEventListener('input', updateCount);

  // ---------------------------------------------------------------- ishga tushirish

  if (TG) {
    try {
      TG.ready();
      TG.expand();
      applyTheme();
      TG.onEvent('themeChanged', applyTheme);
      if (TG.BackButton) TG.BackButton.onClick(goBack);
    } catch (e) { /* eski Telegram mijozlari — ilova baribir ishlaydi */ }
  }

  load();
})();
