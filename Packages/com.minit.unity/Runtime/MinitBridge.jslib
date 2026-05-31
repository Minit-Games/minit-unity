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
    var cfg = window.minit && window.minit.dropConfig;
    var val = (cfg && cfg[key] != null) ? cfg[key] : def;
    var size = lengthBytesUTF8(val) + 1;
    var buffer = _malloc(size);
    stringToUTF8(val, buffer, size);
    return buffer;
  }
});
