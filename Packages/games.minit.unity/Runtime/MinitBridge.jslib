mergeInto(LibraryManager.library, {
  MinitSendResult: function (result, hasFlavor, flavorTextPtr, delay, hasUserData, userDataPtr) {
    var opts = {};
    if (hasFlavor) opts.flavorText = UTF8ToString(flavorTextPtr);
    if (delay > 0) opts.delay = delay;
    if (hasUserData) opts.userData = { value: UTF8ToString(userDataPtr) }; // "" is a real write that clears the slot
    if (window.minit && window.minit.reportResult) window.minit.reportResult(result, opts);
    else console.log("[minit] reportResult", result, opts);
  },
  MinitReportResult__deps: ["MinitSendResult"],
  MinitReportResult: function (score, hasFlavor, flavorTextPtr, delay, hasUserData, userDataPtr) {
    _MinitSendResult(score, hasFlavor, flavorTextPtr, delay, hasUserData, userDataPtr);
  },
  MinitReportResultString__deps: ["MinitSendResult"],
  MinitReportResultString: function (resultPtr, hasFlavor, flavorTextPtr, delay, hasUserData, userDataPtr) {
    _MinitSendResult(resultPtr ? UTF8ToString(resultPtr) : null, hasFlavor, flavorTextPtr, delay, hasUserData, userDataPtr);
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
  MinitGetUserData: function (defaultPtr) {
    var def = defaultPtr ? UTF8ToString(defaultPtr) : "";
    var val = def;
    if (window.minit && window.minit.userData != null) {
      val = window.minit.userData; // host-injected string (including "") wins
    } else {
      var params = new URLSearchParams(window.location.search);
      if (params.has("userData")) val = params.get("userData");
    }
    var size = lengthBytesUTF8(val) + 1;
    var buffer = _malloc(size);
    stringToUTF8(val, buffer, size);
    return buffer; // caller frees via MinitFreeBuffer
  },
  MinitTryGetUserData: function () {
    var val = null;
    if (window.minit && window.minit.userData != null) {
      val = window.minit.userData;
    } else {
      var params = new URLSearchParams(window.location.search);
      if (params.has("userData")) val = params.get("userData");
    }
    if (val === null) return 0;
    var size = lengthBytesUTF8(val) + 1;
    var buffer = _malloc(size);
    stringToUTF8(val, buffer, size);
    return buffer; // caller frees via MinitFreeBuffer
  },
  MinitFreeBuffer: function (ptr) {
    _free(ptr);
  },
  MinitShouldShowTutorial: function () {
    var forced = new URLSearchParams(window.location.search).get("tutorial");
    if (forced === "1" || forced === "true") return 1;
    if (forced === "0" || forced === "false") return 0;
    return window.minit && window.minit.hasPlayedGame === true ? 0 : 1;
  },
  MinitIsViewable: function () {
    var m = window.minit;
    if (m && typeof m.isViewable === "function" && typeof m.addEventListener === "function" && typeof m.removeEventListener === "function") {
      try {
        return m.isViewable() ? 1 : 0;
      } catch (e) {}
    }
    return document.visibilityState === "visible" ? 1 : 0;
  },
  MinitOnViewableChange__deps: ["MinitIsViewable"],
  MinitOnViewableChange: function (cb) {
    var m = window.minit;
    var useHost = m && typeof m.isViewable === "function" && typeof m.addEventListener === "function" && typeof m.removeEventListener === "function";
    var last = _MinitIsViewable();
    var deliver = function (v) {
      if (v === last) return;
      last = v;
      {{{ makeDynCall('vi', 'cb') }}}(v);
    };
    if (useHost) {
      m.addEventListener("viewableChange", function (viewable) { deliver(viewable ? 1 : 0); });
    } else {
      document.addEventListener("visibilitychange", function () { deliver(document.visibilityState === "visible" ? 1 : 0); });
    }
  }
});
