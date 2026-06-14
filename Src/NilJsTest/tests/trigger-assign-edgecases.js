// trigger-assign-edgecases.js
// Try a variety of Object.assign inputs to exercise edge cases
var summaries = [];
try { Object.assign({}, null); summaries.push('assign_null_ok'); } catch(e) { throw e; }
try { Object.assign({}, undefined); summaries.push('assign_undefined_ok'); } catch(e) { throw e; }
try { Object.assign({}, 123); summaries.push('assign_primitive_ok'); } catch(e) { throw e; }
try {
    var src = { get x() { throw new Error('getter-throw'); } };
    try { Object.assign({}, src); summaries.push('assign_getter_throw_not_crash'); } catch(e) { throw e; }
} catch(e) { throw e; }
// Host object emulation: a proxy with exotic behavior
try {
    var proxy = new Proxy({a:1}, { get: function(t,k){ if (k === 'b') throw new Error('proxy-get'); return t[k]; } });
    try { Object.assign({}, proxy); summaries.push('assign_proxy_ok'); } catch(e) { throw e; }
} catch(e) { throw e; }

// Print summary (if runner captures stdout)
try { if (typeof console !== 'undefined' && console.log) console.log('trigger-assign-edgecases done: ' + summaries.join(',')); } catch(e) {}
