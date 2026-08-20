mergeInto(LibraryManager.library, {
  GymChaosDocumentHasFocus: function () {
    if (typeof document === 'undefined' || typeof document.hasFocus !== 'function') {
      return 0;
    }

    return document.hasFocus() ? 1 : 0;
  }
});
