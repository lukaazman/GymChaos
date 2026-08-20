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
  }
});
