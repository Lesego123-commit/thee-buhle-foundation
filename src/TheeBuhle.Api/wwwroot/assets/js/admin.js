/* =============================================================================
   Staff console.
   Thee Buhle Foundation · INSY7315 Task 2

   Every screen here is rendered from a REST API call — the same API the mobile
   app uses. There is no server-rendered data in admin/index.html at all, which
   is deliberate: it means the API's authorisation rules are exercised by a
   second, independent client, so a rule that only appeared to work because the
   website asked nicely is caught here instead of in production.

   Session handling:
     * The access token lives in sessionStorage, cleared when the tab closes. A
       staff laptop at a Saturday session is a shared device.
     * The refresh token is used once, on load, to recover a session after a
       reload, and is rotated every time — presenting a used one revokes the
       whole family server-side.
     * A 401 from any call signs the console out rather than leaving a dead
       screen, because a 401 means the account was suspended or the token was
       revoked, and both should end the session.
   ============================================================================= */

(function () {
  'use strict';

  var $  = function (s, r) { return (r || document).querySelector(s); };
  var $$ = function (s, r) { return Array.prototype.slice.call((r || document).querySelectorAll(s)); };

  /**
   * True when the API refused this because of the values, not the request.
   *
   * ASP.NET Core answers model validation with 400 and a dictionary; the PHP
   * prototype used 422. Both are accepted here so that the check is about the
   * presence of per-field messages rather than about one framework's choice of
   * status code — which is what a caller actually wants to know.
   */
  function isValidationFailure(error) {
    return (error.status === 400 || error.status === 422)
      && Object.keys(error.fields).length > 0;
  }

  var STORE = {
    access: 'tb.admin.access',
    refresh: 'tb.admin.refresh',
    user: 'tb.admin.user'
  };

  var session = {
    access: null,
    refresh: null,
    user: null
  };

  /* --------------------------------------------------------------- helpers */

  function escapeHtml(value) {
    return String(value === null || value === undefined ? '' : value)
      .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
  }

  function rands(amount) {
    return 'R' + Number(amount || 0).toLocaleString('en-ZA', { maximumFractionDigits: 0 }).replace(/,/g, ' ');
  }

  function shortDate(value) {
    if (!value) { return '—'; }

    return new Date(String(value).replace(' ', 'T')).toLocaleDateString('en-ZA', {
      day: 'numeric', month: 'short', year: 'numeric'
    });
  }

  function dateTime(value) {
    if (!value) { return '—'; }

    return new Date(String(value).replace(' ', 'T')).toLocaleString('en-ZA', {
      day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit'
    });
  }

  function notify(variant, title, message) {
    var notice = $('#globalNotice');

    notice.className = 'notice notice--' + variant;
    notice.hidden = false;
    notice.setAttribute('role', 'status');
    notice.innerHTML = '<span aria-hidden="true">' + (variant === 'ok' ? '✓' : '⚠') + '</span>'
      + '<div><b>' + escapeHtml(title) + '</b>' + escapeHtml(message) + '</div>';

    if (variant === 'ok') {
      window.setTimeout(function () { notice.hidden = true; }, 6000);
    }
  }

  function formNotice(form, variant, title, message) {
    var notice = $('[data-notice]', form);

    if (!notice) { return notify(variant, title, message); }

    notice.className = 'notice notice--' + variant;
    notice.hidden = false;
    notice.setAttribute('role', 'status');
    notice.innerHTML = '<span aria-hidden="true">' + (variant === 'ok' ? '✓' : '⚠') + '</span>'
      + '<div><b>' + escapeHtml(title) + '</b>' + escapeHtml(message) + '</div>';
  }

  function bindFieldErrors(form, fields) {
    var first = null;

    Object.keys(fields).forEach(function (name) {
      var input = form.querySelector('[name="' + name + '"]');
      var holder = form.querySelector('[data-error-for="' + name + '"]');

      if (input) {
        input.setAttribute('aria-invalid', 'true');
        if (!first) { first = input; }
      }

      if (holder) { holder.textContent = fields[name]; }
    });

    if (first) { first.focus(); }
  }

  function clearFormErrors(form) {
    $$('[aria-invalid]', form).forEach(function (i) { i.removeAttribute('aria-invalid'); });
    $$('.field-error', form).forEach(function (e) { e.textContent = ''; });

    var notice = $('[data-notice]', form);
    if (notice) { notice.hidden = true; }
  }

  function busy(button, isBusy) {
    if (!button) { return; }

    button.dataset.busy = isBusy ? 'true' : 'false';
    button.disabled = isBusy;
  }

  /**
   * Every API call in the console goes through here, so a 401 is handled in one
   * place: the session ends rather than the screen silently emptying.
   */
  function call(promise) {
    return promise.catch(function (error) {
      if (error.status === 401) {
        signOut('Your session has ended. Please sign in again.');
      }

      throw error;
    });
  }

  function get(path)          { return call(window.TB.get(path, session.access)); }
  function post(path, body)   { return call(window.TB.post(path, body, session.access)); }
  function patch(path, body)  { return call(window.TB.patch(path, body, session.access)); }

  /* -------------------------------------------------------------- sign in */

  $('#signinForm').addEventListener('submit', function (event) {
    event.preventDefault();

    var form = event.currentTarget;
    var button = $('[type="submit"]', form);

    clearFormErrors(form);
    busy(button, true);

    window.TB.post('/auth/sign-in', {
      email: $('[name="email"]', form).value,
      password: $('[name="password"]', form).value,
      device: 'Staff console'
    }).then(function (result) {
      busy(button, false);
      startSession(result.data);
    }).catch(function (error) {
      busy(button, false);

      if (isValidationFailure(error)) {
        bindFieldErrors(form, error.fields);
      }

      // The message is identical for a wrong password, an unknown address and a
      // locked account — the API answers the same way for all three, so the
      // console cannot be used to work out which.
      formNotice(form, 'error', 'Could not sign in. ', error.message);
    });
  });

  function startSession(payload) {
    session.access = payload.accessToken;
    session.user = payload.user;

    // There is no refresh token here to keep. For a browser client the API
    // returns it as an HttpOnly cookie instead of in the body, so this console
    // cannot read it, cannot store it, and cannot leak it — the cookie rides
    // along on the refresh call by itself.
    try {
      sessionStorage.setItem(STORE.access, session.access);
      sessionStorage.setItem(STORE.user, JSON.stringify(session.user));
    } catch (e) {
      // Private browsing, or storage blocked. The session still works for this
      // page load; it simply will not survive a reload.
    }

    $('#signin').hidden = true;
    $('#console').hidden = false;

    $('#whoami').textContent = session.user.displayName + ' · ' + session.user.roleLabel;

    applyRoleVisibility();
    show('overview');
  }

  function signOut(message) {
    session = { access: null, refresh: null, user: null };

    try {
      sessionStorage.removeItem(STORE.access);
      sessionStorage.removeItem(STORE.user);
    } catch (e) { /* nothing to clear */ }

    // Best effort: revoke the refresh token server-side and clear its cookie,
    // so a copy of it is useless. The console signs out either way — a staff
    // member who clicked Sign out is signed out of this tab whatever the
    // network does.
    window.TB.post('/auth/sign-out', {}).catch(function () {});

    $('#console').hidden = true;
    $('#signin').hidden = false;

    if (message) {
      formNotice($('#signinForm'), 'warn', 'Signed out. ', message);
    }
  }

  $('#signout').addEventListener('click', function () { signOut(null); });

  /**
   * Only shows the sections the signed-in role can actually reach.
   *
   * This is courtesy, not security: the API refuses these endpoints for the
   * wrong role regardless, and the integration tests assert that. Hiding a tab
   * a staff member would only be told "no" about is better interface design,
   * and nothing more than that.
   */
  function applyRoleVisibility() {
    // 'SafeguardingLead' is a role the API derives from the staff record's own
    // flag rather than one stored on the account, so there is exactly one of
    // them and it cannot drift from who may read child-safety reports.
    var role = session.user.role;
    var canSafeguard = role === 'SafeguardingLead' || role === 'Admin';
    var canAudit = canSafeguard;

    $$('[data-requires="safeguarding"]').forEach(function (tab) { tab.hidden = !canSafeguard; });
    $$('[data-requires="audit"]').forEach(function (tab) { tab.hidden = !canAudit; });
  }

  /* ------------------------------------------------------------ navigation */

  $('#adminNav').addEventListener('click', function (event) {
    var button = event.target.closest('[data-view]');

    if (button) { show(button.dataset.view); }
  });

  var loaders = {};

  function show(view) {
    $$('#adminNav [data-view]').forEach(function (tab) {
      tab.setAttribute('aria-pressed', String(tab.dataset.view === view));
    });

    $$('.view').forEach(function (section) {
      section.hidden = section.dataset.view !== view;
    });

    if (loaders[view]) { loaders[view](); }
  }

  /* -------------------------------------------------------------- overview */

  /**
   * The overview, composed from the endpoints that already exist.
   *
   * There is no /admin/overview on this API and this console does not ask for
   * one. Composing the morning's picture from the same endpoints the other
   * views use means the overview cannot show a staff member a number they are
   * not allowed to drill into: the safeguarding count is fetched only for the
   * role that may read the queue, and every other tile comes from a call that
   * would have succeeded anyway.
   *
   * Promise.all rather than a sequence, so five small calls cost one round
   * trip's worth of waiting on a Vaal connection rather than five.
   */
  loaders.overview = function () {
    var target = $('#overviewBody');
    var canSafeguard = session.user
      && (session.user.role === 'SafeguardingLead' || session.user.role === 'Admin');

    // A failed tile must not blank the whole screen, so each call resolves to a
    // fallback rather than rejecting the batch.
    function soft(promise, fallback) {
      return promise.then(function (result) { return result.data; })
        .catch(function () { return fallback; });
    }

    Promise.all([
      soft(get('/attendance/sessions/recent'), []),
      soft(get('/admin/learners?page=1&pageSize=1'), { total: 0 }),
      soft(get('/admin/volunteers?status=Submitted&page=1&pageSize=1'), { total: 0 }),
      soft(get('/admin/contact-messages?status=New&page=1&pageSize=1'), { total: 0 }),
      soft(get('/admin/donations/totals'), { total: 0, count: 0, monthlyCount: 0 }),
      canSafeguard ? soft(get('/safeguarding/concerns'), []) : Promise.resolve([])
    ]).then(function (parts) {
      var sessions = parts[0] || [];
      var learners = parts[1] || { total: 0 };
      var volunteers = parts[2] || { total: 0 };
      var enquiries = parts[3] || { total: 0 };
      var giving = parts[4] || { total: 0, count: 0, monthlyCount: 0 };
      var concerns = parts[5] || [];

      var html = '<div class="tiles">'
        + tile(learners.total, 'learners on the roll', null)
        + tile(sessions.length, 'recent sessions', 'registers in the last few weeks')
        + tile(rands(giving.total), 'received', giving.count + ' completed gifts')
        + tile(giving.monthlyCount, 'monthly givers', 'recurring commitments')
        + '</div>';

      // Queues that need somebody to do something, shown only when they are
      // non-zero — so a clear morning looks clear rather than looking like
      // three zeroes somebody has to read before trusting them.
      var queues = [
        [volunteers.total, 'volunteer applications', 'waiting to be screened', 'volunteers'],
        [enquiries.total, 'website enquiries', 'unanswered', 'messages']
      ];

      if (canSafeguard) {
        queues.push([concerns.length, 'flagged concerns', 'in the safeguarding queue', 'safeguarding']);
      }

      queues = queues.filter(function (q) { return q[0] > 0; });

      if (queues.length) {
        html += '<h2 class="d4" style="margin:34px 0 16px">Needs attention</h2><div class="tiles">'
          + queues.map(function (q) {
            return '<button class="tile tile--attention" data-goto="' + q[3] + '" style="text-align:left;cursor:pointer">'
              + '<b>' + escapeHtml(q[0]) + '</b><span>' + escapeHtml(q[1]) + '</span>'
              + '<small>' + escapeHtml(q[2]) + '</small></button>';
          }).join('')
          + '</div>';
      } else {
        html += '<div class="notice notice--ok" style="margin-top:28px;max-width:600px">'
          + '<span aria-hidden="true">✓</span><div><b>Nothing waiting.</b>'
          + 'No unscreened volunteers and no unanswered enquiries.</div></div>';
      }

      html += '<h2 class="d4" style="margin:40px 0 16px">Registers</h2>';

      if (sessions.length === 0) {
        html += emptyState('No sessions yet', 'Registers appear here once sessions have been scheduled.');
      } else {
        html += sessions.map(function (s) {
          var expected = Number(s.expected);
          var marked = Number(s.marked);
          var complete = expected > 0 && marked >= expected;

          return '<div class="datarow">'
            + '<div class="grow"><h3>' + escapeHtml(s.subjectFocus || s.programmeName) + '</h3>'
            + '<p class="quiet" style="margin:4px 0 0">' + escapeHtml(dateTime(s.startsAt))
            + ' · ' + escapeHtml(s.venueName) + (s.room ? ', ' + escapeHtml(s.room) : '') + '</p></div>'
            + '<span class="pill ' + (complete ? 'pill--ok' : 'pill--warn') + '">'
            + marked + ' of ' + expected + (complete ? ' — complete' : ' marked') + '</span>'
            + '</div>';
        }).join('');
      }

      target.innerHTML = html;

      $$('[data-goto]', target).forEach(function (button) {
        button.addEventListener('click', function () { show(button.dataset.goto); });
      });
    }).catch(function (error) {
      target.innerHTML = errorState(error);
    });
  };

  function tile(value, label, note) {
    return '<div class="tile"><b>' + escapeHtml(value) + '</b><span>' + escapeHtml(label) + '</span>'
      + (note ? '<small>' + escapeHtml(note) + '</small>' : '') + '</div>';
  }

  function emptyState(title, body) {
    return '<div class="empty"><b>' + escapeHtml(title) + '</b>' + escapeHtml(body) + '</div>';
  }

  function errorState(error) {
    return '<div class="notice notice--error"><span aria-hidden="true">⚠</span>'
      + '<div><b>Could not load that.</b>' + escapeHtml(error.message) + '</div></div>';
  }

  /* -------------------------------------------------------------- register */

  var registerState = { sessionId: null, marks: {} };

  loaders.register = function () {
    var picker = $('#sessionPicker');

    if (picker.dataset.loaded === 'true') { return; }

    get('/attendance/sessions/recent').then(function (result) {
      var sessions = result.data || [];

      picker.innerHTML = '<option value="">Choose a session…</option>'
        + sessions.map(function (s) {
          return '<option value="' + escapeHtml(s.sessionId) + '">'
            + escapeHtml(dateTime(s.startsAt)) + ' — ' + escapeHtml(s.subjectFocus || s.programmeName)
            + ' (' + escapeHtml(s.venueName) + ')</option>';
        }).join('');

      picker.dataset.loaded = 'true';
    }).catch(function (error) {
      picker.innerHTML = '<option value="">Could not load sessions</option>';
      notify('error', 'Could not load sessions. ', error.message);
    });
  };

  $('#sessionPicker').addEventListener('change', function (event) {
    var sessionId = event.target.value;

    if (!sessionId) {
      $('#registerBody').innerHTML = '';

      return;
    }

    loadRegister(sessionId);
  });

  function loadRegister(sessionId) {
    var target = $('#registerBody');

    target.innerHTML = '<div class="skeleton" style="height:240px"></div>';

    get('/attendance/sessions/' + encodeURIComponent(sessionId) + '/register').then(function (result) {
      registerState.sessionId = sessionId;
      registerState.marks = {};

      var register = result.data || [];

      if (register.length === 0) {
        target.innerHTML = emptyState(
          'Nobody is enrolled in this session’s programme',
          'Enrol learners in the programme before capturing a register — the API refuses a mark for a '
          + 'learner who is not enrolled, because it would corrupt both their attendance rate and the '
          + 'programme figures on the public Impact page.'
        );

        return;
      }

      // Pre-select whatever was already captured, so re-opening a register shows
      // the current state rather than a blank one.
      register.forEach(function (row) {
        if (row.status) { registerState.marks[row.learnerId] = row.status; }
      });

      // The values are the API's own enum names, so what a button holds is
      // exactly what is posted — no mapping table between the screen and the
      // request, and nothing to keep in step when a status is added.
      var statuses = [
        ['Present', 'Present'],
        ['Late', 'Late'],
        ['Absent', 'Absent'],
        ['Excused', 'Excused']
      ];

      target.innerHTML = '<div class="row" style="margin-bottom:16px">'
        + '<button class="btn btn--ghost btn--s" id="markAllPresent">Mark everybody present</button>'
        + '<span class="quiet">Then change the few who were not.</span></div>'
        + '<div class="register-grid">'
        + register.map(function (row) {
          return '<div class="register-row" data-learner="' + escapeHtml(row.learnerId) + '">'
            + '<span class="ref">' + escapeHtml(row.referenceCode) + '</span>'
            + '<div class="who"><b>' + escapeHtml(row.firstName + ' ' + row.surname) + '</b>'
            + '<span>Grade ' + escapeHtml(row.grade) + '</span></div>'
            + '<div class="status-set" role="group" aria-label="Attendance for '
            + escapeHtml(row.firstName + ' ' + row.surname) + '">'
            + statuses.map(function (s) {
              var pressed = registerState.marks[row.learnerId] === s[0];

              return '<button type="button" class="tog" data-status="' + s[0] + '" aria-pressed="'
                + pressed + '">' + s[1] + '</button>';
            }).join('')
            + '</div></div>';
        }).join('')
        + '</div>'
        + '<div class="register-summary">'
        + '<span id="registerCount" class="quiet"></span>'
        + '<button class="btn btn--forest spread" id="saveRegister">Save register</button>'
        + '</div>';

      wireRegister(target, register.length);
      updateRegisterCount(register.length);
    }).catch(function (error) {
      target.innerHTML = errorState(error);
    });
  }

  function wireRegister(target, total) {
    target.addEventListener('click', function (event) {
      var button = event.target.closest('.status-set [data-status]');

      if (!button) { return; }

      var row = button.closest('[data-learner]');

      $$('[data-status]', button.parentElement).forEach(function (sibling) {
        sibling.setAttribute('aria-pressed', 'false');
      });

      button.setAttribute('aria-pressed', 'true');
      registerState.marks[row.dataset.learner] = button.dataset.status;

      updateRegisterCount(total);
    });

    $('#markAllPresent').addEventListener('click', function () {
      $$('[data-learner]', target).forEach(function (row) {
        $$('[data-status]', row).forEach(function (button) {
          button.setAttribute('aria-pressed', String(button.dataset.status === 'Present'));
        });

        registerState.marks[row.dataset.learner] = 'Present';
      });

      updateRegisterCount(total);
    });

    $('#saveRegister').addEventListener('click', function (event) {
      saveRegister(event.currentTarget, total);
    });
  }

  function updateRegisterCount(total) {
    var marked = Object.keys(registerState.marks).length;
    var counter = $('#registerCount');

    if (counter) {
      counter.textContent = marked + ' of ' + total + ' marked'
        + (marked < total ? ' · ' + (total - marked) + ' still to go' : ' · ready to save');
    }
  }

  function saveRegister(button, total) {
    var entries = Object.keys(registerState.marks).map(function (learnerId) {
      return { learnerId: learnerId, status: registerState.marks[learnerId] };
    });

    if (entries.length === 0) {
      notify('warn', 'Nothing to save. ', 'Mark at least one learner first.');

      return;
    }

    if (entries.length < total
      && !window.confirm(
        'Only ' + entries.length + ' of ' + total + ' learners are marked.\n\n'
        + 'Save anyway? You can come back and finish the rest.'
      )) {
      return;
    }

    busy(button, true);

    post('/attendance/sessions/' + encodeURIComponent(registerState.sessionId) + '/register', { entries: entries })
      .then(function (result) {
        busy(button, false);

        // Built from the counts the API returns rather than a message it does
        // not send, and it distinguishes a new mark from a changed one —
        // amending a register is the case a staff member wants confirmed.
        var recorded = result.data.recorded;
        var amended = result.data.amended;

        notify(
          'ok',
          'Register saved. ',
          recorded + (recorded === 1 ? ' mark' : ' marks') + ' recorded'
            + (amended > 0 ? ', ' + amended + ' amended' : '') + '.'
        );
      })
      .catch(function (error) {
        busy(button, false);

        // A 409 here is a business rule, not a bug: a learner who is not
        // enrolled, or a mark another staff member captured a moment ago.
        notify(error.status === 409 ? 'warn' : 'error', 'Register not saved. ', error.message);
      });
  }

  /* -------------------------------------------------------------- learners */

  var learnerSearchTimer = null;

  loaders.learners = function () { loadLearners(); };

  ['input', 'change'].forEach(function (eventName) {
    $('#learnerSearch').addEventListener(eventName, function () {
      // Debounced, so typing a name is one request rather than eight.
      window.clearTimeout(learnerSearchTimer);
      learnerSearchTimer = window.setTimeout(loadLearners, 350);
    });
  });

  $('#learnerGrade').addEventListener('change', loadLearners);

  // Grades 1–12, built once.
  (function () {
    var select = $('#learnerGrade');

    for (var grade = 1; grade <= 12; grade++) {
      var option = document.createElement('option');
      option.value = String(grade);
      option.textContent = 'Grade ' + grade;
      select.appendChild(option);
    }
  }());

  function loadLearners() {
    var target = $('#learnersBody');
    var query = [];
    var search = $('#learnerSearch').value.trim();
    var grade = $('#learnerGrade').value;

    // The API names this parameter `query`; calling it `q` returned the whole
    // roll however carefully somebody typed a name.
    if (search) { query.push('query=' + encodeURIComponent(search)); }
    if (grade) { query.push('grade=' + encodeURIComponent(grade)); }

    get('/admin/learners' + (query.length ? '?' + query.join('&') : '')).then(function (result) {
      var page = result.data || { items: [], total: 0 };

      if (page.items.length === 0) {
        target.innerHTML = emptyState('No learners match that', 'Try a different name, reference code or grade.');

        return;
      }

      target.innerHTML = '<p class="quiet" style="margin:0 0 14px">'
        + page.total + ' learners · showing ' + page.items.length + '</p>'
        + page.items.map(function (learner) {
          var rate = Number(learner.attendanceRate);
          var pill = rate >= 75 ? 'pill--ok' : (rate >= 50 ? 'pill--warn' : 'pill--danger');

          return '<div class="datarow">'
            + '<span class="ref" style="font-family:var(--mono);font-size:12px;color:var(--ink40);min-width:60px">'
            + escapeHtml(learner.referenceCode) + '</span>'
            + '<div class="grow"><h3>' + escapeHtml(learner.firstName + ' ' + learner.surname) + '</h3>'
            + '<p class="quiet" style="margin:4px 0 0">Grade ' + escapeHtml(learner.grade)
            + (learner.school ? ' · ' + escapeHtml(learner.school) : '')
            + (learner.suburb ? ' · ' + escapeHtml(learner.suburb) : '') + '</p></div>'
            + '<span class="pill ' + pill + '">' + rate + '% attendance</span>'
            + '</div>';
        }).join('');
    }).catch(function (error) {
      target.innerHTML = errorState(error);
    });
  }

  /* --------------------------------------------------------- announcements */

  $('#actionRequired').addEventListener('change', function (event) {
    // A deadline only means something for an announcement that needs action, so
    // the field appears with the checkbox rather than sitting there empty.
    $('#dueDateField').hidden = !event.target.checked;
  });

  $('#announceForm').addEventListener('submit', function (event) {
    event.preventDefault();

    var form = event.currentTarget;
    var button = $('[type="submit"]', form);

    clearFormErrors(form);
    busy(button, true);

    var payload = {
      title: $('[name="title"]', form).value,
      // Newlines become paragraphs. The server sanitises this against an
      // eleven-tag allow-list on write, so this is a convenience rather than a
      // trust boundary — nothing here is what keeps script out of the parent app.
      bodyHtml: $('[name="bodyHtml"]', form).value
        .split(/\n{2,}/)
        .map(function (block) { return '<p>' + escapeHtml(block.trim()).replace(/\n/g, '<br>') + '</p>'; })
        .join(''),
      category: $('[name="category"]', form).value,
      audience: $('[name="audience"]', form).value,
      actionRequired: $('#actionRequired').checked
    };

    if (payload.actionRequired) {
      payload.dueDate = $('[name="dueDate"]', form).value;
    }

    post('/admin/announcements', payload).then(function (result) {
      busy(button, false);
      form.reset();
      $('#dueDateField').hidden = true;
      // The recipient count, which is what the person publishing wants to
      // know — an announcement that reached nobody looks identical to one
      // that reached two hundred until you say so.
      var recipients = result.data.recipients;

      formNotice(
        form,
        'ok',
        'Published. ',
        'Delivered to ' + recipients + (recipients === 1 ? ' person' : ' people') + '.'
      );
    }).catch(function (error) {
      busy(button, false);

      if (isValidationFailure(error)) {
        bindFieldErrors(form, error.fields);
      }

      formNotice(form, 'error', 'Not published. ', error.message);
    });
  });

  /* ----------------------------------------------------------- volunteers */

  loaders.volunteers = function () {
    var target = $('#volunteersBody');

    get('/admin/volunteers').then(function (result) {
      var page = result.data || { items: [] };

      if (page.items.length === 0) {
        target.innerHTML = emptyState('No applications', 'Applications from the website appear here.');

        return;
      }

      // The API's own enum names, so the value posted back is the value it
      // validates. 'Withdrawn' is not offered: a volunteer withdraws, staff do
      // not withdraw them.
      var statuses = ['Submitted', 'Screening', 'Cleared', 'Declined'];

      target.innerHTML = page.items.map(function (application) {
        return '<div class="datarow" data-application="' + escapeHtml(application.applicationId) + '">'
          + '<div class="grow">'
          + '<h3>' + escapeHtml(application.fullName) + '</h3>'
          + '<p class="quiet" style="margin:4px 0 8px">'
          + escapeHtml(application.email) + ' · ' + escapeHtml(application.phone)
          + (application.suburb ? ' · ' + escapeHtml(application.suburb) : '') + '</p>'
          + '<div class="chipset" style="gap:6px">'
          + (application.skills || []).map(function (skill) {
            return '<span class="chip">' + escapeHtml(skill) + '</span>';
          }).join('')
          + '</div>'
          + (application.experience
            ? '<p class="quiet" style="margin:10px 0 0;max-width:70ch">' + escapeHtml(application.experience) + '</p>'
            : '')
          + '<p class="dim" style="margin:10px 0 0;font-size:12px">Applied ' + escapeHtml(shortDate(application.submittedAt)) + '</p>'
          + '</div>'
          + '<div style="display:grid;gap:8px;min-width:170px">'
          + '<span class="pill ' + statusPill(application.status) + '">' + escapeHtml(application.status) + '</span>'
          + '<select class="input" data-status-for="' + escapeHtml(application.applicationId) + '" style="font-size:13px;padding:9px">'
          + statuses.map(function (s) {
            return '<option value="' + s + '"' + (s === application.status ? ' selected' : '') + '>' + s + '</option>';
          }).join('')
          + '</select></div></div>';
      }).join('');

      $$('[data-status-for]', target).forEach(function (select) {
        select.addEventListener('change', function () {
          // POST to a /status sub-resource rather than PATCH on the
          // application: the API models a status change as its own action,
          // because it records who made it and when.
          post('/admin/volunteers/' + encodeURIComponent(select.dataset.statusFor) + '/status', { status: select.value })
            .then(function () {
              notify('ok', 'Status updated. ', 'The change is recorded in the audit log.');
              loaders.volunteers();
            })
            .catch(function (error) {
              notify('error', 'Could not update. ', error.message);
            });
        });
      });
    }).catch(function (error) {
      target.innerHTML = errorState(error);
    });
  };

  function statusPill(status) {
    if (status === 'Cleared') { return 'pill--ok'; }
    if (status === 'Declined' || status === 'Withdrawn') { return 'pill--danger'; }
    if (status === 'Screening') { return 'pill--warn'; }

    return 'pill--neutral';
  }

  /* ---------------------------------------------------------------- people */

  /**
   * People and accounts.
   *
   * There is no public sign-up: staff add a person here and hand them a
   * one-time code, which they redeem in the app or at /activate to choose
   * their own password. A forgotten password is a reset code issued the same
   * way, after staff have confirmed who is asking.
   *
   * The code comes back from the API once and is shown once. Only its hash is
   * stored, so "Done" clears it from the page as well.
   */
  var peopleSearchTimer = null;
  var childRoll = [];

  loaders.people = function () {
    var admin = session.user.role === 'Admin';

    // Hidden and refused are different things: the API answers 403 to
    // programme staff regardless. Disabling the option just saves the trip.
    $$('#personKind [data-admin-only]').forEach(function (option) {
      option.disabled = !admin;
      option.textContent = admin ? 'Staff member' : 'Staff member (administrators only)';
    });

    fillGrades();
    loadProgrammes();
    loadChildRoll();
    loadPeople();
  };

  function roleLabel(role) {
    return {
      Parent: 'Parent', Learner: 'Learner', Mentor: 'Mentor', Staff: 'Staff',
      SafeguardingLead: 'Safeguarding lead', Admin: 'Administrator'
    }[role] || role;
  }

  function fillGrades() {
    var select = $('#gradeSelect');

    if (select.options.length) { return; }

    for (var grade = 1; grade <= 12; grade++) {
      select.insertAdjacentHTML('beforeend',
        '<option value="' + grade + '"' + (grade === 8 ? ' selected' : '') + '>Grade ' + grade + '</option>');
    }
  }

  function loadProgrammes() {
    var select = $('#programmeSelect');

    if (select.options.length) { return; }

    get('/public/programmes').then(function (result) {
      select.innerHTML = (result.data || []).map(function (programme) {
        return '<option value="' + escapeHtml(programme.programmeId) + '">' + escapeHtml(programme.name) + '</option>';
      }).join('') + '<option value="">Not yet</option>';
    }).catch(function () {
      select.innerHTML = '<option value="">Not yet</option>';
    });
  }

  /** The learners a new parent can be linked to: the enrolled roll. */
  function loadChildRoll() {
    get('/admin/learners?pageSize=100').then(function (result) {
      childRoll = (result.data && result.data.items) || [];
      renderChildren();
    }).catch(function (error) {
      $('#childList').innerHTML = '<p class="quiet">' + escapeHtml(error.message) + '</p>';
    });
  }

  function renderChildren() {
    var term = $('#childSearch').value.trim().toLowerCase();
    var checked = $$('#childList input:checked').map(function (input) { return input.value; });

    // A ticked learner stays in the list while the filter changes, so
    // searching for a second child does not silently drop the first.
    var rows = childRoll.filter(function (learner) {
      var text = (learner.firstName + ' ' + learner.surname + ' ' + learner.referenceCode).toLowerCase();

      return !term || text.indexOf(term) >= 0 || checked.indexOf(learner.learnerId) >= 0;
    });

    $('#childList').innerHTML = rows.length === 0
      ? '<p class="quiet" style="margin:6px 0">No enrolled learner matches. Add the learner first.</p>'
      : rows.map(function (learner) {
        return '<label class="childopt"><input type="checkbox" value="' + escapeHtml(learner.learnerId) + '"'
          + (checked.indexOf(learner.learnerId) >= 0 ? ' checked' : '') + '>'
          + '<span>' + escapeHtml(learner.firstName + ' ' + learner.surname)
          + ' <span class="dim">' + escapeHtml(learner.referenceCode) + ' · Grade ' + escapeHtml(learner.grade) + '</span></span></label>';
      }).join('');
  }

  $('#childSearch').addEventListener('input', renderChildren);

  function loadPeople() {
    var target = $('#peopleBody');
    var query = $('#peopleSearch').value.trim();
    var role = $('#peopleRole').value;
    var path = '/admin/people?pageSize=50'
      + (query ? '&query=' + encodeURIComponent(query) : '')
      + (role ? '&role=' + encodeURIComponent(role) : '');

    get(path).then(function (result) {
      var page = result.data || { items: [], total: 0 };
      var admin = session.user.role === 'Admin';

      if (page.items.length === 0) {
        target.innerHTML = emptyState('Nobody found', query || role ? 'Try a different search.' : 'Add the first person with the form.');

        return;
      }

      target.innerHTML = page.items.map(function (person) {
        var staffSide = ['Staff', 'SafeguardingLead', 'Admin'].indexOf(person.role) >= 0;
        var action = person.status === 'Invited' ? 'New invite code'
          : person.status === 'Active' ? 'Reset password' : null;
        var status = person.status === 'Invited'
          ? (person.codeExpiresAt ? 'Invited · code valid to ' + shortDate(person.codeExpiresAt) : 'Invited · code expired')
          : person.status;
        var pill = person.status === 'Active' ? 'pill--ok' : person.status === 'Invited' ? 'pill--warn' : 'pill--danger';

        return '<div class="datarow">'
          + '<div class="grow">'
          + '<h3>' + escapeHtml(person.name) + '</h3>'
          + '<p class="quiet" style="margin:4px 0 0">' + escapeHtml(roleLabel(person.role)) + ' · ' + escapeHtml(person.email) + '</p>'
          + (person.detail ? '<p class="dim" style="margin:4px 0 0;font-size:12.5px">' + escapeHtml(person.detail) + '</p>' : '')
          + '</div>'
          + '<div style="display:grid;gap:8px;justify-items:end;min-width:150px">'
          + '<span class="pill ' + pill + '">' + escapeHtml(status) + '</span>'
          // Only an administrator can issue a code to a staff account; the
          // API refuses anyone else with a 403 either way.
          + (action && (admin || !staffSide)
            ? '<button class="btn btn--ghost btn--s" type="button" data-code-for="' + escapeHtml(person.userId)
              + '" data-code-action="' + escapeHtml(action) + '" data-name="' + escapeHtml(person.name) + '">' + escapeHtml(action) + '</button>'
            : '')
          + '</div></div>';
      }).join('')
        + (page.total > page.items.length
          ? '<p class="quiet" style="margin-top:12px">Showing ' + page.items.length + ' of ' + page.total + '. Search to narrow it down.</p>'
          : '');

      $$('[data-code-for]', target).forEach(function (button) {
        button.addEventListener('click', function () { issueCode(button); });
      });
    }).catch(function (error) {
      target.innerHTML = errorState(error);
    });
  }

  $('#peopleSearch').addEventListener('input', function () {
    window.clearTimeout(peopleSearchTimer);
    peopleSearchTimer = window.setTimeout(loadPeople, 300);
  });

  $('#peopleRole').addEventListener('change', loadPeople);

  function issueCode(button) {
    var reset = button.dataset.codeAction === 'Reset password';

    // A reset code is a way into somebody's account. The confirmation is the
    // moment to check that it really is them asking.
    if (reset && !window.confirm('Issue a password reset code for ' + button.dataset.name + '?\n\n'
      + 'Only do this once you have confirmed it really is them asking. Any earlier code stops working.')) {
      return;
    }

    busy(button, true);

    post('/admin/people/' + encodeURIComponent(button.dataset.codeFor) + '/code', {}).then(function (result) {
      busy(button, false);
      showCode(result.data, button.dataset.name);
      loadPeople();
    }).catch(function (error) {
      busy(button, false);
      notify('error', 'No code issued. ', error.message);
    });
  }

  function showCode(issued, name) {
    var panel = $('#codePanel');
    var invite = issued.purpose === 'Invite';
    var until = shortDate(issued.expiresAt);

    $('#codeTitle').textContent = (invite ? 'Invitation ready for ' : 'Reset code ready for ') + name;
    $('#codeEmail').textContent = issued.email;
    $('#codeValue').textContent = issued.code;
    $('#codeNote').textContent = (issued.emailed
      ? 'It has also been emailed to them. '
      : 'The foundation’s email is not set up yet, so send it to them yourself. ')
      + 'It works once, until ' + until + '.';
    $('#codeMessage').value = 'Thee Buhle Foundation: '
      + (invite ? 'you have been added to our app. ' : 'here is the password reset you asked for. ')
      + 'Your code is ' + issued.code + ', valid until ' + until + '. '
      + 'Open the Thee Buhle app and tap "I have a code", or go to theebuhlefoundation.co.za/activate, '
      + 'and use the email address ' + issued.email + '.';

    panel.hidden = false;
    panel.focus();
    panel.scrollIntoView({ block: 'start', behavior: 'smooth' });
  }

  $('#codeCopy').addEventListener('click', function () {
    var message = $('#codeMessage');
    var copied = navigator.clipboard ? navigator.clipboard.writeText(message.value) : Promise.reject(new Error('no clipboard'));

    copied.then(function () {
      notify('ok', 'Copied. ', 'Paste it into an SMS or WhatsApp.');
    }).catch(function () {
      message.select();
      notify('ok', 'Selected. ', 'Press Ctrl+C to copy it.');
    });
  });

  $('#codeDone').addEventListener('click', function () {
    $('#codePanel').hidden = true;
    $('#codeValue').textContent = '';
    $('#codeMessage').value = '';
  });

  function applyPersonKind() {
    var kind = $('#personKind').value;

    $$('#personForm [data-for-kind]').forEach(function (block) {
      block.hidden = block.dataset.forKind !== kind;
    });

    // Everyone but a learner signs in, so everyone but a learner needs an address.
    $('#personForm [name="email"]').required = kind !== 'learners';
  }

  $('#personKind').addEventListener('change', applyPersonKind);
  applyPersonKind();

  $('#personForm').addEventListener('submit', function (event) {
    event.preventDefault();

    var form = event.currentTarget;
    var button = $('[type="submit"]', form);
    var kind = $('#personKind').value;

    function value(name) {
      var input = form.querySelector('[name="' + name + '"]');

      return input ? input.value.trim() : '';
    }

    function checked(name) {
      var input = form.querySelector('[name="' + name + '"]');

      return Boolean(input && input.checked);
    }

    clearFormErrors(form);

    var payload = { firstName: value('firstName'), surname: value('surname') };

    if (value('email')) { payload.email = value('email'); }

    if (kind === 'parents') {
      payload.phone = value('phone');
      payload.relationship = value('relationship');
      payload.learnerIds = $$('#childList input:checked').map(function (input) { return input.value; });

      if (payload.learnerIds.length === 0) {
        bindFieldErrors(form, { learnerIds: 'Tick at least one learner.' });

        return;
      }
    } else if (kind === 'learners') {
      payload.dateOfBirth = value('dateOfBirth');
      payload.grade = Number(value('grade'));
      if (value('school')) { payload.school = value('school'); }
      if (value('suburb')) { payload.suburb = value('suburb'); }
      if (value('programmeId')) { payload.programmeId = value('programmeId'); }

      if (!payload.dateOfBirth) {
        bindFieldErrors(form, { dateOfBirth: 'Enter their date of birth.' });

        return;
      }
    } else if (kind === 'mentors') {
      if (value('occupation')) { payload.occupation = value('occupation'); }
      if (value('clearedUntil')) { payload.clearedUntil = value('clearedUntil'); }
    } else if (kind === 'staff') {
      payload.jobTitle = value('jobTitle');
      payload.isSafeguardingLead = checked('isSafeguardingLead');
      payload.isAdministrator = checked('isAdministrator');
    }

    busy(button, true);

    post('/admin/people/' + kind, payload).then(function (result) {
      var created = result.data;
      var name = payload.firstName + ' ' + payload.surname;

      busy(button, false);
      form.reset();
      $('#personKind').value = kind;
      applyPersonKind();
      renderChildren();

      if (created.invite) {
        showCode(created.invite, name);
        formNotice(form, 'ok', 'Added. ', name + (created.referenceCode ? ' (' + created.referenceCode + ')' : '')
          + ' has an account waiting. Give them the code above.');
      } else {
        formNotice(form, 'ok', 'Added. ', name + ' is on the roll as ' + created.referenceCode + '. No login, so no code.');
      }

      loadPeople();

      if (kind === 'learners') { loadChildRoll(); }
    }).catch(function (error) {
      busy(button, false);

      if (isValidationFailure(error)) {
        formNotice(form, 'error', 'Check the highlighted fields. ', error.message);
        bindFieldErrors(form, error.fields);

        return;
      }

      formNotice(form, 'error', 'Not added. ', error.message);
    });
  });

  /* -------------------------------------------------------------- messages */

  loaders.messages = function () {
    var target = $('#messagesBody');

    get('/admin/contact-messages').then(function (result) {
      var page = result.data || { items: [] };

      if (page.items.length === 0) {
        target.innerHTML = emptyState('No enquiries', 'Messages from the contact form appear here.');

        return;
      }

      target.innerHTML = page.items.map(function (message) {
        var isConcern = message.topic === 'Concern';

        return '<div class="datarow' + (isConcern ? ' datarow--concern' : '') + '">'
          + '<div class="grow">'
          + '<div class="row" style="gap:8px;margin-bottom:6px">'
          + '<span class="pill ' + (isConcern ? 'pill--danger' : 'pill--neutral') + '">'
          + escapeHtml(isConcern ? 'REPORTED CONCERN' : message.topic) + '</span>'
          + '<span class="pill ' + statusPill(message.status) + '">' + escapeHtml(message.status) + '</span>'
          + '</div>'
          + '<h3>' + escapeHtml(message.name)
          + (message.organisation ? ' · ' + escapeHtml(message.organisation) : '') + '</h3>'
          + '<p class="quiet" style="margin:4px 0 8px">'
          + '<a href="mailto:' + escapeHtml(message.email) + '">' + escapeHtml(message.email) + '</a>'
          + (message.phone ? ' · ' + escapeHtml(message.phone) : '') + '</p>'
          + '<p style="margin:0;max-width:78ch;font-size:14px;line-height:1.7">' + escapeHtml(message.body) + '</p>'
          + '<p class="dim" style="margin:10px 0 0;font-size:12px">' + escapeHtml(dateTime(message.createdAt)) + '</p>'
          + '</div>'
          + '<div style="display:grid;gap:8px;min-width:160px">'
          + '<select class="input" data-message-status="' + escapeHtml(message.messageId) + '" style="font-size:13px;padding:9px">'
          + [['New', 'new'], ['InProgress', 'in progress'], ['Answered', 'answered'], ['Closed', 'closed']]
            .map(function (s) {
              return '<option value="' + s[0] + '"' + (s[0] === message.status ? ' selected' : '') + '>'
                + s[1] + '</option>';
            }).join('')
          + '</select>'
          + '<a class="btn btn--ghost btn--s" href="mailto:' + escapeHtml(message.email) + '">Reply</a>'
          + '</div></div>';
      }).join('');

      $$('[data-message-status]', target).forEach(function (select) {
        select.addEventListener('change', function () {
          post('/admin/contact-messages/' + encodeURIComponent(select.dataset.messageStatus) + '/status', { status: select.value })
            .then(function () { notify('ok', 'Updated. ', 'Enquiry status changed.'); })
            .catch(function (error) { notify('error', 'Could not update. ', error.message); });
        });
      });
    }).catch(function (error) {
      target.innerHTML = errorState(error);
    });
  };

  /* ------------------------------------------------------------- donations */

  loaders.donations = function () {
    var target = $('#donationsBody');

    // Two calls rather than one composite endpoint: the totals are computed
    // from completed donations only, while the list shows pending ones too —
    // a staff member needs to see an abandoned checkout, a published figure
    // must not include it.
    Promise.all([
      get('/admin/donations/totals'),
      get('/admin/donations')
    ]).then(function (parts) {
      var totals = parts[0].data || { total: 0, count: 0, monthlyTotal: 0, monthlyCount: 0 };
      var page = parts[1].data || { items: [] };

      var html = '<div class="tiles">'
        + tile(rands(totals.total), 'received in total', totals.count + ' completed gifts')
        + tile(rands(totals.monthlyTotal), 'from monthly givers', totals.monthlyCount + ' recurring')
        + '</div>';

      html += '<h2 class="d4" style="margin:34px 0 16px">Recent gifts</h2>';

      html += page.items.length === 0
        ? emptyState('No donations yet', 'Gifts appear here as soon as somebody starts a donation.')
        : page.items.map(function (donation) {
            var settled = donation.status === 'Complete';

            return '<div class="datarow"><div class="grow">'
              + '<h3>' + escapeHtml(donation.displayName)
              + (donation.isOrganisation ? ' <span class="pill pill--neutral">organisation</span>' : '')
              + '</h3>'
              + '<p class="quiet" style="margin:4px 0 0">' + escapeHtml(donation.email)
              + ' · ref ' + escapeHtml(donation.gatewayReference) + '</p>'
              + '<p class="dim" style="margin:4px 0 0;font-size:12px">'
              + escapeHtml(donation.receivedAt ? dateTime(donation.receivedAt) : 'not yet received')
              + (donation.wantsSection18A
                ? (donation.section18AIssuedAt ? ' · S18A issued' : ' · S18A pending')
                : '')
              + '</p></div>'
              + '<div style="text-align:right">'
              + '<b class="num" style="font-family:var(--display);font-size:24px;font-weight:600">'
              + rands(donation.amount) + '</b>'
              + '<p style="margin:2px 0 0"><span class="pill ' + (settled ? 'pill--ok' : 'pill--warn') + '">'
              + escapeHtml(donation.frequency) + ' · ' + escapeHtml(donation.status) + '</span></p>'
              + '</div></div>';
          }).join('');

      target.innerHTML = html;
    }).catch(function (error) {
      target.innerHTML = errorState(error);
    });
  };

  /* ---------------------------------------------------------- safeguarding */

  loaders.safeguarding = function () {
    var target = $('#safeguardingBody');

    get('/safeguarding/concerns').then(function (result) {
      var concerns = result.data || [];

      if (concerns.length === 0) {
        target.innerHTML = emptyState('No open concerns', 'Flagged concerns from mentor check-ins appear here.');

        return;
      }

      target.innerHTML = concerns.map(function (concern) {
        // Already an array from the API. The prototype stored this column as a
        // JSON string and parsed it here, which meant a malformed row silently
        // rendered a concern with no support areas on it.
        var areas = concern.supportAreas || [];

        return '<div class="datarow datarow--concern"><div class="grow">'
          + '<div class="row" style="gap:8px;margin-bottom:8px">'
          + '<span class="pill pill--danger">FLAGGED CONCERN</span>'
          + '<span class="pill pill--warn">' + escapeHtml(concern.wellbeing) + '</span>'
          + (concern.escalatedAt
            ? '<span class="pill pill--neutral">routed ' + escapeHtml(shortDate(concern.escalatedAt)) + '</span>'
            : '')
          + '</div>'
          // The learner's reference code, not their name. Even here, in the
          // one queue where the reader is allowed to know, the list shows the
          // pseudonym.
          + '<h3>Learner ' + escapeHtml(concern.referenceCode) + ' · Grade ' + escapeHtml(concern.grade)
          + (concern.suburb ? ' · ' + escapeHtml(concern.suburb) : '') + '</h3>'
          + '<p class="quiet" style="margin:4px 0 8px">Raised by ' + escapeHtml(concern.mentorName)
          + ' on ' + escapeHtml(shortDate(concern.occurredOn)) + '</p>'
          + (areas.length
            ? '<div class="chipset" style="gap:6px;margin-bottom:10px">'
              + areas.map(function (a) {
                return '<span class="chip chip--clay">' + escapeHtml(a.replace(/_/g, ' ')) + '</span>';
              }).join('')
              + '</div>'
            : '')
          + '<p style="margin:0;max-width:78ch;font-size:14px;line-height:1.7">'
          + escapeHtml(concern.notes || '') + '</p>'
          + '</div>'
          + '<div style="min-width:150px">'
          + '<button class="btn btn--ghost btn--s" data-close-concern="' + escapeHtml(concern.checkInId) + '">'
          + 'Record outcome</button></div>'
          + '</div>';
      }).join('');

      $$('[data-close-concern]', target).forEach(function (button) {
        button.addEventListener('click', function () { closeConcern(button); });
      });
    }).catch(function (error) {
      target.innerHTML = errorState(error);
    });
  };

  /**
   * Closes a concern with a written outcome.
   *
   * The outcome is required and is free text, because the part that matters if
   * this record is ever needed — "spoke to the grandmother, referred to the
   * clinic social worker, reviewing in two weeks" — does not fit a dropdown.
   * The concern row is updated rather than deleted: closing it takes it out of
   * the queue and nothing else.
   */
  function closeConcern(button) {
    var outcome = window.prompt(
      'What was done about this concern?\n\n'
      + 'This is kept as the safeguarding record. The concern is not deleted.'
    );

    if (outcome === null) { return; }

    if (outcome.trim().length < 10) {
      notify('warn', 'Not closed. ', 'Please describe what was done, in a sentence or more.');

      return;
    }

    busy(button, true);

    post('/safeguarding/concerns/' + encodeURIComponent(button.dataset.closeConcern) + '/close',
      { outcome: outcome.trim() })
      .then(function () {
        notify('ok', 'Outcome recorded. ', 'The concern has been taken out of the queue.');
        loaders.safeguarding();
      })
      .catch(function (error) {
        busy(button, false);
        notify(error.status === 409 ? 'warn' : 'error', 'Not closed. ', error.message);
      });
  }

  /* ----------------------------------------------------------------- audit */

  loaders.audit = function () {
    var target = $('#auditBody');

    get('/admin/audit').then(function (result) {
      var page = result.data || { items: [], total: 0 };

      if (page.items.length === 0) {
        target.innerHTML = emptyState('Nothing logged yet', 'Changes appear here as they are made.');

        return;
      }

      target.innerHTML = '<p class="quiet" style="margin:0 0 14px">'
        + page.total + ' entries · showing the most recent ' + page.items.length + '</p>'
        + page.items.map(function (entry) {
          return '<div class="datarow"><div class="grow">'
            + '<h3>' + escapeHtml(entry.action) + '</h3>'
            + '<p class="quiet" style="margin:4px 0 0">'
            + escapeHtml(entry.entityName)
            + (entry.entityId ? ' · ' + escapeHtml(entry.entityId) : '')
            + '</p>'
            + '<p class="dim" style="margin:4px 0 0;font-size:12px">'
            + escapeHtml(entry.actorEmail || 'system') + ' · ' + escapeHtml(dateTime(entry.occurredAt))
            + (entry.ipAddress ? ' · ' + escapeHtml(entry.ipAddress) : '')
            + '</p></div></div>';
        }).join('');
    }).catch(function (error) {
      target.innerHTML = errorState(error);
    });
  };

  /* ------------------------------------------------------------ session restore */

  (function restore() {
    var access, user;

    try {
      access = sessionStorage.getItem(STORE.access);
      user = sessionStorage.getItem(STORE.user);
    } catch (e) {
      return; // storage unavailable; the sign-in screen stands
    }

    if (!access || !user) { return; }

    session.access = access;

    try {
      session.user = JSON.parse(user);
    } catch (e) {
      return;
    }

    // Confirm the token is still good before showing the console. Doing this
    // rather than trusting the stored token means a suspended account does not
    // get a console full of empty panels and confusing errors.
    window.TB.get('/auth/me', access).then(function (result) {
      startSession({ accessToken: access, user: result.data });
    }).catch(function () {
      // Expired access token: try the refresh once. No body — the refresh
      // token is in an HttpOnly cookie the browser attaches by itself, which
      // is why this console never had to hold one.
      window.TB.post('/auth/refresh', {})
        .then(function (result) { startSession(result.data); })
        .catch(function () { signOut(null); });
    });
  }());
}());
