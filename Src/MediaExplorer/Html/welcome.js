// If you want to keep your original, it's at /mnt/data/welcome.js. This version is an optimized drop-in.
// Original file content (for reference) is available at: /mnt/data/welcome.js. :contentReference[oaicite:4]{index=4}

/* ========= Clock + Greeting + Date ========= */
function updateTimeOnce() {
  const now = new Date();

  // Clock HH:MM
  const timeString = now.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  const clock = document.getElementById('clock');
  if (clock) clock.textContent = timeString;

  // Date
  const options = { weekday: 'long', month: 'long', day: 'numeric' };
  const dateEl = document.getElementById('date-display');
  if (dateEl) dateEl.textContent = now.toLocaleDateString(undefined, options);

  // Greeting
  const hour = now.getHours();
  const greeting = document.getElementById('greeting');
  if (greeting) {
    if (hour < 12) greeting.textContent = "Good morning";
    else if (hour < 18) greeting.textContent = "Good afternoon";
    else greeting.textContent = "Good evening";
  }
}

// update once now, then every second (keeps sync)
updateTimeOnce();
setInterval(updateTimeOnce, 1000);

/* ========= Search handling ========= */
const searchInput = document.getElementById('search-input');
if (searchInput) {
  searchInput.addEventListener('keypress', function (e) {
    if (e.key === 'Enter') {
      const query = searchInput.value.trim();
      if (!query) return;

      // Basic URL heuristic
      const looksLikeURL = (q) => {
        // allow localhost, ip and domain names; require dot OR protocol OR endswith common tld
        return q.startsWith('http://') || q.startsWith('https://') || q.includes('.') && !q.includes(' ');
      };

      if (looksLikeURL(query)) {
        let url = query;
        if (!/^https?:\/\//i.test(url)) url = 'https://' + url;
        // For an embeddable browser, you might want to postMessage to the embed parent instead.
        window.location.href = url;
      } else {
        window.location.href = `https://www.google.com/search?q=${encodeURIComponent(query)}`;
      }
    }
  });
}

/* ========= Keyboard shortcuts ========= */
document.addEventListener('keydown', (e) => {
  if (!e.altKey) return;
  const key = e.key.toLowerCase();

  // Alt+L -> focus search
  if (key === 'l') {
    e.preventDefault();
    searchInput && searchInput.focus();
  }

  // Alt+1 .. Alt+4 open quick links
  if (['1','2','3','4'].includes(key)) {
    e.preventDefault();
    const link = document.querySelector(`.link-card[data-key="${key}"]`);
    if (link) {
      // open in same window for a real browser; change as desired
      window.open(link.href, '_blank', 'noopener,noreferrer');
    }
  }
});

/* ========= Particle trail (throttled & limited) ========= */
(function setupParticles() {
  const maxParticles = 30;
  let active = 0;
  let last = 0;
  const throttleMs = 40; // at most ~25 particles/sec

  function makeParticle(x, y) {
    if (active >= maxParticles) return;
    active++;
    const particle = document.createElement('div');
    particle.className = 'particle';
    const size = Math.random() * 18 + 8;
    particle.style.width = `${size}px`;
    particle.style.height = `${size}px`;
    particle.style.left = `${x}px`;
    particle.style.top = `${y}px`;
    document.body.appendChild(particle);

    // cleanup
    particle.addEventListener('animationend', () => {
      particle.remove();
      active = Math.max(0, active - 1);
    }, { once: true });

    // fallback cleanup in case animationend doesn't fire
    setTimeout(() => {
      if (particle.parentElement) particle.remove();
      active = Math.max(0, active - 1);
    }, 1200);
  }

  document.addEventListener('mousemove', (e) => {
    const now = performance.now();
    if (now - last < throttleMs) return;
    last = now;

    // small probability so it's subtle, but now throttled
    if (Math.random() > 0.35) return;

    makeParticle(e.clientX, e.clientY);
  });

  // touch support: create on touch
  document.addEventListener('touchmove', (e) => {
    const t = e.touches[0];
    if (!t) return;
    makeParticle(t.clientX, t.clientY);
  }, { passive: true });
})();

/* ========= Optional: allow customizing quick links via localStorage ========= */
(function loadLinksFromStorage() {
  try {
    const stored = JSON.parse(localStorage.getItem('quick_links') || 'null');
    if (Array.isArray(stored) && stored.length) {
      const container = document.querySelector('.links-grid');
      if (!container) return;
      container.innerHTML = ''; // rebuild
      stored.forEach((item, idx) => {
        const a = document.createElement('a');
        a.href = item.href || '#';
        a.className = 'link-card';
        a.dataset.key = String(idx + 1);
        a.target = '_blank';
        a.rel = 'noopener noreferrer';
        const icon = document.createElement('div');
        icon.className = 'icon';
        icon.textContent = item.icon || (item.title ? item.title.charAt(0) : '?');
        a.appendChild(icon);
        const span = document.createElement('span');
        span.textContent = item.title || item.href;
        a.appendChild(span);
        container.appendChild(a);
      });
    }
  } catch (err) {
    // ignore
  }
})();
