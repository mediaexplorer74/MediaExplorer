// trigger-assign.js
// Repeatedly executes code similar to the Object.assign polyfill to exercise NiL.JS
// and try to provoke the internal IndexOutOfRange/JSException paths.
for (var i = 0; i < 10; i++) {
    try {
        (function(){ if (!Object.assign) Object.assign = function(t){for(var i=1;i<arguments.length;i++){var s=arguments[i];if(s)for(var k in s)if(Object.prototype.hasOwnProperty.call(s,k))t[k]=s[k]}return t}; })();
    } catch (e) {
        // swallow - we want SafeEval to record
        throw e;
    }
}
