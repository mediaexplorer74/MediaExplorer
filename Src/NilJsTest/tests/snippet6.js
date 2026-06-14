(function(){
    const t=document.createElement("link").relList;
    if(t&&t.supports&&t.supports("modulepreload"))return;
    for(const o of document.querySelectorAll('link[rel="modulepreload"]'))n(o);
    new MutationObserver(o=>{for(const r of o)if(r.type==="childList")for(const a of r.addedNodes)a.tagName==="LINK"&&a.rel==="modulepreload"&&n(a)}).observe(document,{childList:!0,subtree:!0});
    function i(o){const r={};return o.integrity&&(r.integrity=o.integrity),o.referrerPolicy&&(r.referrerPolicy=o.referrerPolicy),o.crossOrigin==="use-credentials"?r.credentials="include":o.crossOrigin==="anonymous"?r.credentials="omit":r.credentials="same-origin",r}
    function n(o){if(o.ep)return;o.ep=!0;const r=i(o);fetch(o.href,r)}
})();