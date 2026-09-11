/* Sertifikatlar sahifasi skripti — ALOHIDA faylda, chunki prod CSP (script-src 'self')
   HTML ichidagi inline skriptni bloklaydi. Ilgari butun mantiq sahifaning O'ZIDA <script>
   tegida turardi: lokalda (CSP yo'q) ishlar, PRODDA esa sertifikatlar yuklanmas, filtr va
   qidiruv o'lik bo'lar va ariza formasi (POST /api/public/leads) umuman yubormasdi.
   Uslub landing.js bilan bir xil: ES5 (var/function), tashqi kutubxonasiz. */
(function(){
  'use strict';

  // Mobil menyu — landing.js dagi bilan AYNAN bir xil mantiq. Ilgari bu sahifada hamburger
  // umuman yo'q edi: telefonda 6 ta nav havolasi header'ga sig'may, landingdan o'tganda nav
  // butunlay boshqacha ko'rinardi.
  var navToggle = document.getElementById('navToggle');
  var navLinks = document.getElementById('navLinks');
  if (navToggle && navLinks) {
    navToggle.addEventListener('click', function(){
      navLinks.classList.toggle('show');
    });
    // Havola bosilganda menyu yopiladi (aks holda ochiq menyu sahifani to'sib turardi)
    var navItems = navLinks.querySelectorAll('a');
    Array.prototype.forEach.call(navItems, function(link){
      link.addEventListener('click', function(){
        navLinks.classList.remove('show');
      });
    });
  }


  // ─────────────────────────── NAV: "Yutuqlar" ochiladigan menyusi ───────────────────────────
  // Desktopda hover ham ochadi (CSS), lekin sensorli ekranda hover YO'Q — shu sabab bosish
  // bilan ham ochiladi. Mobil menyuda (<=860px) ochilish umuman kerak emas: u yerda ikkita
  // havola sarlavha ostida ketma-ket turadi (CSS), tugma esa `pointer-events: none`.
  (function initNavDropdowns() {
    var drops = document.querySelectorAll('[data-nav-drop]');
    if (drops.length === 0) return;

    function closeAll(except) {
      for (var i = 0; i < drops.length; i++) {
        if (drops[i] === except) continue;
        drops[i].classList.remove('open');
        var b = drops[i].querySelector('.nav-drop-toggle');
        if (b) b.setAttribute('aria-expanded', 'false');
      }
    }

    for (var i = 0; i < drops.length; i++) {
      (function(drop) {
        var btn = drop.querySelector('.nav-drop-toggle');
        if (!btn) return;
        btn.addEventListener('click', function(e) {
          e.preventDefault();
          e.stopPropagation();
          var willOpen = !drop.classList.contains('open');
          closeAll(drop);
          drop.classList.toggle('open', willOpen);
          btn.setAttribute('aria-expanded', willOpen ? 'true' : 'false');
        });
        // Havola bosilganda menyu yopiladi (bir xil sahifa ichida ham — faqat hash o'zgaradi).
        var links = drop.querySelectorAll('.nav-drop-link');
        for (var k = 0; k < links.length; k++) {
          links[k].addEventListener('click', function() { closeAll(null); });
        }
      })(drops[i]);
    }

    document.addEventListener('click', function() { closeAll(null); });
    document.addEventListener('keydown', function(e) {
      if (e.key === 'Escape') closeAll(null);
    });
  })();

  // ⚠️ NAMUNA (soxta) SERTIFIKATLAR YO'Q. Ilgari bu yerda 4 ta o'ylab topilgan o'quvchi
  // (ism, ball, boshqa odamning sertifikat surati) qattiq kodlangan va sahifa ochilishi
  // bilan AYNAN shular chizilardi — ya'ni CMS'ga hech narsa kiritilmagan markaz ommaviy
  // saytda to'qib chiqarilgan natijalarni ko'rsatib turardi. Endi ro'yxat bo'sh boshlanadi
  // va FAQAT `GET /api/public/landing-data` javobi bilan to'ladi.
  var allCertificates = [];

  // Yuklanish holati: 'loading' | 'ready' | 'error'.
  // Sabab: uch holat UCHTA xil xabar talab qiladi va ular ARALASHMASLIGI kerak —
  //   loading — javob hali kelmagan (bir zumga "topilmadi" chaqnab ketmasin);
  //   ready   — javob keldi (bo'sh bo'lsa "hali joylanmagan", filtr bo'sh bo'lsa "topilmadi");
  //   error   — so'rov yiqildi. Bu holat ATAYIN bo'sh holatdan ajratilgan: tarmoq xatosini
  //             "sertifikat yo'q" deb ko'rsatish foydalanuvchini chalg'itardi.
  var loadState = 'loading';

  var currentFilter = 'all';
  var currentSearch = '';
  var currentLeadNote = 'Sertifikatlar sahifasidan';

  var certsGrid = document.getElementById('certsGrid');
  var searchInput = document.getElementById('certSearchInput');
  var filterTabs = document.querySelectorAll('.filter-tab');

  var leadModalBackdrop = document.getElementById('leadModalBackdrop');
  var leadModalClose = document.getElementById('leadModalClose');
  var leadForm = document.getElementById('leadForm');
  var leadFormMsg = document.getElementById('leadFormMsg');
  var leadSubjectSelect = document.getElementById('leadSubject');
  var leadNameInput = document.getElementById('leadName');
  var leadPhoneInput = document.getElementById('leadPhone');
  var openHeaderLeadBtn = document.getElementById('openHeaderLeadBtn');

  var resultModalBackdrop = document.getElementById('resultModalBackdrop');
  var resultModalClose = document.getElementById('resultModalClose');
  var resultModalImg = document.getElementById('resultModalImg');
  var resultModalStudentName = document.getElementById('resultModalStudentName');
  var resultModalOverall = document.getElementById('resultModalOverall');
  var resultModalListening = document.getElementById('resultModalListening');
  var resultModalReading = document.getElementById('resultModalReading');
  var resultModalWriting = document.getElementById('resultModalWriting');
  var resultModalSpeaking = document.getElementById('resultModalSpeaking');
  var resultModalCategory = document.getElementById('resultModalCategory');
  var resultModalCta = document.getElementById('resultModalCta');

  // CMS'dan matn ham, RAQAM ham keladi (masalan overallScore: 8.5) — xom raqamda `.replace`
  // metodi yo'q va TypeError butun ro'yxatni chizilmay qoldirardi, shuning uchun String().
  function escapeHtml(str) {
    if (str === null || str === undefined || str === '') return '';
    return String(str).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  }

  function digitsOnly(str) {
    return (str || '').replace(/\D/g, '');
  }

  // Modal ochilganda sahifa ORQADA sirg'anmasin (landing.js dagi bilan bir xil uslub).
  function showBackdrop(el) {
    if (!el) return;
    el.classList.add('show');
    document.body.style.overflow = 'hidden';
  }
  function hideBackdrop(el) {
    if (!el) return;
    el.classList.remove('show');
    document.body.style.overflow = '';
  }

  // ===================== ARIZA MODALI =====================

  function openLeadModal(subject, note) {
    if (subject && leadSubjectSelect) leadSubjectSelect.value = subject;
    currentLeadNote = note || 'Sertifikatlar sahifasidan';
    showBackdrop(leadModalBackdrop);
  }
  function closeLeadModal() { hideBackdrop(leadModalBackdrop); }

  if (openHeaderLeadBtn) {
    openHeaderLeadBtn.addEventListener('click', function() {
      openLeadModal('', 'Sertifikatlar sahifasi tepasidan');
    });
  }
  if (leadModalClose) leadModalClose.addEventListener('click', closeLeadModal);
  if (leadModalBackdrop) {
    leadModalBackdrop.addEventListener('click', function(e) {
      if (e.target === leadModalBackdrop) closeLeadModal();
    });
  }

  // ===================== SERTIFIKAT MODALI =====================

  // Qiymat yo'q bo'lsa "—". ⚠️ Ilgari `overallScore` uchun '8.5' zaxira turardi — ball
  // kiritilmagan sertifikat SOXTA natija bilan ochilardi.
  function dashIfEmpty(v) {
    return (v === null || v === undefined || v === '') ? '—' : String(v);
  }

  function openResultModal(c) {
    if (resultModalStudentName) resultModalStudentName.textContent = c.studentName || 'O\'QUVCHI';
    if (resultModalOverall) resultModalOverall.textContent = dashIfEmpty(c.overallScore);
    if (resultModalListening) resultModalListening.textContent = dashIfEmpty(c.listening);
    if (resultModalReading) resultModalReading.textContent = dashIfEmpty(c.reading);
    if (resultModalWriting) resultModalWriting.textContent = dashIfEmpty(c.writing);
    if (resultModalSpeaking) resultModalSpeaking.textContent = dashIfEmpty(c.speaking);
    // ⚠️ Rasm bo'lmasa BOSHQA o'quvchining sertifikat surati ko'rsatilmaydi — `<img>` butunlay
    // yashiriladi (bo'sh `src` da brauzer "singan rasm" belgisini chizardi).
    if (resultModalImg) {
      if (c.imageUrl) {
        resultModalImg.src = c.imageUrl;
        resultModalImg.style.display = '';
      } else {
        resultModalImg.removeAttribute('src');
        resultModalImg.style.display = 'none';
      }
    }
    if (resultModalCategory) {
      // Tur ham, toifa ham bo'lmasa qavs ichida uydirma "Xalqaro" yozilmaydi.
      var parts = [];
      if (c.certType) parts.push(String(c.certType));
      if (c.category) parts.push('(' + String(c.category) + ')');
      resultModalCategory.textContent = parts.length ? parts.join(' ') : 'SERTIFIKAT';
    }
    showBackdrop(resultModalBackdrop);
  }
  function closeResultModal() { hideBackdrop(resultModalBackdrop); }

  if (resultModalClose) resultModalClose.addEventListener('click', closeResultModal);
  if (resultModalBackdrop) {
    resultModalBackdrop.addEventListener('click', function(e) {
      if (e.target === resultModalBackdrop) closeResultModal();
    });
  }
  if (resultModalCta) {
    resultModalCta.addEventListener('click', function() {
      var who = resultModalStudentName ? resultModalStudentName.textContent : '';
      closeResultModal();
      openLeadModal('IELTS', 'Sertifikatlar sahifasidan: ' + who + ' natijasi orqali');
    });
  }

  // Escape ikkala modalni ham yopadi — foydalanuvchi "✕" ni qidirib qolmasin.
  document.addEventListener('keydown', function(e) {
    if (e.key !== 'Escape') return;
    if (resultModalBackdrop && resultModalBackdrop.classList.contains('show')) closeResultModal();
    else if (leadModalBackdrop && leadModalBackdrop.classList.contains('show')) closeLeadModal();
  });

  // ===================== RO'YXAT =====================

  /**
    * OLIYGOHGA KIRISH natijasimi? Yagona ta'rif — filtrlar shu funksiyadan foydalanadi, aks
    * holda "Oliygohga kirishlar" va "Sertifikatlar" kesimlari ustma-ust tushib, bitta yozuv
    * ikkala ro'yxatda ham ko'rinishi mumkin edi.
    * CMS'dagi TOIFA (yoki tur) asosiy manba; sarlavhadagi kalit so'zlar esa toifa
    * belgilanmasdan kiritilgan eski yozuvlar uchun (xalqaro/milliy filtrlaridagi bilan bir xil uslub).
    */
  function isOliygoh(c) {
    var type = (c.certType || '').toLowerCase();
    var title = (c.title || '').toLowerCase();
    var cat = (c.category || '').toLowerCase();
    return cat.indexOf('oliygoh') !== -1 || type.indexOf('oliygoh') !== -1 ||
           title.indexOf('oliygoh') !== -1 || title.indexOf('universitet') !== -1 ||
           title.indexOf('grant') !== -1;
  }

  function matchesFilter(c) {
    var catLower = (currentFilter || 'all').toLowerCase();
    if (catLower === 'all') return true;

    var typeLower = (c.certType || '').toLowerCase();
    var titleLower = (c.title || '').toLowerCase();
    var cCategoryLower = (c.category || '').toLowerCase();

    var oliygoh = isOliygoh(c);
    if (catLower === 'oliygoh') return oliygoh;
    // "Sertifikatlar" = oliygohga kirishlardan BOSHQA hammasi (navdagi ikkinchi band).
    if (catLower === 'sertifikat') return !oliygoh;
    // ⚠️ Oliygoh natijasi xalqaro/milliy kesimlariga TUSHMAYDI: turi "IELTS" bo'lib qolgan
    // bo'lsa ham u sertifikat emas, kirish natijasi.
    if (oliygoh) return false;

    if (catLower === 'xalqaro') {
      return typeLower.indexOf('ielts') !== -1 || typeLower.indexOf('multi') !== -1 || typeLower.indexOf('cefr') !== -1 ||
             titleLower.indexOf('ielts') !== -1 || titleLower.indexOf('multi') !== -1 || cCategoryLower.indexOf('xalqaro') !== -1;
    }
    if (catLower === 'milliy') {
      return typeLower.indexOf('sat') !== -1 || typeLower.indexOf('milli') !== -1 ||
             titleLower.indexOf('sat') !== -1 || titleLower.indexOf('milli') !== -1 || cCategoryLower.indexOf('milliy') !== -1;
    }
    return cCategoryLower === catLower || typeLower.indexOf(catLower) !== -1 || titleLower.indexOf(catLower) !== -1;
  }

  function matchesSearch(c) {
    var s = (currentSearch || '').trim().toLowerCase();
    if (!s) return true;
    // Qiymat raqam bo'lishi mumkin — String() bo'lmasa .toLowerCase() yiqilardi.
    var fields = [c.studentName, c.overallScore, c.title, c.certType];
    for (var i = 0; i < fields.length; i++) {
      var v = fields[i];
      if (v !== null && v !== undefined && String(v).toLowerCase().indexOf(s) !== -1) return true;
    }
    return false;
  }

  // Bo'sh/yuklanish/xato holatlari uchun bitta joy — matnlar ro'yxat bilan aralashmasin.
  function stateHtml(title, sub) {
    return '<div style="grid-column:1/-1; text-align:center; padding:60px 0; color:var(--muted); font-size:16px;">' +
             '<div style="font-weight:700; color:#e5e7eb; margin-bottom:6px;">' + escapeHtml(title) + '</div>' +
             (sub ? '<div style="font-size:14px;">' + escapeHtml(sub) + '</div>' : '') +
           '</div>';
  }

  function renderGrid() {
    if (!certsGrid) return;
    certsGrid.innerHTML = '';

    // 1) Javob hali kelmagan — "topilmadi" YOZILMAYDI (aks holda sahifa ochilishida bir zumga
    //    "Sertifikatlar topilmadi" chaqnab, keyin ro'yxat paydo bo'lardi).
    if (loadState === 'loading') {
      certsGrid.innerHTML = stateHtml('Sertifikatlar yuklanmoqda...', '');
      return;
    }

    // 2) So'rov yiqildi — bu BO'SH holat EMAS. Jimgina "topilmadi" desak, foydalanuvchi
    //    markazda sertifikat yo'q deb o'ylardi.
    if (loadState === 'error') {
      certsGrid.innerHTML = stateHtml(
        'Sertifikatlarni yuklab bo\'lmadi',
        'Internet aloqasini tekshirib, sahifani yangilang.');
      return;
    }

    // 3) CMS'da umuman yozuv yo'q — filtr/qidiruvga bog'liq bo'lmagan bo'sh holat.
    if (allCertificates.length === 0) {
      certsGrid.innerHTML = stateHtml(
        'Hozircha sertifikatlar joylanmagan',
        'Tez orada o\'quvchilarimizning natijalari shu yerda e\'lon qilinadi.');
      return;
    }

    var list = allCertificates.filter(function(c) {
      return matchesFilter(c) && matchesSearch(c);
    });

    // 4) Ma'lumot BOR, lekin filtr/qidiruvga mos kelmadi — foydalanuvchi nima qilishini bilsin.
    if (list.length === 0) {
      certsGrid.innerHTML = stateHtml(
        'Mos sertifikatlar topilmadi',
        'Qidiruv so\'rovini yoki toifani o\'zgartirib ko\'ring.');
      return;
    }

    list.forEach(function(c) {
      var card = document.createElement('div');
      card.className = 'result-card';

      // ⚠️ Rasm YO'Q bo'lsa boshqa o'quvchining sertifikati (ilgari '/img/certificates/cert-1.jpg')
      // qo'yilmaydi — rasm bloki umuman chizilmaydi.
      var imgHtml = c.imageUrl
        ? '<div class="result-cert-img-wrap">' +
            '<img src="' + escapeHtml(c.imageUrl) + '" alt="' + escapeHtml(c.studentName || 'Sertifikat') + '" loading="lazy">' +
          '</div>'
        : '';

      // ⚠️ Ball/tur YO'Q bo'lsa uydirma '8.5' / 'OVERALL' yozilmaydi — quti chizilmaydi.
      var overallHtml = (c.overallScore || c.certType)
        ? '<div class="result-overall-box">' +
            '<span class="result-overall-label">' + escapeHtml(c.certType || '') + '</span>' +
            '<span class="result-overall-score">' + escapeHtml(c.overallScore || '') + '</span>' +
          '</div>'
        : '';

      // Bo'lim ballari: birortasi ham bo'lmasa qator umuman chizilmaydi (to'rtta "-" dan ko'ra
      // hech narsa ko'rsatmagan tushunarliroq).
      var rowsHtml = '';
      [['List:', c.listening], ['Read:', c.reading], ['Writ:', c.writing], ['Spea:', c.speaking]].forEach(function(pair) {
        if (pair[1] === null || pair[1] === undefined || pair[1] === '') return;
        rowsHtml += '<div class="result-breakdown-row"><span>' + pair[0] + '</span> <span>' + escapeHtml(pair[1]) + '</span></div>';
      });
      var breakdownHtml = rowsHtml ? '<div class="result-breakdown">' + rowsHtml + '</div>' : '';

      // ⚠️ Har bir CMS qiymati escape qilinadi: matnda "<" bo'lsa kartochka markup'i buzilardi.
      card.innerHTML =
        imgHtml +
        '<div class="result-student-name">' + (escapeHtml(c.studentName) || 'O\'QUVCHI') + '</div>' +
        '<div class="result-card-bottom">' +
          overallHtml +
          breakdownHtml +
        '</div>';

      card.style.cursor = 'pointer';
      card.addEventListener('click', function() { openResultModal(c); });
      certsGrid.appendChild(card);
    });
  }

  filterTabs.forEach(function(tab) {
    tab.addEventListener('click', function() {
      setFilter(tab.getAttribute('data-filter') || 'all');
    });
  });

  /** Filtrni qo'yadi va mos chipni belgilaydi (bitta joy — chip va hash ayri ketmasin). */
  function setFilter(value) {
    currentFilter = value || 'all';
    filterTabs.forEach(function(t) {
      t.classList.toggle('active', (t.getAttribute('data-filter') || 'all') === currentFilter);
    });
    renderGrid();
  }

  // NAVDAGI "Yutuqlar" bandlari shu sahifaga hash bilan keladi: `#oliygoh` → oliygohga
  // kirishlar, `#sertifikatlar` → sertifikatlar. Noma'lum hash e'tiborsiz qoldiriladi
  // (filtr "Barchasi" bo'lib qolaveradi) — sahifa hech qachon bo'sh ochilmasin.
  function applyHashFilter() {
    var h = (window.location.hash || '').replace('#', '').toLowerCase();
    if (h === 'oliygoh') setFilter('Oliygoh');
    else if (h === 'sertifikatlar' || h === 'sertifikat') setFilter('Sertifikat');
  }
  applyHashFilter();
  // Sahifa ALLAQACHON ochiq bo'lganda navdan bosilsa faqat hash o'zgaradi — sahifa qayta
  // yuklanmaydi, shuning uchun o'zgarish alohida kuzatiladi.
  window.addEventListener('hashchange', applyHashFilter);

  if (searchInput) {
    searchInput.addEventListener('input', function() {
      currentSearch = searchInput.value;
      renderGrid();
    });
  }

  // Avval YUKLANISH holati chiziladi (soxta zaxira ro'yxat emas) — sahifa bo'sh turmasin,
  // lekin hech kimning nomi va bali o'ylab topilmasin.
  renderGrid();

  fetch('/api/public/landing-data').then(function(res) {
    // ⚠️ `!res.ok` — bu XATO, "sertifikat yo'q" emas. Ilgari u `null` ga aylantirilib jimgina
    // yutilardi va ekranda soxta zaxira ro'yxat qolib ketardi.
    if (!res.ok) throw new Error('HTTP ' + res.status);
    return res.json();
  }).then(function(data) {
    // Bo'sh massiv — TO'LIQ HUQUQLI javob: ro'yxat bo'shatiladi va "hali joylanmagan"
    // holati ko'rsatiladi (ilgari `length > 0` sharti tufayli eski/soxta ro'yxat qolardi).
    allCertificates = (data && data.certificates) ? data.certificates : [];
    loadState = 'ready';
    renderGrid();
  }).catch(function() {
    loadState = 'error';
    renderGrid();
  });

  // Footerdagi yil — qo'lda yozilgan sana eskirib qolmasin.
  var certYear = document.getElementById('certYear');
  if (certYear) certYear.textContent = String(new Date().getFullYear());

  // ===================== ARIZA YUBORISH =====================

  function setLeadMsg(text, color) {
    if (!leadFormMsg) return;
    leadFormMsg.textContent = text;
    leadFormMsg.style.color = color;
  }

  if (leadForm) {
    var leadSubmitBtn = leadForm.querySelector('button[type="submit"]');

    leadForm.addEventListener('submit', function(e) {
      e.preventDefault();

      var name = leadNameInput ? leadNameInput.value.trim() : '';
      var phone = leadPhoneInput ? leadPhoneInput.value.trim() : '';
      var subject = leadSubjectSelect ? leadSubjectSelect.value : '';

      // Ilgari `if (!name || !phone) return;` — forma JIM turib qolardi va foydalanuvchi
      // nima noto'g'ri ekanini bilmasdi.
      if (!name) {
        setLeadMsg('Iltimos, ismingizni kiriting.', '#f87171');
        if (leadNameInput) leadNameInput.focus();
        return;
      }
      if (digitsOnly(phone).length < 9) {
        setLeadMsg('Iltimos, to\'g\'ri telefon raqam kiriting (kamida 9 ta raqam).', '#f87171');
        if (leadPhoneInput) leadPhoneInput.focus();
        return;
      }

      setLeadMsg('Yuborilmoqda...', '#38bdf8');
      if (leadSubmitBtn) leadSubmitBtn.disabled = true;

      fetch('/api/public/leads', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ fullName: name, phone: phone, subject: subject, note: currentLeadNote })
      }).then(function(res) {
        if (res.ok) {
          setLeadMsg('✅ Arizangiz muvaffaqiyatli yuborildi!', '#4ade80');
          leadForm.reset();
          setTimeout(closeLeadModal, 2000);
          return null;
        }
        // 400/429 da server sababni yozadi (masalan "juda tez-tez yuborilmoqda") — uni ko'rsatamiz.
        return res.json().catch(function() { return null; }).then(function(data) {
          setLeadMsg('❌ ' + ((data && data.message) ? data.message : 'Xatolik yuz berdi. Qayta urinib ko\'ring.'), '#f87171');
        });
      }).catch(function() {
        setLeadMsg('❌ Xatolik yuz berdi.', '#f87171');
      }).finally(function() {
        if (leadSubmitBtn) leadSubmitBtn.disabled = false;
      });
    });
  }

  // ---- BREND (logo + nom) ----
  // Landing bilan bir xil manba: GET /api/public/brand (CenterMeta). Ilgari bu sahifa uni
  // umuman chaqirmasdi — nav'da faqat qattiq yozilgan matn turardi va Sozlamalardagi logo
  // bu yerda hech qachon ko'rinmasdi.
  // Rasm AVVAL yuklab ko'riladi: manzil buzuq bo'lsa "singan rasm" chiqmasin (inline
  // `onerror=` prod CSP tufayli mumkin emas). Yuklanmasa belgi bo'sh qoladi va CSS
  // (.logo .mark:empty) uni chizmaydi — faqat "Wunderkind" matni ko'rinadi.
  fetch('/api/public/brand').then(function(res){
    return res.ok ? res.json() : null;
  }).then(function(brand){
    if (!brand) return;
    if (brand.name) {
      var nameEl = document.getElementById('brandName');
      if (nameEl) nameEl.textContent = brand.name;
    }
    if (!brand.logoUrl) return;
    var probe = new Image();
    probe.onload = function(){
      var mark = document.getElementById('brandMark');
      // alt="" — markaz nomi yonida matn bilan yozilgan, takrorlash shart emas
      if (mark) mark.innerHTML = '<img src="' + escapeHtml(brand.logoUrl) + '" alt="" style="width:100%; height:100%; object-fit:contain; border-radius:inherit;">';
      var link = document.getElementById('brandFavicon');
      if (link) link.href = brand.logoUrl;
    };
    probe.src = brand.logoUrl;
  }).catch(function(){});
})();
