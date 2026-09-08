mergeInto(LibraryManager.library, {
  GymChaosDocumentHasFocus: function () {
    if (typeof document === 'undefined' || typeof document.hasFocus !== 'function') {
      return 0;
    }

    return document.hasFocus() ? 1 : 0;
  },

  GymChaosInstallPointerLock: function () {
    if (typeof document === 'undefined') {
      return;
    }

    var canvas = (typeof Module !== 'undefined' && Module.canvas) ||
      document.querySelector('canvas');
    if (!canvas || canvas.__gymChaosPointerLockInstalled) {
      return;
    }

    canvas.__gymChaosPointerLockInstalled = true;
    canvas.addEventListener('mousedown', function () {
      if (!canvas.requestPointerLock || document.pointerLockElement === canvas) {
        return;
      }

      try {
        var request = canvas.requestPointerLock();
        if (request && typeof request.catch === 'function') {
          request.catch(function () {});
        }
      } catch (error) {
        // Browsers may reject pointer lock while the tab is unfocused. The
        // next click retries it, and the rejection must stay non-fatal.
      }
    }, false);
  },

  GymChaosExitPointerLock: function () {
    if (typeof document !== 'undefined' && document.exitPointerLock) {
      document.exitPointerLock();
    }
  },

  GymChaosIsPointerLocked: function () {
    if (typeof document === 'undefined') {
      return 0;
    }

    var canvas = (typeof Module !== 'undefined' && Module.canvas) ||
      document.querySelector('canvas');
    return canvas && document.pointerLockElement === canvas ? 1 : 0;
  },

  GymChaosOpenSoundCloudSite: function (urlPtr) {
    if (typeof window === 'undefined') {
      return 0;
    }

    var url = UTF8ToString(urlPtr);
    if (!url) {
      return 0;
    }
    var popup = window.open(url, 'gymchaos-soundcloud-library');
    if (!popup) {
      return 0;
    }
    try {
      popup.opener = null;
      popup.focus();
    } catch (error) {}
    return 1;
  },

  GymChaosSoundCloudWidgetLoad: function (playlistUrlPtr, objectNamePtr, volume) {
    if (typeof window === 'undefined' || typeof document === 'undefined') {
      return 0;
    }

    var playlistUrl = UTF8ToString(playlistUrlPtr);
    var objectName = UTF8ToString(objectNamePtr);
    if (!playlistUrl || !objectName) {
      return 0;
    }

    window.__gymChaosSoundCloudVolume = Math.max(
      0, Math.min(100, volume | 0));
    window.__gymChaosSoundCloudObject = objectName;

    var send = function (method, value) {
      if (typeof SendMessage === 'function' &&
          window.__gymChaosSoundCloudObject) {
        SendMessage(
          window.__gymChaosSoundCloudObject, method, value || '');
      }
    };

    var iframe = document.getElementById('gymchaos-soundcloud-widget');
    if (!iframe) {
      iframe = document.createElement('iframe');
      iframe.id = 'gymchaos-soundcloud-widget';
      iframe.title = 'SoundCloud playlist audio';
      iframe.allow = 'autoplay';
      iframe.setAttribute('aria-hidden', 'true');
      iframe.style.position = 'fixed';
      iframe.style.left = '-10000px';
      iframe.style.bottom = '0';
      iframe.style.width = '1px';
      iframe.style.height = '1px';
      iframe.style.opacity = '0';
      iframe.style.pointerEvents = 'none';
      iframe.src = 'https://w.soundcloud.com/player/?url=' +
        encodeURIComponent(playlistUrl) +
        '&auto_play=false&show_artwork=false&show_comments=false' +
        '&show_user=false&show_reposts=false&visual=false';
      document.body.appendChild(iframe);
    }

    var connectWidget = function () {
      if (!window.SC || !window.SC.Widget) {
        send('HandleSoundCloudWidgetError', 'widget-api-unavailable');
        return;
      }

      var widget = window.__gymChaosSoundCloudWidget ||
        window.SC.Widget(iframe);
      window.__gymChaosSoundCloudWidget = widget;
      if (!window.__gymChaosSoundCloudWidgetBound) {
        window.__gymChaosSoundCloudWidgetBound = true;
        widget.bind(window.SC.Widget.Events.READY, function () {
          widget.setVolume(window.__gymChaosSoundCloudVolume || 0);
          send('HandleSoundCloudWidgetReady', '');
        });
        widget.bind(window.SC.Widget.Events.PLAY, function () {
          send('HandleSoundCloudWidgetPlay', '');
        });
        widget.bind(window.SC.Widget.Events.PAUSE, function () {
          send('HandleSoundCloudWidgetPause', '');
        });
        widget.bind(window.SC.Widget.Events.FINISH, function () {
          send('HandleSoundCloudWidgetFinish', '');
        });
        widget.bind(window.SC.Widget.Events.ERROR, function () {
          send('HandleSoundCloudWidgetError', 'widget-playback-error');
        });
      }

      if (window.__gymChaosSoundCloudLoadedOnce) {
        widget.load(playlistUrl, {
          auto_play: true,
          show_artwork: false,
          show_comments: false,
          show_user: false,
          show_reposts: false,
          visual: false,
          callback: function () {
            widget.setVolume(window.__gymChaosSoundCloudVolume || 0);
          }
        });
      } else {
        window.__gymChaosSoundCloudLoadedOnce = true;
      }
    };

    if (window.SC && window.SC.Widget) {
      connectWidget();
      return 1;
    }

    var script = document.getElementById('gymchaos-soundcloud-widget-api');
    if (!script) {
      script = document.createElement('script');
      script.id = 'gymchaos-soundcloud-widget-api';
      script.src = 'https://w.soundcloud.com/player/api.js';
      script.async = true;
      script.onload = function () {
        script.setAttribute('data-loaded', 'true');
        connectWidget();
      };
      script.onerror = function () {
        send('HandleSoundCloudWidgetError', 'widget-api-load-error');
      };
      document.head.appendChild(script);
    } else if (window.SC && window.SC.Widget) {
      connectWidget();
    } else {
      script.addEventListener('load', connectWidget, { once: true });
    }
    return 1;
  },

  GymChaosSoundCloudWidgetPlay: function () {
    var widget = typeof window !== 'undefined' &&
      window.__gymChaosSoundCloudWidget;
    if (widget) {
      widget.play();
    }
  },

  GymChaosSoundCloudWidgetPause: function () {
    var widget = typeof window !== 'undefined' &&
      window.__gymChaosSoundCloudWidget;
    if (widget) {
      widget.pause();
    }
  },

  GymChaosSoundCloudWidgetNext: function () {
    var widget = typeof window !== 'undefined' &&
      window.__gymChaosSoundCloudWidget;
    if (widget) {
      widget.next();
      widget.play();
    }
  },

  GymChaosSoundCloudWidgetContinue: function () {
    var widget = typeof window !== 'undefined' &&
      window.__gymChaosSoundCloudWidget;
    if (!widget) {
      return;
    }
    widget.getSounds(function (sounds) {
      widget.getCurrentSoundIndex(function (index) {
        if (sounds && sounds.length && index >= sounds.length - 1) {
          widget.skip(0);
        } else {
          widget.next();
        }
        widget.play();
      });
    });
  },

  GymChaosSoundCloudWidgetSetVolume: function (volume) {
    if (typeof window === 'undefined') {
      return;
    }
    window.__gymChaosSoundCloudVolume = Math.max(
      0, Math.min(100, volume | 0));
    var widget = window.__gymChaosSoundCloudWidget;
    if (widget) {
      widget.setVolume(window.__gymChaosSoundCloudVolume);
    }
  },

  GymChaosSoundCloudWidgetDestroy: function () {
    if (typeof window === 'undefined' || typeof document === 'undefined') {
      return;
    }
    var widget = window.__gymChaosSoundCloudWidget;
    if (widget) {
      widget.pause();
    }
    var iframe = document.getElementById('gymchaos-soundcloud-widget');
    if (iframe && iframe.parentNode) {
      iframe.parentNode.removeChild(iframe);
    }
    window.__gymChaosSoundCloudWidget = null;
    window.__gymChaosSoundCloudWidgetBound = false;
    window.__gymChaosSoundCloudLoadedOnce = false;
  }
});
