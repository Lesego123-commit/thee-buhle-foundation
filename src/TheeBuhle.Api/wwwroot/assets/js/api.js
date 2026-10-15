/* =============================================================================
   The browser's API client.
   Thee Buhle Foundation · INSY7315 Task 2

   Every call the website makes to the REST API goes through this module. It is
   deliberately one file with no build step and no framework: the site is served
   from cPanel shared hosting, and a public site for an NPO should not require a
   toolchain to deploy a copy change.

   What it centralises, and why each matters:

     * One base URL, so the front end has no hard-coded host. It is read from
       this script tag's own data-api-base attribute rather than from a global
       set by an inline <script>, because the site sends
       script-src 'self' with no 'unsafe-inline' — an inline assignment is
       blocked by the browser and the fallback silently wins, which is exactly
       how every form on this site once posted to a URL that did not exist.
     * Error shape. The API answers failures as RFC 7807 problem details, so
       this module turns a 400 with a validation dictionary into per-field
       messages and anything else into one human sentence.
     * Timeouts. A request that hangs forever leaves a spinner spinning; every
       call is aborted after 15 seconds and reported as a network problem.

   Note on what is NOT here: there is no form-token dance. The PHP prototype
   required a short-lived signed token on every public write; the ASP.NET Core
   API protects those endpoints with per-address rate limiting instead, and has
   no token endpoint at all. Nothing carries a cookie either, so there is no
   CSRF surface for a token to close.
   ============================================================================= */

(function (global) {
  'use strict';

  /* The script tag that loaded this file, so the page can point it at a
     different origin without an inline script. document.currentScript is still
     the loading element for a deferred classic script, which is how both the
     website layout and the staff console load it. */
  var SCRIPT = document.currentScript;
  var CONFIGURED = SCRIPT && SCRIPT.dataset ? SCRIPT.dataset.apiBase : null;

  /* Same-origin '/api' by default, because the API and the website are the same
     ASP.NET Core application. A default that points somewhere real matters: the
     previous one pointed at the PHP prototype's '/api/v1', so when the inline
     assignment was blocked every request 404'd against a path this build has
     never served. */
  var BASE = (CONFIGURED != null && CONFIGURED !== ''
    ? CONFIGURED
    : (global.TB_API_BASE || '/api')).replace(/\/$/, '');

  var TIMEOUT_MS = 15000;

  /* ---------------------------------------------------------------- errors */

  /**
   * The one error type the callers handle.
   *
   * `fields` carries the API's per-field messages so a form can bind them
   * straight onto its inputs; `status` lets a caller distinguish "you are not
   * signed in" from "that is already registered".
   */
  function ApiError(message, status, code, fields) {
    this.name = 'ApiError';
    this.message = message;
    this.status = status || 0;
    this.code = code || 'unknown';
    this.fields = fields || {};
  }

  ApiError.prototype = Object.create(Error.prototype);
  ApiError.prototype.constructor = ApiError;

  /** True when the failure is worth a "check your connection" message. */
  ApiError.prototype.isNetwork = function () {
    return this.status === 0;
  };

  /* ------------------------------------------------------- problem details */

  /**
   * Turns an RFC 7807 problem document into the one error type callers handle.
   *
   * Two shapes arrive. A model-validation failure carries `errors`, a
   * dictionary of PascalCase property names to message lists. A business rule
   * refused by the API carries `title` and `detail` and no dictionary — that
   * detail is written for the person reading it, so it is shown as-is.
   *
   * The dictionary keys are lower-cased on the first letter because that is
   * what the form controls are named: the server validates `ConsentPopia`, the
   * input is `consentPopia`, and a message that cannot find its field is a red
   * box with no explanation in it.
   */
  function problemToError(payload, status) {
    if (!payload) {
      return new ApiError('Something went wrong. Please try again.', status, 'http_' + status);
    }

    var fields = {};

    if (payload.errors) {
      Object.keys(payload.errors).forEach(function (key) {
        var messages = payload.errors[key];
        var name = key.charAt(0).toLowerCase() + key.slice(1);

        fields[name] = Array.isArray(messages) ? messages[0] : String(messages);
      });
    }

    // A validation dictionary's own title is "One or more validation errors
    // occurred", which tells the reader nothing they can act on. The per-field
    // messages are the useful part, so the summary says where to look.
    var message = payload.detail
      || (Object.keys(fields).length
        ? 'Please check the highlighted fields.'
        : payload.title)
      || 'Something went wrong. Please try again.';

    return new ApiError(message, status, payload.title || 'http_' + status, fields);
  }

  /* --------------------------------------------------------------- request */

  function request(method, path, options) {
    options = options || {};

    var url = BASE + path;
    var headers = { Accept: 'application/json' };
    var body;

    if (options.body !== undefined) {
      headers['Content-Type'] = 'application/json';
      body = JSON.stringify(options.body);
    }

    if (options.token) {
      headers.Authorization = 'Bearer ' + options.token;
    }

    // AbortController gives the request a deadline. Without one, a stalled
    // mobile connection leaves the submit button spinning indefinitely, which
    // reads to the user as "the site is broken" rather than "try again".
    var controller = typeof AbortController === 'function' ? new AbortController() : null;
    var timer = controller
      ? global.setTimeout(function () { controller.abort(); }, options.timeout || TIMEOUT_MS)
      : null;

    return global.fetch(url, {
      method: method,
      headers: headers,
      body: body,
      signal: controller ? controller.signal : undefined,
      credentials: 'same-origin'
    }).then(function (response) {
      if (timer) { global.clearTimeout(timer); }

      if (response.status === 204) {
        return { data: null, meta: null, status: 204 };
      }

      return response.text().then(function (text) {
        var payload = null;

        if (text) {
          try {
            payload = JSON.parse(text);
          } catch (e) {
            // A non-JSON body from a JSON API means something upstream
            // intervened — a proxy error page, a PHP fatal before the kernel.
            throw new ApiError(
              'The server returned an unexpected response. Please try again in a moment.',
              response.status,
              'bad_response'
            );
          }
        }

        if (!response.ok) {
          throw problemToError(payload, response.status);
        }

        // The payload itself, not payload.data. This API answers with the
        // object, unwrapped — the {data,meta} envelope belonged to the PHP
        // prototype, and reading .data off a bare response yielded undefined
        // on every single call.
        return {
          data: payload,
          meta: payload ? payload.meta || null : null,
          status: response.status
        };
      });
    }).catch(function (e) {
      if (timer) { global.clearTimeout(timer); }

      if (e instanceof ApiError) { throw e; }

      if (e && e.name === 'AbortError') {
        throw new ApiError(
          'That took too long. Check your connection and try again.',
          0,
          'timeout'
        );
      }

      throw new ApiError(
        'We could not reach the server. Check your connection and try again.',
        0,
        'network'
      );
    });
  }

  /* -------------------------------------------------------- public writes */

  /**
   * A public write.
   *
   * Nothing more than a POST. The PHP prototype fetched a signed form token
   * first and retried once if it had gone stale; this API has no token
   * endpoint, and calling for one made every submission fail before it was
   * sent. Abuse is handled server-side by the rate limiter on these routes,
   * which is a control the client cannot be talked out of.
   *
   * Kept as a named function rather than inlined, so the call sites read the
   * same as before and a future token scheme has one place to live.
   */
  function publicPost(path, payload) {
    return request('POST', path, { body: payload });
  }

  /* ------------------------------------------------------------ public API */

  global.TB = {
    ApiError: ApiError,
    base: BASE,

    get: function (path, token) {
      return request('GET', path, { token: token });
    },

    post: function (path, payload, token) {
      return request('POST', path, { body: payload, token: token });
    },

    patch: function (path, payload, token) {
      return request('PATCH', path, { body: payload, token: token });
    },

    put: function (path, payload, token) {
      return request('PUT', path, { body: payload, token: token });
    },

    del: function (path, token) {
      return request('DELETE', path, { token: token });
    },

    publicPost: publicPost,

    /* ------------------------------------------------- named endpoints
       Named wrappers rather than raw paths at the call sites, so a route change
       is one edit here instead of a search through the page scripts. */

    contact: function (payload) { return publicPost('/public/contact', payload); },
    subscribe: function (email) { return publicPost('/public/newsletter', { email: email }); },
    volunteer: function (payload) { return publicPost('/public/volunteer', payload); },
    donate: function (payload) { return publicPost('/public/donations', payload); },

    registerForEvent: function (eventId, payload) {
      return publicPost('/public/events/' + encodeURIComponent(eventId) + '/register', payload);
    },

    donationOptions: function () { return request('GET', '/public/donations/impact-copy'); },
    sponsorCards: function () { return request('GET', '/public/sponsorships/available'); },
    impact: function () { return request('GET', '/public/impact'); },
    events: function (type) {
      return request('GET', '/public/events' + (type ? '?type=' + encodeURIComponent(type) : ''));
    },
    news: function (page, category) {
      var query = ['page=' + (page || 1)];
      if (category) { query.push('category=' + encodeURIComponent(category)); }

      return request('GET', '/public/news?' + query.join('&'));
    }
  };
}(window));
