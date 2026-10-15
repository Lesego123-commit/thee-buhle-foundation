/* =============================================================================
   Site behaviour: navigation, toggles, and the four public forms.
   Thee Buhle Foundation · INSY7315 Task 2

   Progressive enhancement throughout. The pages are server-rendered PHP, so the
   content, the navigation and the copy all work with JavaScript switched off or
   still downloading on a slow connection — which is not a hypothetical for
   learners on prepaid data in the Vaal. This file adds the interactive layer on
   top: the drawer, the accordions, the donation calculator, and submitting the
   forms through the API without a page reload.

   The form handling is the part worth reading. Every submission:
     * disables the button and shows it as busy, so nobody double-submits;
     * clears previous field errors before trying again;
     * binds the API's 422 `fields` object onto the inputs with aria-invalid and
       a visible message under each one;
     * moves focus to the first problem, or to the success notice, so a screen
       reader and a keyboard user both learn what happened.
   ============================================================================= */

(function () {
  'use strict';

  var $  = function (selector, root) { return (root || document).querySelector(selector); };
  var $$ = function (selector, root) {
    return Array.prototype.slice.call((root || document).querySelectorAll(selector));
  };

  /* ---------------------------------------------------- mobile navigation */

  var burger = $('#burger');
  var drawer = $('#drawer');

  if (burger && drawer) {
    burger.addEventListener('click', function () {
      var open = !drawer.classList.contains('is-open');

      drawer.classList.toggle('is-open', open);
      burger.setAttribute('aria-expanded', String(open));

      if (open) {
        var first = drawer.querySelector('a, button');
        if (first) { first.focus(); }
      }
    });

    // Escape closes the drawer and returns focus to the button that opened it,
    // so a keyboard user is never left adrift inside a closed menu.
    document.addEventListener('keydown', function (event) {
      if (event.key === 'Escape' && drawer.classList.contains('is-open')) {
        drawer.classList.remove('is-open');
        burger.setAttribute('aria-expanded', 'false');
        burger.focus();
      }
    });
  }

  /* --------------------------------------------------------- toggle groups */

  document.addEventListener('click', function (event) {
    var button = event.target.closest('[aria-pressed]');

    if (!button || button.closest('[data-no-toggle]')) { return; }

    if (button.closest('[data-multiselect]')) {
      button.setAttribute('aria-pressed', button.getAttribute('aria-pressed') === 'true' ? 'false' : 'true');

      return;
    }

    var group = button.closest('[data-singleselect], [data-amountset]');

    if (!group) { return; }

    $$('[aria-pressed]', group).forEach(function (sibling) {
      sibling.setAttribute('aria-pressed', 'false');
    });

    button.setAttribute('aria-pressed', 'true');

    if (button.dataset.filter !== undefined) { applyFilter(group, button); }
    if (button.dataset.amt) { updateDonationReadout(); }
  });

  /* ------------------------------------------------------------ accordions */

  $$('.acc > button').forEach(function (button) {
    button.addEventListener('click', function () {
      var accordion = button.parentElement;
      var open = !accordion.classList.contains('is-open');

      accordion.classList.toggle('is-open', open);
      button.setAttribute('aria-expanded', String(open));

      var indicator = button.lastElementChild;
      if (indicator) { indicator.textContent = open ? '−' : '+'; }
    });
  });

  /* -------------------------------------------------------- client filters */

  function applyFilter(group, button) {
    var target = group.dataset.filterTarget;

    if (!target) { return; }

    var value = button.dataset.filter;

    $$(target + ' [data-category]').forEach(function (item) {
      var show = value === 'all' || item.dataset.category === value;
      item.hidden = !show;
    });

    var visible = $$(target + ' [data-category]').filter(function (item) { return !item.hidden; }).length;
    var counter = $(group.dataset.filterCount || '#filterCount');

    if (counter) {
      counter.textContent = visible + (visible === 1 ? ' item' : ' items');
    }
  }

  /* ------------------------------------------------------ donation widget */

  var IMPACT_COPY = null;

  function updateDonationReadout() {
    var widget = $('[data-widget="donation"]');

    if (!widget) { return; }

    var frequency = (widget.querySelector('[data-freq][aria-pressed="true"]') || {}).dataset;
    var amountBtn = widget.querySelector('[data-amt][aria-pressed="true"]');
    var other     = $('#otherAmount', widget);
    var readout   = $('[data-readout] p', widget);

    var freq = frequency ? frequency.freq : 'monthly';
    var amount = other && other.value ? parseFloat(other.value) : (amountBtn ? parseFloat(amountBtn.dataset.amt) : 0);

    // The per-amount wording comes from the API (GET /donations/options), so the
    // website, the receipt email and the app all describe a gift identically.
    var copy = IMPACT_COPY && IMPACT_COPY[freq] ? IMPACT_COPY[freq][String(amount)] : null;

    if (readout) {
      readout.innerHTML = '<b>' + formatRand(amount) + ' ' + wordFor(freq) + '</b> '
        + (copy || 'goes straight to programme delivery.');
    }

    // The per-month sub-labels change with frequency, because R250 "once" and
    // R250 "per month" are very different gifts.
    $$('[data-amountset] .amt span', widget).forEach(function (span) {
      span.textContent = freq === 'once' ? 'once-off' : 'per month';
    });
  }

  function wordFor(frequency) {
    return frequency === 'once' ? 'once-off' : 'a month';
  }

  function formatRand(amount) {
    if (!amount || isNaN(amount)) { return 'R0'; }

    return 'R' + Math.round(amount).toLocaleString('en-ZA').replace(/,/g, ' ');
  }

  if ($('[data-widget="donation"]') && window.TB) {
    window.TB.donationOptions().then(function (result) {
      IMPACT_COPY = result.data.copy;
      updateDonationReadout();
    }).catch(function () {
      // The copy is a nicety; the form still works without it.
      updateDonationReadout();
    });

    var other = $('#otherAmount');

    if (other) {
      other.addEventListener('input', function () {
        if (other.value) {
          $$('[data-amountset] [aria-pressed]').forEach(function (button) {
            button.setAttribute('aria-pressed', 'false');
          });
        }

        updateDonationReadout();
      });
    }

    $$('[data-freq]').forEach(function (button) {
      button.addEventListener('click', function () {
        $$('[data-freq]').forEach(function (sibling) { sibling.setAttribute('aria-pressed', 'false'); });
        button.setAttribute('aria-pressed', 'true');

        // Sponsorship is per-learner, so the learner picker only applies there.
        var picker = $('#sponsorPicker');
        if (picker) { picker.hidden = button.dataset.freq !== 'sponsor'; }

        updateDonationReadout();
      });
    });
  }

  /* ----------------------------------------------------------- form plumbing */

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

  /** Clears every field error and the summary notice before a fresh attempt. */
  function clearErrors(form) {
    $$('[aria-invalid="true"]', form).forEach(function (input) {
      input.removeAttribute('aria-invalid');
      input.removeAttribute('aria-describedby');
    });

    $$('.field-error', form).forEach(function (element) { element.textContent = ''; });

    var notice = $('[data-notice]', form);

    if (notice) {
      notice.hidden = true;
      notice.className = 'notice';
      notice.innerHTML = '';
    }
  }

  /**
   * Binds the API's per-field messages onto the inputs.
   *
   * Each message gets an id and the input points at it with aria-describedby, so
   * a screen reader reads the problem when focus lands on the field rather than
   * leaving a silently red box.
   */
  function showFieldErrors(form, fields) {
    var invalid = [];

    Object.keys(fields).forEach(function (name) {
      var input = form.querySelector('[name="' + name + '"]');

      if (!input) { return; }

      input.setAttribute('aria-invalid', 'true');

      var holder = form.querySelector('[data-error-for="' + name + '"]');

      if (holder) {
        var id = 'err-' + name;
        holder.id = id;
        holder.textContent = fields[name];
        input.setAttribute('aria-describedby', id);
      }

      invalid.push(input);
    });

    // Focus the field nearest the top of the form, not the first key the API
    // happened to return. A validation dictionary has no order a reader can
    // see, and being thrown to the fourth field while the first is also red
    // reads as the page having lost its place.
    if (invalid.length > 0) {
      invalid.sort(function (a, b) {
        var position = a.compareDocumentPosition(b);

        return (position & Node.DOCUMENT_POSITION_FOLLOWING) ? -1 : 1;
      });

      invalid[0].focus();
    }
  }

  function showNotice(form, variant, title, message) {
    var notice = $('[data-notice]', form);

    if (!notice) { return; }

    notice.className = 'notice notice--' + variant;
    notice.hidden = false;
    // Not every endpoint answers with a sentence — a donation answers with the
    // gateway's field set, and nothing else. An absent message is rendered as
    // nothing rather than as the word "undefined".
    notice.innerHTML = '<span aria-hidden="true">' + (variant === 'ok' ? '✓' : '⚠') + '</span>'
      + '<div><b>' + escapeHtml(title) + '</b>' + escapeHtml(message == null ? '' : message) + '</div>';

    // role="status" on the element means this is announced without stealing
    // focus, which is right for a success; for a failure the focus has already
    // gone to the offending field.
    notice.setAttribute('role', 'status');
    notice.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  }

  function escapeHtml(value) {
    return String(value)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  }

  function setBusy(form, busy) {
    var button = $('[type="submit"]', form);

    if (!button) { return; }

    button.dataset.busy = busy ? 'true' : 'false';
    button.disabled = busy;
  }

  /** Reads a form into a plain object, honouring the toggle groups. */
  function collect(form) {
    var payload = {};

    $$('input, textarea, select', form).forEach(function (input) {
      if (!input.name) { return; }

      if (input.type === 'checkbox') {
        payload[input.name] = input.checked;
      } else if (input.type === 'radio') {
        if (input.checked) { payload[input.name] = input.value; }
      } else if (input.value !== '' || input.required) {
        // A required field is sent even when it is empty, so the API can say
        // which one is missing. Dropping it instead meant the request failed to
        // deserialise at all, and the reply was one sentence about the whole
        // submission rather than a message on each field the person still has
        // to fill in.
        //
        // Empty optional fields are still left out: sending "" for a phone
        // number would store an empty string where the column means "not
        // given".
        payload[input.name] = input.value;
      }
    });

    // Toggle-button groups are not form controls, so they are collected by hand.
    $$('[data-collect]', form).forEach(function (group) {
      var name = group.dataset.collect;

      if (group.hasAttribute('data-multiselect')) {
        payload[name] = $$('[aria-pressed="true"]', group).map(function (button) {
          return button.dataset.value || button.textContent.trim();
        });
      } else {
        var selected = $('[aria-pressed="true"]', group);
        if (selected) { payload[name] = selected.dataset.value; }
      }
    });

    return payload;
  }

  /**
   * Wires one form to one API call.
   *
   * @param {string}   selector
   * @param {function} submit   receives the payload, returns a promise
   * @param {function} onSuccess
   */
  function wireForm(selector, submit, onSuccess) {
    var form = $(selector);

    if (!form) { return; }

    form.addEventListener('submit', function (event) {
      event.preventDefault();
      clearErrors(form);
      setBusy(form, true);

      submit(collect(form)).then(function (result) {
        setBusy(form, false);
        onSuccess(form, result.data);
      }).catch(function (error) {
        setBusy(form, false);

        if (isValidationFailure(error)) {
          // The heading and the message are different sentences: the client's
          // summary for a validation failure is already "Please check the
          // highlighted fields", and printing it as both read as a stutter.
          showNotice(form, 'error', 'That did not send. ', error.message);
          showFieldErrors(form, error.fields);

          return;
        }

        showNotice(
          form,
          'error',
          error.isNetwork() ? 'We could not reach the server. ' : 'That did not work. ',
          error.message
        );
      });
    });
  }

  /* ------------------------------------------------------------- the forms */

  if (window.TB) {
    wireForm('#activateForm', function (payload) {
      // Checked here because the API only ever sees one copy of the password.
      if (payload.newPassword !== payload.confirmPassword) {
        return Promise.reject({
          status: 400,
          message: 'The two passwords are different.',
          fields: { confirmPassword: 'This does not match the password above.' },
          isNetwork: function () { return false; }
        });
      }

      return window.TB.publicPost('/auth/redeem-code', {
        email: payload.email,
        code: payload.code,
        newPassword: payload.newPassword
      });
    }, function (form) {
      form.reset();
      showNotice(form, 'ok', 'Your password is set. ',
        'Sign in to the Thee Buhle app with your email address and new password. Staff: sign in at /admin.');
    });

    wireForm('#contactForm', function (payload) {
      return window.TB.contact(payload);
    }, function (form, data) {
      form.reset();
      showNotice(form, 'ok', 'Thank you. ', data.message);
    });

    wireForm('#volunteerForm', function (payload) {
      // The availability grid is a set of toggle buttons rather than checkboxes,
      // so it is assembled into the JSON shape the API validates.
      payload.availability = collectAvailability();

      return window.TB.volunteer(payload);
    }, function (form, data) {
      showNotice(form, 'ok', 'Application received. ', data.message);
      $$('[type="submit"], .input, .tog', form).forEach(function (element) {
        element.disabled = true;
      });
    });

    wireForm('#donateForm', function (payload) {
      payload.amount = selectedAmount();
      payload.frequency = selectedFrequency();

      return window.TB.donate(payload);
    }, function (form, data) {
      // The browser now posts the signed field set to the gateway, on the
      // gateway's own domain. No card details are collected by this site.
      showNotice(
        form,
        'ok',
        'Taking you to our secure payment provider. ',
        'Your card details are entered on their site and never reach ours.'
      );

      handOffToGateway(data);
    });

    $$('[data-newsletter]').forEach(function (form) {
      form.addEventListener('submit', function (event) {
        event.preventDefault();
        clearErrors(form);
        setBusy(form, true);

        var email = ($('input[name="email"]', form) || {}).value || '';

        window.TB.subscribe(email).then(function (result) {
          setBusy(form, false);
          form.reset();
          // The API's sentence already opens with "You are on the list", so the
          // heading says something else rather than saying it twice.
          showNotice(form, 'ok', 'Subscribed. ', result.data.message);
        }).catch(function (error) {
          setBusy(form, false);
          showNotice(form, 'error', 'That did not work. ', error.message);
        });
      });
    });

    $$('[data-event-form]').forEach(function (form) {
      form.addEventListener('submit', function (event) {
        event.preventDefault();
        clearErrors(form);
        setBusy(form, true);

        window.TB.registerForEvent(form.dataset.eventForm, collect(form)).then(function (result) {
          setBusy(form, false);
          // Built from the registration rather than read off a `message` the
          // API does not send, so the confirmation names the event the person
          // actually just booked.
          showNotice(
            form,
            'ok',
            'You are registered. ',
            'We have you down for ' + result.data.eventTitle + '. Check your email for the details.'
          );

          var counter = $('[data-places-left="' + form.dataset.eventForm + '"]');

          if (counter && result.data.placesLeft !== null) {
            counter.textContent = result.data.placesLeft + ' places left';
          }
        }).catch(function (error) {
          setBusy(form, false);

          if (isValidationFailure(error)) {
            showFieldErrors(form, error.fields);
          }

          showNotice(form, error.status === 409 ? 'warn' : 'error', 'Not registered. ', error.message);
        });
      });
    });
  }

  function selectedAmount() {
    var other = $('#otherAmount');

    if (other && other.value) { return parseFloat(other.value); }

    var selected = $('[data-amountset] [aria-pressed="true"]');

    return selected ? parseFloat(selected.dataset.amt) : 0;
  }

  function selectedFrequency() {
    var selected = $('[data-freq][aria-pressed="true"]');

    return selected ? selected.dataset.freq : 'monthly';
  }

  function collectAvailability() {
    var availability = {};

    $$('[data-availability] [aria-pressed="true"]').forEach(function (cell) {
      var slot = cell.dataset.slot;
      var day  = cell.dataset.day;

      if (!availability[slot]) { availability[slot] = []; }

      availability[slot].push(day);
    });

    return availability;
  }

  /**
   * Posts the gateway's signed field set from a form built at runtime.
   *
   * Built here rather than rendered into the page because the signature is
   * bound to one specific donation, and a stale signed form left in the DOM
   * would be a set of fields somebody could resubmit.
   */
  function handOffToGateway(data) {
    var form = document.createElement('form');

    form.method = 'POST';
    form.action = data.gatewayUrl;
    form.style.display = 'none';

    Object.keys(data.fields).forEach(function (name) {
      var input = document.createElement('input');

      input.type = 'hidden';
      input.name = name;
      input.value = data.fields[name];

      form.appendChild(input);
    });

    document.body.appendChild(form);

    // A short pause so the success notice is readable before the redirect.
    window.setTimeout(function () { form.submit(); }, 900);
  }

  /* ------------------------------------------------------- initial render */

  updateDonationReadout();

  // Live counters that would be stale if baked into the server-rendered HTML.
  $$('[data-live-impact]').forEach(function (element) {
    if (!window.TB) { return; }

    window.TB.impact().then(function (result) {
      var metric = result.data.headlines.filter(function (headline) {
        return headline.metric_key === element.dataset.liveImpact;
      })[0];

      if (metric) { element.textContent = metric.value_text; }
    }).catch(function () {
      // The server-rendered value stays. A failed refresh should never blank a
      // number that is already correct on screen.
    });
  });
}());
