// uwp_wrap_test.js — replicates exact UWP SafeEval wrapping
// Polyfill prefix + d3.v5 wrapped in try{}catch(e){}
// After eval, check d3 state via a tail expression

var __MapPolyfill = function(entries) { this._d = {}; this.size = 0; if (entries) for (var __mpe_i = 0; __mpe_i < entries.length; ++__mpe_i) this.set(entries[__mpe_i][0], entries[__mpe_i][1]); };
__MapPolyfill.__polyfilled = true;
__MapPolyfill.prototype.set = function(k, v) { var s = typeof k + '|' + k; if (!this._d.hasOwnProperty(s)) this.size++; this._d[s] = v; return this; };
__MapPolyfill.prototype.get = function(k) { var s = typeof k + '|' + k; return this._d.hasOwnProperty(s) ? this._d[s] : void 0; };
__MapPolyfill.prototype.has = function(k) { return this._d.hasOwnProperty(typeof k + '|' + k); };
__MapPolyfill.prototype.delete = function(k) { var s = typeof k + '|' + k; if (this._d.hasOwnProperty(s)) { delete this._d[s]; this.size--; return true; } return false; };
__MapPolyfill.prototype.clear = function() { this._d = {}; this.size = 0; };
__MapPolyfill.prototype.forEach = function(fn, thisArg) { for (var k in this._d) if (this._d.hasOwnProperty(k)) fn.call(thisArg || this, this._d[k], k, this); };
__MapPolyfill.prototype.entries = function() { var a = []; for (var k in this._d) if (this._d.hasOwnProperty(k)) { var p = k.indexOf('|'); a.push([k.substring(p + 1), this._d[k]]); } return a; };
var __SetPolyfill = function(values) { this._d = {}; this.size = 0; if (values) for (var __spe_i = 0; __spe_i < values.length; ++__spe_i) this.add(values[__spe_i]); };
__SetPolyfill.__polyfilled = true;
__SetPolyfill.prototype.add = function(v) { var s = typeof v + '|' + v; if (!this._d.hasOwnProperty(s)) this.size++; this._d[s] = v; return this; };
__SetPolyfill.prototype.has = function(v) { return this._d.hasOwnProperty(typeof v + '|' + v); };
__SetPolyfill.prototype.delete = function(v) { var s = typeof v + '|' + v; if (this._d.hasOwnProperty(s)) { delete this._d[s]; this.size--; return true; } return false; };
__SetPolyfill.prototype.clear = function() { this._d = {}; this.size = 0; };
__SetPolyfill.prototype.forEach = function(fn, thisArg) { for (var k in this._d) if (this._d.hasOwnProperty(k)) fn.call(thisArg || this, this._d[k], k, this); };
if (typeof Symbol === 'undefined') { var __id = 0; var Symbol = function(k){ return '__Symbol_' + (k||'') + '_' + (++__id) }; Symbol.iterator = '__Symbol_iterator'; Symbol.toStringTag = '__Symbol_toStringTag'; Symbol.species = '__Symbol_species'; Symbol.for = function(k){ return '__Symbol_for_' + k }; }
