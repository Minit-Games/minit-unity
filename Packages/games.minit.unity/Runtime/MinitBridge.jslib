mergeInto(LibraryManager.library, {
  MinitReportResult: function (score, flavorTextPtr, delay) {
    var opts = {};
    var flavorText = flavorTextPtr ? UTF8ToString(flavorTextPtr) : "";
    if (flavorText) opts.flavorText = flavorText;
    if (delay > 0) opts.delay = delay;
    if (window.minit && window.minit.reportResult) window.minit.reportResult(score, opts);
    else console.log("[minit] reportResult", score, opts);
  },
  MinitLoadingDone: function () {
    if (window.minit && window.minit.loadingDone) window.minit.loadingDone();
  },
  MinitGetConfigValue: function (keyPtr, defaultPtr) {
    var key = UTF8ToString(keyPtr);
    var def = defaultPtr ? UTF8ToString(defaultPtr) : "";
    var val = def;
    if (key !== "userData") { // reserved key — mirrors @minit-games/sdk
      var params = new URLSearchParams(window.location.search);
      if (params.has(key)) val = params.get(key);
    }
    var size = lengthBytesUTF8(val) + 1;
    var buffer = _malloc(size);
    stringToUTF8(val, buffer, size);
    return buffer; // caller frees via MinitFreeBuffer
  },
  MinitFreeBuffer: function (ptr) {
    _free(ptr);
  }
});
