/* Landing sahifasi skripti — ALOHIDA faylda, chunki prod CSP (script-src 'self')
   HTML ichidagi inline skriptni bloklaydi (inline bo'lsa tugmalar umuman ishlamaydi). */
(function(){
  'use strict';

  var modalBackdrop = document.getElementById('modalBackdrop');
  var modalClose = document.getElementById('modalClose');
  var openTriggers = document.querySelectorAll('[data-open-modal]');
  var navToggle = document.getElementById('navToggle');
  var navLinks = document.getElementById('navLinks');
  var subjectSelect = document.getElementById('leadSubject');

  var currentLeadNote = '';

  // UZLUKSIZ karusel (marquee) treklari MODUL darajasida ro'yxatga olinadi: oyna o'lchami
  // o'zgarganda hammasi qayta o'lchanadi. Slayd kengligi FOIZDA berilgan (33.3% / 50% / 100%),
  // ya'ni surilish masofasi ekranga bog'liq — bir marta hisoblab qo'yib bo'lmaydi.
  var marqueeTracks = [];

  function openModal(selectedSubject, customNote){
    clearError();
    if (subjectSelect) {
      if (selectedSubject) {
        var matched = false;
        for (var i = 0; i < subjectSelect.options.length; i++) {
          var val = subjectSelect.options[i].value || subjectSelect.options[i].text;
          if (val && (val === selectedSubject || selectedSubject.indexOf(val) !== -1 || val.indexOf(selectedSubject) !== -1)) {
            subjectSelect.selectedIndex = i;
            matched = true;
            break;
          }
        }
        if (!matched && subjectSelect.options.length > 1) {
          subjectSelect.selectedIndex = 1;
        }
      } else if (subjectSelect.selectedIndex === 0 && subjectSelect.options.length > 1) {
        subjectSelect.selectedIndex = 1;
      }
    }
    currentLeadNote = customNote || 'Sayt tugmasidan yozilish';
    if (!modalBackdrop) return;
    modalBackdrop.classList.add('show');
    document.body.style.overflow = 'hidden';
  }

  function closeModal(){
    if (!modalBackdrop) return;
    modalBackdrop.classList.remove('show');
    document.body.style.overflow = '';
  }

  openTriggers.forEach(function(btn){
    btn.addEventListener('click', function(e){
      // Atribut AYNAN BOSILGAN PAYTDA tekshiriladi: renderSocials() App Store / Play Market
      // tugmalaridan `data-open-modal` ni olib tashlaydi, lekin removeAttribute listenerni
      // O'CHIRMAYDI — tekshiruvsiz bunday tugma ham modalni ochib, ham havolaga o'tib ketardi.
      if (!btn.hasAttribute('data-open-modal')) return;
      // Footer tugmalari <a href="#"> — busiz sahifa sahifa boshiga sakrab ketardi.
      if (btn.tagName === 'A') e.preventDefault();
      var subj = btn.getAttribute('data-subject');
      var note = btn.getAttribute('data-note') || 'Tugma: ' + (btn.textContent || '').trim();
      openModal(subj, note);
    });
  });

  // Barcha DOM murojaatlari himoyalangan: bitta element HTML'dan olib tashlansa ham butun
  // IIFE xato berib, landingdagi HAMMA skript (karusel, modallar, formalar) o'lib qolardi.
  if (modalClose) modalClose.addEventListener('click', closeModal);
  if (modalBackdrop) {
    modalBackdrop.addEventListener('click', function(e){
      if (e.target === modalBackdrop) closeModal();
    });
  }
  document.addEventListener('keydown', function(e){
    if (e.key === 'Escape' && modalBackdrop && modalBackdrop.classList.contains('show')) closeModal();
  });

  // Mobil menyu toggle
  if (navToggle && navLinks) {
    navToggle.addEventListener('click', function(){
      navLinks.classList.toggle('show');
    });

    // Nav-link bosilganda mobil menyuni yopish
    var links = navLinks.querySelectorAll('a');
    links.forEach(function(link){
      link.addEventListener('click', function(){
        navLinks.classList.remove('show');
      });
    });
  }

  // FAQ Akordeon
  var faqToggles = document.querySelectorAll('[data-faq-toggle]');
  faqToggles.forEach(function(btn){
    btn.addEventListener('click', function(){
      var item = btn.closest('.faq-item');
      if (item) {
        var isOpen = item.classList.contains('active');
        document.querySelectorAll('.faq-item').forEach(function(fi){
          fi.classList.remove('active');
        });
        if (!isOpen) {
          item.classList.add('active');
        }
      }
    });
  });

  // Brend ma'lumotlarini dinamik yuklash
  fetch('/api/public/brand').then(function(res){
    if (!res.ok) return null;
    return res.json();
  }).then(function(brand){
    if (!brand) return;
    if (brand.logoUrl) applyBrandLogo(brand.logoUrl);
    // `brandNameCopy` ro'yxatdan OLIB TASHLANDI — bunday ID landing.html da umuman yo'q edi
    // (o'lik havola: getElementById har safar null qaytarardi).
    if (brand.name) {
      ['brandName', 'brandNameFooter'].forEach(function(id){
        var el = document.getElementById(id);
        if (el) el.textContent = brand.name;
      });
    }
    if (brand.phone) {
      var fab = document.getElementById('fabPhone');
      if (fab) fab.setAttribute('href', 'tel:' + brand.phone.replace(/[^\d+]/g, ''));
    }
  }).catch(function(){});

  // Logo AVVAL yuklab ko'riladi, KEYIN qo'yiladi. Sabab: CMS'dagi manzil buzuq bo'lsa
  // nav va footerda "singan rasm" ikonkasi chiqib qolardi, inline `onerror=` esa prod
  // CSP (script-src 'self') tufayli mumkin emas. Rasm yuklanmasa belgi BO'SH qoladi va
  // CSS (.logo .mark:empty) uni umuman chizmaydi — faqat "Wunderkind" matni ko'rinadi.
  function applyBrandLogo(url) {
    var probe = new Image();
    probe.onload = function(){
      var safeUrl = escapeHtml(url); // manzil CMS'dan keladi — atribut ichiga qo'yishdan oldin tozalanadi
      ['brandMark', 'brandMarkFooter'].forEach(function(id){
        var mark = document.getElementById(id);
        // alt="" — yonida markaz nomi matn bilan yozilgan, skrinrider takrorlamasin
        if (mark) mark.innerHTML = '<img src="' + safeUrl + '" alt="" style="width:100%; height:100%; object-fit:contain; border-radius:inherit;">';
      });
      setFavicon(url);
    };
    probe.src = url;
  }

  // Tab ikonkasi — markaz logosi. Statik 🎓 emoji favicon HTML'dan olib tashlangan, ya'ni
  // logo sozlanmagan bo'lsa u QAYTMAYDI: tabda faqat "Wunderkind" sarlavhasi qoladi.
  function setFavicon(url) {
    var link = document.getElementById('brandFavicon') || document.querySelector('link[rel="icon"]');
    if (!link) {
      link = document.createElement('link');
      link.rel = 'icon';
      document.head.appendChild(link);
    }
    link.href = url;
  }

  var form = document.getElementById('leadForm');
  var formWrap = document.getElementById('formWrap');
  var msgEl = document.getElementById('leadFormMsg');
  var submitBtn = document.getElementById('leadSubmitBtn');
  var nameInput = document.getElementById('leadName');
  var phoneInput = document.getElementById('leadPhone');

  function showError(text){
    if (!msgEl) return;
    msgEl.textContent = text;
    msgEl.classList.add('error');
  }
  function clearError(){
    if (!msgEl) return;
    msgEl.textContent = '';
    msgEl.classList.remove('error');
  }
  // Tugma holati bitta joyda — har bir tarmoqda null-tekshiruv takrorlanmasin.
  function setLeadSubmitState(busy){
    if (!submitBtn) return;
    submitBtn.disabled = busy;
    submitBtn.textContent = busy ? 'Yuborilmoqda…' : 'Ariza qoldirish';
  }

  function digitsOnly(str){
    return (str || '').replace(/\D/g, '');
  }

  if (phoneInput) phoneInput.addEventListener('input', clearError);
  if (nameInput) nameInput.addEventListener('input', clearError);

  if (subjectSelect) subjectSelect.addEventListener('change', clearError);

  if (form) form.addEventListener('submit', function(event){
    event.preventDefault();
    clearError();

    var fullName = nameInput ? nameInput.value.trim() : '';
    var phone = phoneInput ? phoneInput.value.trim() : '';
    var subject = '';
    if (subjectSelect) {
      subject = subjectSelect.value;
      if (!subject && subjectSelect.selectedIndex >= 0 && subjectSelect.options[subjectSelect.selectedIndex]) {
        var opt = subjectSelect.options[subjectSelect.selectedIndex];
        if (!opt.disabled) {
          subject = opt.value || opt.text;
        }
      }
      if (!subject && subjectSelect.options.length > 1) {
        subjectSelect.selectedIndex = 1;
        var opt2 = subjectSelect.options[1];
        subject = opt2.value || opt2.text || 'General English';
      }
    }
    if (!subject) subject = 'General English';

    if (!fullName) {
      showError('Iltimos, ismingizni kiriting.');
      if (nameInput) nameInput.focus();
      return;
    }
    if (digitsOnly(phone).length < 9) {
      showError('Iltimos, to\'g\'ri telefon raqam kiriting (kamida 9 ta raqam).');
      if (phoneInput) phoneInput.focus();
      return;
    }

    setLeadSubmitState(true);

    fetch('/api/public/landing-lead', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ fullName: fullName, phone: phone, subject: subject, note: currentLeadNote })
    }).then(function(response){
      if (response.ok) {
        if (formWrap) formWrap.innerHTML =
          '<div class="form-success">' +
            '<div class="ic">✅</div>' +
            '<h4>Arizangiz qabul qilindi!</h4>' +
            '<p>Tez orada siz bilan bog\'lanamiz.</p>' +
          '</div>';
        return null;
      }
      if (response.status === 400 || response.status === 429) {
        return response.json().catch(function(){ return null; }).then(function(data){
          var message = (data && data.message) ? data.message : 'Xatolik yuz berdi, qayta urinib ko\'ring yoki qo\'ng\'iroq qiling.';
          showError(message);
          setLeadSubmitState(false);
        });
      }
      showError('Xatolik yuz berdi, qayta urinib ko\'ring yoki qo\'ng\'iroq qiling.');
      setLeadSubmitState(false);
      return null;
    }).catch(function(){
      showError('Xatolik yuz berdi, qayta urinib ko\'ring yoki qo\'ng\'iroq qiling.');
      setLeadSubmitState(false);
    });
  });

  function updateNavCta(){
    var isSmall = window.innerWidth <= 600;
    var navBtn = document.querySelector('.nav-actions .btn-primary');
    if (!navBtn) return;
    var full = navBtn.querySelector('.nav-cta-full');
    var short = navBtn.querySelector('.nav-cta-short');
    if (!full || !short) return;
    full.style.display = isSmall ? 'none' : 'inline';
    short.style.display = isSmall ? 'inline' : 'none';
  }
  updateNavCta();
  window.addEventListener('resize', updateNavCta);

  // Teacher Modal Controls
  var teacherModalBackdrop = document.getElementById('teacherModalBackdrop');
  var teacherModalClose = document.getElementById('teacherModalClose');
  var teacherModalPhoto = document.getElementById('teacherModalPhoto');
  var teacherModalBadge = document.getElementById('teacherModalBadge');
  var teacherModalName = document.getElementById('teacherModalName');
  var teacherModalSubject = document.getElementById('teacherModalSubject');
  var teacherModalBio = document.getElementById('teacherModalBio');
  var teacherModalCta = document.getElementById('teacherModalCta');

  var currentTeacherName = '';
  function openTeacherModal(t) {
    if (!t) return;
    currentTeacherName = t.fullName || '';
    // Surat bo'lmasa NEYTRAL ikonka qo'yiladi — bu hech kimning shaxsiy surati emas, ya'ni
    // "boshqa odamning rasmi zaxira sifatida ishlatilmasin" qoidasini buzmaydi. Modal tuzilishi
    // suratsiz chizilmagani uchun (rasm — kartochkaning asosiy qismi) bu yerda zaxira SAQLANDI.
    if (teacherModalPhoto) teacherModalPhoto.src = t.photoUrl || '/img/icons/icon-teacher.png';
    if (teacherModalBadge) {
      teacherModalBadge.textContent = t.badge || '';
      teacherModalBadge.style.display = t.badge ? 'inline-block' : 'none';
    }
    if (teacherModalName) teacherModalName.textContent = t.fullName || '';
    if (teacherModalSubject) teacherModalSubject.textContent = t.subject || '';
    if (teacherModalBio) teacherModalBio.textContent = t.fullBio || t.shortBio || '';
    if (teacherModalCta) teacherModalCta.setAttribute('data-subject', t.subject || 'General English');

    if (teacherModalBackdrop) {
      teacherModalBackdrop.classList.add('show');
      document.body.style.overflow = 'hidden';
    }
  }

  function closeTeacherModal() {
    if (teacherModalBackdrop) {
      teacherModalBackdrop.classList.remove('show');
      document.body.style.overflow = '';
    }
  }

  if (teacherModalClose) teacherModalClose.addEventListener('click', closeTeacherModal);
  if (teacherModalBackdrop) {
    teacherModalBackdrop.addEventListener('click', function(e) {
      if (e.target === teacherModalBackdrop) closeTeacherModal();
    });
  }

  if (teacherModalCta) {
    teacherModalCta.addEventListener('click', function() {
      var subj = teacherModalCta.getAttribute('data-subject');
      closeTeacherModal();
      openModal(subj, "Ustoz: " + currentTeacherName + " darsiga yozilmoqchi");
    });
  }

  // Result / Certificate Detail Modal Logic
  var resultModalBackdrop = document.getElementById('resultModalBackdrop');
  var resultModalClose = document.getElementById('resultModalClose');
  var resultModalImg = document.getElementById('resultModalImg');
  var resultModalStudentName = document.getElementById('resultModalStudentName');
  var resultModalCategory = document.getElementById('resultModalCategory');
  var resultModalOverall = document.getElementById('resultModalOverall');
  var resultModalListening = document.getElementById('resultModalListening');
  var resultModalReading = document.getElementById('resultModalReading');
  var resultModalWriting = document.getElementById('resultModalWriting');
  var resultModalSpeaking = document.getElementById('resultModalSpeaking');
  var resultModalCta = document.getElementById('resultModalCta');

  var currentCertInfo = '';

  // Ko'rsatiladigan qiymat YO'Q bo'lsa "—" chiziladi. ⚠️ Ilgari bu yerda uydirma ballar
  // ('8.5', '9.0', '7.5', '8.0') zaxira sifatida turardi — ya'ni CMS'da ball kiritilmagan
  // sertifikat ochilsa modal SOXTA natija ko'rsatardi. Ommaviy saytda bu — to'qib chiqarilgan
  // ma'lumot, shuning uchun har qanday zaxira bal olib tashlandi.
  function dashIfEmpty(v) {
    return (v === null || v === undefined || v === '') ? '—' : String(v);
  }

  function openResultModal(certData) {
    currentCertInfo = (certData.studentName || certData.title || '') + (certData.overall ? ' (' + certData.overall + ')' : '');
    // Rasm bo'lmasa BOSHQA o'quvchining sertifikat surati qo'yilmaydi — `<img>` butunlay
    // yashiriladi (bo'sh `src` bilan brauzer "singan rasm" belgisini chizardi).
    if (resultModalImg) {
      if (certData.imageUrl) {
        resultModalImg.src = certData.imageUrl;
        resultModalImg.style.display = '';
      } else {
        resultModalImg.removeAttribute('src');
        resultModalImg.style.display = 'none';
      }
    }
    if (resultModalStudentName) resultModalStudentName.textContent = certData.studentName || certData.title || 'O\'QUVCHI SERTIFIKATI';
    if (resultModalCategory) resultModalCategory.textContent = certData.category || 'SERTIFIKAT';
    if (resultModalOverall) resultModalOverall.textContent = dashIfEmpty(certData.overall);
    if (resultModalListening) resultModalListening.textContent = dashIfEmpty(certData.listening);
    if (resultModalReading) resultModalReading.textContent = dashIfEmpty(certData.reading);
    if (resultModalWriting) resultModalWriting.textContent = dashIfEmpty(certData.writing);
    if (resultModalSpeaking) resultModalSpeaking.textContent = dashIfEmpty(certData.speaking);

    if (resultModalBackdrop) {
      resultModalBackdrop.classList.add('show');
      document.body.style.overflow = 'hidden';
    }
  }

  function closeResultModal() {
    if (resultModalBackdrop) {
      resultModalBackdrop.classList.remove('show');
      document.body.style.overflow = '';
    }
  }

  if (resultModalClose) resultModalClose.addEventListener('click', closeResultModal);
  if (resultModalBackdrop) {
    resultModalBackdrop.addEventListener('click', function(e) {
      if (e.target === resultModalBackdrop) closeResultModal();
    });
  }
  if (resultModalCta) {
    resultModalCta.addEventListener('click', function() {
      closeResultModal();
      openModal('IELTS', "Sertifikat: " + currentCertInfo + " natijasi orqali yozildi");
    });
  }

  // FAQ Contact Banner Lead Form Submission
  var faqLeadForm = document.getElementById('faqLeadForm');
  var faqLeadName = document.getElementById('faqLeadName');
  var faqLeadPhone = document.getElementById('faqLeadPhone');
  var faqLeadSubject = document.getElementById('faqLeadSubject');
  var faqLeadSubmitBtn = document.getElementById('faqLeadSubmitBtn');
  var faqLeadFormMsg = document.getElementById('faqLeadFormMsg');

  function setFaqMsg(text, isError, isSuccess) {
    if (!faqLeadFormMsg) return;
    faqLeadFormMsg.textContent = text;
    faqLeadFormMsg.style.display = text ? 'block' : 'none';
    faqLeadFormMsg.style.marginTop = '16px';
    faqLeadFormMsg.style.padding = '12px 18px';
    faqLeadFormMsg.style.borderRadius = '12px';
    faqLeadFormMsg.style.fontSize = '14px';
    faqLeadFormMsg.style.fontWeight = '600';
    faqLeadFormMsg.style.textAlign = 'center';
    faqLeadFormMsg.style.gridColumn = '1 / -1';

    if (isError) {
      faqLeadFormMsg.style.color = '#ff8b96';
      faqLeadFormMsg.style.background = 'rgba(220, 53, 69, 0.15)';
      faqLeadFormMsg.style.border = '1px solid rgba(220, 53, 69, 0.4)';
    } else if (isSuccess) {
      faqLeadFormMsg.style.color = '#4ade80';
      faqLeadFormMsg.style.background = 'rgba(74, 222, 128, 0.15)';
      faqLeadFormMsg.style.border = '1px solid rgba(74, 222, 128, 0.4)';
    } else {
      faqLeadFormMsg.style.color = '#38bdf8';
      faqLeadFormMsg.style.background = 'rgba(56, 189, 248, 0.15)';
      faqLeadFormMsg.style.border = '1px solid rgba(56, 189, 248, 0.4)';
    }
  }

  if (faqLeadName) faqLeadName.addEventListener('input', function() { setFaqMsg('', false, false); });
  if (faqLeadPhone) faqLeadPhone.addEventListener('input', function() { setFaqMsg('', false, false); });
  if (faqLeadSubject) faqLeadSubject.addEventListener('change', function() { setFaqMsg('', false, false); });

  if (faqLeadForm) {
    faqLeadForm.addEventListener('submit', function(e) {
      e.preventDefault();
      var name = faqLeadName ? faqLeadName.value.trim() : '';
      var phone = faqLeadPhone ? faqLeadPhone.value.trim() : '';
      var subject = faqLeadSubject ? faqLeadSubject.value : '';

      if (!name) {
        setFaqMsg('Iltimos, ismingizni kiriting.', true, false);
        if (faqLeadName) faqLeadName.focus();
        return;
      }
      if (!phone || digitsOnly(phone).length < 9) {
        setFaqMsg('Iltimos, to\'g\'ri telefon raqamingizni kiriting.', true, false);
        if (faqLeadPhone) faqLeadPhone.focus();
        return;
      }
      if (!subject) {
        setFaqMsg('Iltimos, qiziqqan faningizni tanlang.', true, false);
        if (faqLeadSubject) faqLeadSubject.focus();
        return;
      }

      setFaqMsg('Yuborilmoqda...', false, false);
      if (faqLeadSubmitBtn) faqLeadSubmitBtn.disabled = true;

      fetch('/api/public/landing-lead', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          fullName: name,
          phone: phone,
          subject: subject,
          note: 'FAQ ostidagi "Savollaringiz qoldimi? Biz bilan bog\'laning!" formasidan'
        })
      }).then(function(res) {
        if (res.ok) {
          setFaqMsg('✅ Arizangiz muvaffaqiyatli yuborildi! Tez orada bog\'lanamiz.', false, true);
          faqLeadForm.reset();
        } else {
          setFaqMsg('❌ Xatolik yuz berdi. Qayta urinib ko\'ring.', true, false);
        }
      }).catch(function() {
        setFaqMsg('❌ Xatolik yuz berdi.', true, false);
      }).finally(function() {
        if (faqLeadSubmitBtn) faqLeadSubmitBtn.disabled = false;
      });
    });
  }

  // URL Query Parameters Parsing & Auto-Lead Intake (?name=...&phone=...&subject=...#contact)
  (function handleUrlQueryParams() {
    try {
      var searchStr = window.location.search;
      if (!searchStr) return;
      var params = new URLSearchParams(searchStr);

      var urlName = (params.get('name') || params.get('fullName') || params.get('ism') || '').trim();
      var urlPhone = (params.get('phone') || params.get('tel') || params.get('raqam') || '').trim();
      var urlSubject = (params.get('subject') || params.get('course') || params.get('fan') || '').trim();

      if (!urlName && !urlPhone && !urlSubject) return;

      // 1) Lid MODALI shakllarini pre-fill (avto to'ldirish).
      //    Ilgari bu yerda aloqa bo'limidagi "Bepul maslahat oling" formasi ham to'ldirilardi —
      //    o'sha forma sahifadan butunlay olib tashlangani uchun havolalar ham olib tashlandi
      //    (aks holda getElementById har safar null qaytaradigan o'lik kod qolardi).
      if (urlName) {
        if (nameInput) nameInput.value = urlName;
      }
      if (urlPhone) {
        if (phoneInput) phoneInput.value = urlPhone;
      }
      if (urlSubject) {
        [subjectSelect].forEach(function(sel) {
          if (!sel) return;
          for (var i = 0; i < sel.options.length; i++) {
            var val = sel.options[i].value || sel.options[i].text;
            if (val && (val === urlSubject || urlSubject.indexOf(val) !== -1 || val.indexOf(urlSubject) !== -1)) {
              sel.selectedIndex = i;
              break;
            }
          }
        });
      }

      // 2) Agar URL'da kamida ism va telefon raqami bo'lsa — CRM'ga avtomatik lid yuborish.
      //    ⚠️ FAQAT BIR MARTA. Ilgari so'rov sahifa har ochilganda ketardi: foydalanuvchi F5
      //    bossa yoki "orqaga" qaytsa, serverda lidning RepeatCount'i oshib, operator soxta
      //    "takroriy murojaat" ko'rardi. Kalit telefon raqamidan yasaladi va sessionStorage'da
      //    saqlanadi (tab yopilguncha yetarli — bir seansda takror yuborishning oldi olinadi).
      if (urlName && digitsOnly(urlPhone).length >= 7) {
        var autoSubj = urlSubject || 'General English';
        var autoNote = 'Sayt URL havolasi orqali kelgan ariza (URL: ' + window.location.href + ')';
        var autoKey = 'landingAutoLead:' + digitsOnly(urlPhone);

        var autoLeadSent = function() {
          try { return sessionStorage.getItem(autoKey) === '1'; } catch (err) { return false; }
        };
        var markAutoLead = function(sent) {
          try {
            if (sent) sessionStorage.setItem(autoKey, '1');
            else sessionStorage.removeItem(autoKey);
          } catch (err) {}
        };
        var showAutoLeadSuccess = function() {
          if (formWrap) {
            formWrap.innerHTML =
              '<div class="form-success">' +
                '<div class="ic">✅</div>' +
                '<h4>Arizangiz CRM ga qabul qilindi!</h4>' +
                '<p>Tez orada siz bilan bog\'lanamiz.</p>' +
              '</div>';
          }
          if (modalBackdrop) modalBackdrop.classList.add('show');
        };

        if (autoLeadSent()) {
          // Ariza allaqachon yuborilgan — server bezovta qilinmaydi, lekin foydalanuvchi
          // tasdiqni baribir ko'rsin (aks holda "yuborildimi?" degan savol qolardi).
          showAutoLeadSuccess();
        } else {
          // Belgi so'rovdan OLDIN qo'yiladi — juda tez qayta yuklashda ikkinchi so'rov ketmasin.
          markAutoLead(true);
          fetch('/api/public/landing-lead', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
              fullName: urlName,
              phone: urlPhone,
              subject: autoSubj,
              note: autoNote
            })
          }).then(function(res) {
            if (res.ok) {
              showAutoLeadSuccess();
            } else {
              // Server qabul qilmadi — belgini olib tashlaymiz, keyingi urinishga yo'l ochiq qolsin.
              markAutoLead(false);
            }
          }).catch(function(err) {
            markAutoLead(false);
            console.error('[Auto-Lead] Xatolik:', err);
          });
        }
      }
    } catch (e) {
      console.error('[Auto-Lead] Query param error:', e);
    }
  })();

  function normalizeMapUrl(url) {
    if (!url) return '';
    var u = url.trim();

    // Handle Yandex Maps URLs
    if (u.indexOf('yandex.uz') !== -1 || u.indexOf('yandex.ru') !== -1) {
      var oidMatch = u.match(/oid%3D(\d+)/i) || u.match(/oid=(\d+)/i) || u.match(/org\/(\d+)/i);
      if (oidMatch && oidMatch[1]) {
        return 'https://yandex.uz/map-widget/v1/org/' + oidMatch[1];
      }
      var llMatch = u.match(/ll=([\d\.\%2C\,]+)/i);
      if (llMatch && llMatch[1]) {
        var coords = decodeURIComponent(llMatch[1]);
        return 'https://yandex.uz/map-widget/v1/?ll=' + coords + '&z=16';
      }
      if (u.indexOf('/map-widget/') === -1) {
        return u.replace(/\/maps\//i, '/map-widget/v1/');
      }
      return u;
    }

    // Handle Google Maps URLs
    if (u.indexOf('google.com') !== -1) {
      u = u.replace(/www\.google\.com/gi, 'maps.google.com');
      if (u.indexOf('output=embed') === -1 && u.indexOf('/embed') === -1) {
        u += (u.indexOf('?') === -1 ? '?' : '&') + 'output=embed';
      }
    }
    return u;
  }

  function renderMap(mapSource) {
    var wrap = document.getElementById('landingMapWrap');
    if (!wrap) return;
    var raw = (mapSource || '').trim();
    if (!raw) return;

    // Xarita USTIDAGI "Yandex/Google Maps-da ochish" overlay tugmalari ATAYIN YO'Q:
    // aynan o'sha ikki havola aloqa kartochkasining pastki qatorida STATIK holda turadi.
    // Statik variant afzal — u JS'siz ham ishlaydi va Google havolasi to'liq Place
    // sahifasiga olib boradi (overlay'da faqat koordinata edi). Ikki ustunli tartibda
    // xarita tor bo'lgani uchun overlay uning yarmini bekitib ham qo'yardi.

    // Xarita HAQIQATAN chizilganda chaqiriladi: konteynerni ochadi VA aloqa gridiga
    // `has-map` sinfini qo'yadi (CSS shunda ikki ustunga o'tadi).
    // ⚠️ Sinf faqat SHU YERDA qo'yiladi — xarita chizilmagan yo'lda (mapUrl bo'sh yoki
    // URL topilmadi) grid bitta ustunda qoladi, aks holda kartochkaning o'ng yarmi bo'sh qolardi.
    function showMapWrap() {
      wrap.style.display = 'block';
      if (!wrap.closest) return;
      // Grid ikki ustunga o'tadi...
      var split = wrap.closest('.contact-split');
      if (split) split.classList.add('has-map');
      // ...va kartochkaning o'zi kengayadi (850px ichida ikki ustun siqilib qolardi).
      var box = wrap.closest('.contact-card-box');
      if (box) box.classList.add('has-map');
    }

    var iframeMatch = raw.match(/<iframe[^>]*src=["']([^"']+)["']/i);
    var targetUrl = '';

    if (iframeMatch && iframeMatch[1]) {
      targetUrl = iframeMatch[1].replace(/&amp;/gi, '&');
    } else if (raw.indexOf('http://') === 0 || raw.indexOf('https://') === 0) {
      targetUrl = raw.split(/\s+/)[0].replace(/&amp;/gi, '&');
    }

    if (targetUrl) {
      targetUrl = normalizeMapUrl(targetUrl);
      showMapWrap();
      wrap.innerHTML = '<iframe id="landingMapIframe" src="' + targetUrl + '" width="100%" height="100%" style="border:0; min-height:380px; width:100%; display:block;" allowfullscreen="" loading="lazy"></iframe>';
    } else if (raw.indexOf('<') !== -1) {
      var cleanHtml = raw;
      if (raw.indexOf('<body') !== -1) {
        var bM = raw.match(/<body[^>]*>([\s\S]*?)<\/body>/i);
        if (bM) cleanHtml = bM[1];
      }

      cleanHtml = cleanHtml.replace(/<!DOCTYPE[^>]*>/gi, '')
                           .replace(/<\/?html[^>]*>/gi, '')
                           .replace(/<\/?head[^>]*>/gi, '');

      showMapWrap();
      wrap.innerHTML = cleanHtml;

      var scriptRegex = /<script([^>]*)>([\s\S]*?)<\/script>/gi;
      var match;
      while ((match = scriptRegex.exec(raw)) !== null) {
        var attrs = match[1];
        var code = match[2];
        var srcM = attrs.match(/src=["']([^"']+)["']/i);
        var typeM = attrs.match(/type=["']([^"']+)["']/i);

        var s = document.createElement('script');
        if (typeM && typeM[1]) s.type = typeM[1];
        if (srcM && srcM[1]) {
          s.src = srcM[1];
          s.async = true;
        } else if (code && code.trim()) {
          s.text = code;
        }
        document.head.appendChild(s);
      }
    }
  }

  // Dynamic Landing Data Fetch & Carousel Init
  fetch('/api/public/landing-data').then(function(res) {
    if (!res.ok) return null;
    return res.json();
  }).then(function(data) {
    if (!data) return;

    if (data.mapUrl) {
      renderMap(data.mapUrl);
    }

    // O'QITUVCHILAR va SERTIFIKATLAR — fikrlar bo'limi kabi SHARTSIZ chaqiriladi.
    // ⚠️ Ilgari bu yerda `length > 0` sharti turardi: ro'yxat bo'sh bo'lsa render UMUMAN
    // chaqirilmas va HTML'dagi statik SOXTA kartochkalar ekranda qolib ketardi. Endi statik
    // markup ham yo'q, "bo'sh bo'lsa bo'limni yashir" qarorini esa funksiyalarning O'ZI
    // qabul qiladi (bitta joyda, ikki xil bo'lib ketmasin).
    renderTeachers(data.teachers);
    renderCertificates(data.certificates);

    // FAQ — ilgari renderFaqs() yozilgan, lekin HECH QAYERDA chaqirilmagan edi: admin CMS'da
    // savol qo'shsa ham saytda hech narsa o'zgarmasdi. Ro'yxat bo'sh bo'lsa HTML'dagi statik
    // savollar joyida qoladi (bo'sh ekran ko'rsatilmaydi).
    if (data.faqs && data.faqs.length > 0) {
      renderFaqs(data.faqs);
    }

    // FIKRLAR — SHARTSIZ chaqiriladi (yuqoridagilardan farqi shu). Sabab: bo'limning HTML'da
    // statik zaxirasi yo'q va u boshidan yopiq, ya'ni "ro'yxat bo'sh bo'lsa yashir" qarorini
    // ham aynan shu funksiya qabul qiladi. Shart qo'yilsa bo'sh ro'yxatda funksiya umuman
    // chaqirilmas, keyinchalik kimdir bo'limni ochib qo'ysa esa bo'sh quti osilib qolardi.
    renderTestimonials(data.testimonials);

    // Render Socials & Contact Info
    if (data.socials) {
      renderSocials(data.socials);
    }
  }).catch(function() {});

  function renderSocials(socials) {
    if (!socials) return;

    // Contact Phone
    var phoneEl = document.getElementById('contactPhoneValue');
    if (phoneEl && socials.contactPhone) {
      phoneEl.textContent = socials.contactPhone;
      phoneEl.href = 'tel:' + socials.contactPhone.replace(/[^0-9+]/g, '');
    }

    // Working Hours
    var hoursEl = document.getElementById('workingHoursValue');
    if (hoursEl && socials.workingHours) {
      hoursEl.innerHTML = escapeHtml(socials.workingHours).replace(/\n/g, '<br>');
    }

    // Center Address
    var addrEl = document.getElementById('centerAddressValue');
    if (addrEl && socials.centerAddress) {
      addrEl.textContent = socials.centerAddress;
    }

    // Social links in footer
    var tgLink = document.getElementById('socialTelegramLink');
    if (tgLink && socials.telegramUrl) tgLink.href = socials.telegramUrl;

    var instaLink = document.getElementById('socialInstagramLink');
    if (instaLink && socials.instagramUrl) instaLink.href = socials.instagramUrl;

    var ytLink = document.getElementById('socialYoutubeLink');
    if (ytLink && socials.youtubeUrl) ytLink.href = socials.youtubeUrl;

    var fbLink = document.getElementById('socialFacebookLink');
    if (fbLink && socials.facebookUrl) fbLink.href = socials.facebookUrl;

    var emailLink = document.getElementById('socialEmailLink');
    if (emailLink && socials.centerEmail) {
      var cleanEmail = socials.centerEmail.trim().replace(/^mailto:/i, '');
      emailLink.href = 'mailto:' + cleanEmail;
    }

    var appStoreBtn = document.getElementById('appStoreFooterBtn');
    if (appStoreBtn && socials.appStoreUrl && socials.appStoreUrl.trim()) {
      appStoreBtn.href = socials.appStoreUrl.trim();
      appStoreBtn.target = '_blank';
      appStoreBtn.removeAttribute('data-open-modal');
    }

    var playMarketBtn = document.getElementById('playMarketFooterBtn');
    if (playMarketBtn && socials.playMarketUrl && socials.playMarketUrl.trim()) {
      playMarketBtn.href = socials.playMarketUrl.trim();
      playMarketBtn.target = '_blank';
      playMarketBtn.removeAttribute('data-open-modal');
    }
  }

  // ------------------------------------------------- BO'LIM KO'RINISHI (yagona joy)
  //
  // Bo'lim ma'lumot BO'LMASA butunlay yashiriladi: bo'sh sarlavha va bo'sh karusel saytda
  // "ishlamayapti" degan taassurot qoldiradi. Yashirilgan bo'limga olib boradigan nav
  // havolasi ham berkitiladi — aks holda menyudagi tugma hech qayerga olib bormasdi
  // (brauzer sahifani joyida qoldiradi va foydalanuvchi "havola singan" deb o'ylaydi).
  function setSectionVisible(sectionId, visible) {
    var section = document.getElementById(sectionId);
    if (section) section.style.display = visible ? '' : 'none';

    var link = document.querySelector('#navLinks a[href="#' + sectionId + '"]');
    if (link) {
      // Havolaning O'ZI emas, uni o'rab turgan <li> yashiriladi — aks holda ro'yxatda
      // bo'sh katak (gap) qolib ketardi.
      var item = link.parentNode && link.parentNode.tagName === 'LI' ? link.parentNode : link;
      item.style.display = visible ? '' : 'none';
    }
  }

  // Ism va familiyaning bosh harflari — surat bo'lmaganda ishlatiladigan zaxira
  // (fikrlar bo'limidagi `initialsOf` bilan bir xil qoida; funksiya quyiroqda e'lon qilingan,
  // `function` deklaratsiyasi hoist bo'lgani uchun bu yerdan chaqirish xavfsiz).

  function renderTeachers(teachersList) {
    var track = document.getElementById('teacherTrack');
    if (!track) return;

    var items = teachersList || [];
    track.innerHTML = '';

    // BO'SH RO'YXAT — bo'lim BUTUNLAY yashiriladi (soxta kartochkalar endi HTML'da ham yo'q).
    if (items.length === 0) {
      setSectionVisible('teachers', false);
      return;
    }

    items.forEach(function(t, idx) {
      var slide = document.createElement('div');
      slide.className = 'teacher-slide';
      slide.style.cursor = 'pointer';
      // Uzluksiz lenta slaydlarni nusxalaydi — modal uchun kerakli yozuv AYNAN shu indeks
      // orqali topiladi (quyidagi delegatsiyaga qarang).
      slide.setAttribute('data-index', String(idx));

      // ⚠️ KLASS NOMLARI landing.html dagi CSS bilan AYNAN mos: `.teacher-photo-bg`,
      // `.teacher-top-badge`, `.teacher-card-overlay`, `.teacher-card-name`, ... Ilgari bu yerda
      // statik soxta markupdan ko'chirilgan `.teacher-avatar-wrap` / `.teacher-name` ishlatilar,
      // ular uchun esa CSS UMUMAN YO'Q edi — kartochka uslubsiz chiqardi.
      var badgeHtml = t.badge ? '<span class="teacher-top-badge">' + escapeHtml(t.badge) + '</span>' : '';

      // ⚠️ Surat YO'Q bo'lsa BOSHQA o'qituvchining rasmi (ilgari '/img/teachers/teacher-1.jpg')
      // qo'yilmaydi — bu ochiq saytda begona odamni bizning ustozimiz sifatida ko'rsatish edi.
      // O'rniga fikrlar bo'limidagi naqsh: ism bosh harflari.
      var photoHtml = t.photoUrl
        ? '<img class="teacher-photo-bg" src="' + escapeHtml(t.photoUrl) + '" alt="' + escapeHtml(t.fullName) + '" loading="lazy">'
        : '<div class="teacher-photo-bg teacher-photo-initials" aria-hidden="true">' + escapeHtml(initialsOf(t.fullName)) + '</div>';

      // Qisqa ma'lumot bo'lmasa pastki qator umuman chizilmaydi — bo'sh "Ma'lumot" yorlig'i
      // ostida quruq joy qolmasin.
      var bioText = t.shortBio || t.fullBio || '';
      var metaHtml = bioText
        ? '<div class="teacher-card-meta">' +
            '<div class="teacher-meta-item">' +
              '<span class="teacher-meta-label">Ma\'lumot</span>' +
              '<span class="teacher-meta-val">Batafsil ko\'rish →</span>' +
            '</div>' +
          '</div>'
        : '';

      slide.innerHTML =
        '<div class="teacher-card">' +
          photoHtml +
          badgeHtml +
          '<div class="teacher-card-overlay">' +
            '<div class="teacher-card-name">' + escapeHtml(t.fullName) + '</div>' +
            '<div class="teacher-card-subject">' + escapeHtml(t.subject || '') + '</div>' +
            metaHtml +
          '</div>' +
        '</div>';

      track.appendChild(slide);
    });

    // ⚠️ Bosilish TREKNING O'ZIDA ushlanadi (delegatsiya): uzluksiz lenta slaydlarni
    // `cloneNode` bilan ko'paytiradi, `addEventListener` bilan qo'yilgan ishlov beruvchi esa
    // nusxaga KO'CHMAYDI — nusxa bosilganda modal umuman ochilmasdi.
    track.onclick = function(e) {
      var slide = e.target && e.target.closest ? e.target.closest('.teacher-slide') : null;
      if (!slide) return;
      var t = items[Number(slide.getAttribute('data-index'))];
      if (t) openTeacherModal(t);
    };

    setSectionVisible('teachers', true);
    initMarquee(track);
  }

  // ------------------------------------------------- SERTIFIKATLAR (landing karuseli)
  //
  // ⚠️ KATEGORIYA FILTRI (Barchasi / Xalqaro / Milliy) landingda YO'Q — u faqat
  // `/sertifikatlar` sahifasida. Bu yerda hamma sertifikat karuselda birin-ketin
  // aylanaveradi, ya'ni ziyoratchi tugma bosmasdan hammasini ko'radi. Shu sabab ilgarigi
  // filtr holati + qayta chizish juftligi o'rniga bitta oddiy
  // `renderCertificates(list)` qoldi (server allaqachon faqat `isActive` larni va
  // `order` bo'yicha saralab qaytaradi — bu yerda qayta filtrlash SHART EMAS).
  function renderCertificates(certList) {
    var track = document.getElementById('resultsTrack');
    if (!track) return;

    var items = certList || [];
    track.innerHTML = '';

    // BO'SH RO'YXAT — butun "Bizning natijalarimiz" bo'limi yashiriladi.
    if (items.length === 0) {
      setSectionVisible('results', false);
      return;
    }

    items.forEach(function(c, idx) {
      var slide = document.createElement('div');
      slide.className = 'result-slide';
      slide.style.cursor = 'pointer';
      // Uzluksiz lenta slaydlarni nusxalaydi — modal uchun yozuv shu indeks orqali topiladi.
      slide.setAttribute('data-index', String(idx));

      var scoreSectionHtml = '';
      if (c.listening || c.reading || c.writing || c.speaking) {
        scoreSectionHtml =
          '<div class="result-breakdown">' +
            (c.listening ? '<div class="result-breakdown-row"><span>Listening</span> <span>' + escapeHtml(c.listening) + '</span></div>' : '') +
            (c.reading ? '<div class="result-breakdown-row"><span>Reading</span> <span>' + escapeHtml(c.reading) + '</span></div>' : '') +
            (c.writing ? '<div class="result-breakdown-row"><span>Writing</span> <span>' + escapeHtml(c.writing) + '</span></div>' : '') +
            (c.speaking ? '<div class="result-breakdown-row"><span>Speaking</span> <span>' + escapeHtml(c.speaking) + '</span></div>' : '') +
          '</div>';
      } else if (c.resultNote) {
        scoreSectionHtml = '<div style="font-size:13px;color:#f5a623;font-weight:700;margin-top:8px;">' + escapeHtml(c.resultNote) + '</div>';
      }

      // ⚠️ Sertifikat surati YO'Q bo'lsa BOSHQA o'quvchining sertifikati (ilgari
      // '/img/certificates/cert-1.jpg') ko'rsatilmaydi — rasm bloki umuman chizilmaydi.
      var imgHtml = c.imageUrl
        ? '<div class="result-cert-img-wrap">' +
            '<img src="' + escapeHtml(c.imageUrl) + '" alt="' + escapeHtml(c.title || c.studentName) + '" loading="lazy">' +
          '</div>'
        : '';

      // Ball/tur YO'Q bo'lsa uydirma '8.5' / 'OVERALL' yozilmaydi — quti umuman chizilmaydi.
      var overallHtml = (c.overallScore || c.certType)
        ? '<div class="result-overall-box">' +
            '<span class="result-overall-label">' + escapeHtml(c.certType || '') + '</span>' +
            '<span class="result-overall-score">' + escapeHtml(c.overallScore || '') + '</span>' +
          '</div>'
        : '';

      slide.innerHTML =
        '<div class="result-card">' +
          imgHtml +
          '<div class="result-student-name">' + escapeHtml(c.studentName || c.title) + '</div>' +
          '<div class="result-card-bottom">' +
            overallHtml +
            scoreSectionHtml +
          '</div>' +
        '</div>';

      track.appendChild(slide);
    });

    // Bosilish delegatsiya orqali — sabab o'qituvchilar bo'limidagi bilan bir xil (nusxalar).
    track.onclick = function(e) {
      var slide = e.target && e.target.closest ? e.target.closest('.result-slide') : null;
      if (!slide) return;
      var c = items[Number(slide.getAttribute('data-index'))];
      if (!c) return;
      openResultModal({
        studentName: c.studentName || c.title,
        overall: c.overallScore || '',
        category: c.category || '',
        listening: c.listening || '',
        reading: c.reading || '',
        writing: c.writing || '',
        speaking: c.speaking || '',
        imageUrl: c.imageUrl || ''
      });
    };

    setSectionVisible('results', true);
    initMarquee(track);
  }

  function renderFaqs(faqList) {
    var container = document.querySelector('.faq-list');
    if (!container) return;
    container.innerHTML = '';

    faqList.forEach(function(f) {
      var item = document.createElement('div');
      item.className = 'faq-item';
      item.innerHTML =
        '<button type="button" class="faq-question" data-faq-toggle>' +
          '<span>' + escapeHtml(f.question) + '</span>' +
          '<span class="faq-icon">▾</span>' +
        '</button>' +
        '<div class="faq-answer">' + escapeHtml(f.answer) + '</div>';

      var toggleBtn = item.querySelector('[data-faq-toggle]');
      toggleBtn.addEventListener('click', function() {
        var isOpen = item.classList.contains('active');
        document.querySelectorAll('.faq-item').forEach(function(fi) { fi.classList.remove('active'); });
        if (!isOpen) item.classList.add('active');
      });

      container.appendChild(item);
    });
  }

  // ---------------------------------------------------------------- FIKRLAR (Testimonials)
  //
  // Maydonlar `LandingTestimonial` entity'sidan (camelCase): authorName, authorRole, avatarUrl,
  // rating (butun son), comment, order, isActive. Server allaqachon FAQAT `isActive` yozuvlarni
  // va `order` bo'yicha tartiblab qaytaradi — bu yerda qayta filtrlash/saralash SHART EMAS.

  // `rating` — RAQAM, shuning uchun u to'g'ridan-to'g'ri innerHTML'ga qo'yilmaydi: avval butun
  // songa keltiriladi va 0..5 oralig'iga CHEKLANADI (CMS'ga xato bilan 50 kiritilsa kartochka
  // yulduzlar bilan to'lib ketmasin). 0 yoki yo'q bo'lsa — yulduz umuman chizilmaydi.
  function starsHtml(rating) {
    var n = Math.round(Number(rating));
    if (!isFinite(n) || n <= 0) return '';
    if (n > 5) n = 5;
    var out = '';
    for (var i = 0; i < 5; i++) out += (i < n ? '★' : '☆');
    // `n` — yuqorida tozalangan butun son (1..5), ya'ni atributga xavfsiz qo'yiladi.
    return '<div class="testimonial-stars" role="img" aria-label="Baho: ' + n + ' / 5">' + out + '</div>';
  }

  // Ism va familiyaning bosh harflari — avatar bo'lmaganda ishlatiladigan zaxira.
  function initialsOf(name) {
    var parts = String(name || '').trim().split(/\s+/);
    var out = '';
    for (var i = 0; i < parts.length && out.length < 2; i++) {
      if (parts[i]) out += parts[i].charAt(0).toUpperCase();
    }
    return out || '?';
  }

  // `avatarUrl` bo'sh bo'lsa SINUQ `<img>` ko'rsatilmaydi — loyihadagi odatdagi zaxira
  // (surat o'rniga bosh harflar) qo'llanadi. Avatar bor bo'lsa manzil `/uploads/<guid>.png`:
  // u mehmonga ochilishi uchun `UploadsGuard` ochiq ro'yxatiga qo'shilgan.
  function testimonialAvatarHtml(url, name) {
    if (url) {
      return '<img class="testimonial-avatar" src="' + escapeHtml(url) +
             '" alt="' + escapeHtml(name) + '" loading="lazy">';
    }
    return '<div class="testimonial-initials" aria-hidden="true">' +
           escapeHtml(initialsOf(name)) + '</div>';
  }

  function renderTestimonials(list) {
    var section = document.getElementById('testimonials');
    var grid = document.getElementById('testimonialsGrid');
    if (!section || !grid) return;

    var items = list || [];
    grid.innerHTML = '';

    // BO'SH RO'YXAT — bo'lim BUTUNLAY yashiriladi. O'qituvchi/sertifikat/FAQ dan farqli ravishda
    // server fikrlar uchun sukut (zaxira) ma'lumot qaytarmaydi, ya'ni aks holda sahifada bo'sh
    // sarlavha va bo'sh quti osilib qolardi.
    if (items.length === 0) {
      section.style.display = 'none';
      return;
    }

    items.forEach(function(t) {
      var card = document.createElement('article');
      card.className = 'testimonial-card';

      // Lavozim ("Ota-ona", "O'quvchi") bo'sh bo'lsa qator umuman chizilmaydi — bo'sh
      // element ostida ortiqcha bo'shliq qolmasin.
      var roleHtml = t.authorRole
        ? '<div class="testimonial-role">' + escapeHtml(t.authorRole) + '</div>'
        : '';

      card.innerHTML =
        starsHtml(t.rating) +
        '<p class="testimonial-text">' + escapeHtml(t.comment) + '</p>' +
        '<div class="testimonial-author">' +
          testimonialAvatarHtml(t.avatarUrl, t.authorName) +
          '<div>' +
            '<div class="testimonial-name">' + escapeHtml(t.authorName) + '</div>' +
            roleHtml +
          '</div>' +
        '</div>';

      grid.appendChild(card);
    });

    // Bo'lim FAQAT haqiqiy ma'lumot bo'lganda ochiladi (HTML'da u `display:none` bilan keladi).
    section.style.display = '';
  }

  // CMS'dan raqam ham kelishi mumkin (masalan overallScore: 8.5) — xom raqamda `.replace`
  // metodi yo'q, TypeError chiqib BUTUN sertifikatlar ro'yxati chizilmay qolardi. Shu sabab
  // qiymat avval String() ga o'tkaziladi.
  function escapeHtml(str) {
    if (str === null || str === undefined || str === '') return '';
    return String(str).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  }

  // ═══════════════════════════════════ UZLUKSIZ LENTA (marquee) ═══════════════════════════════
  //
  // O'qituvchilar va sertifikatlar bo'limlari. Ilgari bu yerda ikkita bir xil "qadamli" karusel
  // turardi: har 3.5 soniyada bir slayd suriladi, oxiriga yetgach boshiga qaytadi, ustiga
  // strelka va nuqtalar. Ya'ni harakat to'xtab-to'xtab ketardi va oxirida sakrab qaytardi.
  // Endi lenta TO'XTAMASDAN, bir tekis aylanadi (CSS animatsiyasi — `.marquee-track`).
  //
  // PRINTSIP: ro'yxat bir necha marta TAKRORLANADI va animatsiya aynan BITTA takror uzunligiga
  // suriladi. Surilish tugagan lahzada ekranda aynan o'sha manzara turadi, shuning uchun
  // boshiga qaytish ko'zga ko'rinmaydi.
  //
  // ⚠️ Masofa CSS'da foiz bilan berilmaydi (`-50%` kabi): slaydlar trekdan kengroq bo'lib
  // TOSHIB turadi, ya'ni trekning o'z kengligi takror uzunligiga teng emas — foiz noto'g'ri
  // masofa berardi. Shu sabab u shu yerda PIKSELDA o'lchanadi.
  var MARQUEE_GAP = 24;     // `.teachers-track` / `.results-track` dagi `gap` bilan AYNAN bir xil
  var MARQUEE_SPEED = 55;   // piksel/soniya — o'qishga ulguradigan xotirjam sur'at

  /** Trekni o'lchab, takrorlarni chizadi va CSS o'zgaruvchilarini yangilaydi. */
  function layoutMarquee(track) {
    var baseCount = Number(track.getAttribute('data-base-count') || 0);
    if (!baseCount) return;

    // Oldingi takrorlar olib tashlanadi: ekran o'lchami o'zgarsa ularning SONI ham o'zgaradi.
    while (track.children.length > baseCount) track.removeChild(track.lastChild);

    var base = Array.prototype.slice.call(track.children);
    var wrapWidth = track.parentNode ? track.parentNode.getBoundingClientRect().width : 0;
    var blockWidth = 0;
    base.forEach(function(el) { blockWidth += el.getBoundingClientRect().width + MARQUEE_GAP; });
    // Bo'lim hali yashirin bo'lsa hamma o'lcham 0 chiqadi — bunda hech narsa qilinmaydi
    // (bo'lim ko'ringanda `initMarquee` qaytadan chaqiriladi).
    if (blockWidth <= 0) return;

    // Bitta takror ekranni TO'LDIRISHI shart, aks holda aylanish paytida o'ng tomonda bo'sh
    // joy ko'rinib qolardi (kartochkasi kam markazda — masalan 2 ta o'qituvchi).
    var reps = Math.max(1, Math.ceil(wrapWidth / blockWidth));
    var frag = document.createDocumentFragment();
    for (var r = 1; r < reps * 2; r++) {
      base.forEach(function(el) {
        var copy = el.cloneNode(true);
        // Nusxa — TAKROR mazmun: ekran o'qish dasturi uni ikkinchi marta o'qimasin.
        copy.setAttribute('aria-hidden', 'true');
        // ⚠️ Nusxadagi rasmlardan `loading="lazy"` OLIB TASHLANADI: nusxalar ekrandan tashqarida
        // turadi va lenta ularni surib kelganda rasm hali yuklanmagan bo'lib, kartochka bo'sh
        // ko'rinib qolishi mumkin edi. Manzillar asl slayd bilan BIR XIL — brauzer keshidan
        // olinadi, ya'ni qo'shimcha so'rov ketmaydi.
        var imgs = copy.querySelectorAll ? copy.querySelectorAll('img') : [];
        for (var k = 0; k < imgs.length; k++) imgs[k].removeAttribute('loading');
        frag.appendChild(copy);
      });
    }
    track.appendChild(frag);

    var shift = blockWidth * reps;
    track.style.setProperty('--marquee-shift', '-' + Math.round(shift) + 'px');
    track.style.setProperty('--marquee-duration', Math.max(8, Math.round(shift / MARQUEE_SPEED)) + 's');

    // Animatsiya YANGI qiymatlar bilan BOSHIDAN ketsin. Aks holda (masalan oyna o'lchami
    // o'zgarganda) brauzer o'tgan vaqtni saqlab qolib, lenta o'rtasidan sakrab ketardi.
    track.style.animation = 'none';
    void track.offsetWidth;   // reflow — "none" haqiqatan qo'llanishi uchun
    track.style.animation = '';
  }

  /** Chizilgan trekni uzluksiz lentaga aylantiradi (slaydlar allaqachon qo'yilgan bo'lishi kerak). */
  function initMarquee(track) {
    if (!track || track.children.length === 0) return;
    // Asl slaydlar soni ESLAB QOLINADI — qayta o'lchashda takrorlar shunga qarab tozalanadi.
    track.setAttribute('data-base-count', String(track.children.length));
    if (marqueeTracks.indexOf(track) === -1) marqueeTracks.push(track);
    layoutMarquee(track);
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

  // Oyna o'lchami o'zgarganda qayta o'lchash (debounce bilan — sudrab o'lchamni o'zgartirganda
  // har piksel uchun butun lentani qayta chizish shart emas).
  var marqueeResizeTimer = null;
  window.addEventListener('resize', function() {
    if (marqueeResizeTimer) clearTimeout(marqueeResizeTimer);
    marqueeResizeTimer = setTimeout(function() {
      marqueeTracks.forEach(layoutMarquee);
    }, 200);
  });
})();
