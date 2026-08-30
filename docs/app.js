/* ==========================================================================
   SimpleMirror - Landing Page Interactive Script
   ========================================================================== */

document.addEventListener('DOMContentLoaded', () => {
  // 1. Fetch latest release info from GitHub API (fallback to hardcoded v0.1.0)
  const repoOwner = 'k0ta0uchi';
  const repoName = 'SimpleMirror';

  async function updateReleaseInfo() {
    try {
      const response = await fetch(`https://api.github.com/repos/${repoOwner}/${repoName}/releases/latest`);
      if (!response.ok) return;
      const data = await response.json();

      if (data.tag_name) {
        // Update version badge
        const badges = document.querySelectorAll('.version-badge');
        badges.forEach(b => b.textContent = data.tag_name);

        // Find portable zip and exe
        const zipAsset = data.assets?.find(a => a.name.endsWith('-portable.zip') || a.name.endsWith('.zip'));
        const exeAsset = data.assets?.find(a => a.name.endsWith('.exe'));

        if (zipAsset) {
          const zipSizeMb = (zipAsset.size / (1024 * 1024)).toFixed(0);
          const dlBtn = document.getElementById('main-download-btn');
          if (dlBtn) {
            dlBtn.href = zipAsset.browser_download_url;
            const subText = dlBtn.querySelector('.btn-sub-text');
            if (subText) subText.textContent = `ポータブル版 ZIP (${data.tag_name}) • Windows 10/11 x64 (~${zipSizeMb} MB)`;
          }

          const primaryDl = document.querySelector('.primary-dl');
          if (primaryDl) {
            primaryDl.href = zipAsset.browser_download_url;
            const meta = primaryDl.querySelector('.dl-opt-meta');
            if (meta) meta.textContent = `${zipAsset.name} (~${zipSizeMb} MB)`;
          }
        }

        if (exeAsset) {
          const exeSizeMb = (exeAsset.size / (1024 * 1024)).toFixed(0);
          const secDl = document.querySelector('.secondary-dl');
          if (secDl) {
            secDl.href = exeAsset.browser_download_url;
            const meta = secDl.querySelector('.dl-opt-meta');
            if (meta) meta.textContent = `${exeAsset.name} (~${exeSizeMb} MB)`;
          }
        }
      }
    } catch (err) {
      console.log('GitHub API fetch bypassed, using default release assets.');
    }
  }

  updateReleaseInfo();

  // 2. Interactive Mockup Toolbar buttons
  const toolBtns = document.querySelectorAll('.tool-btn');
  toolBtns.forEach(btn => {
    btn.addEventListener('click', () => {
      toolBtns.forEach(b => b.classList.remove('active'));
      btn.classList.add('active');
    });
  });

  // 3. Smooth scroll for anchor links
  document.querySelectorAll('a[href^="#"]').forEach(anchor => {
    anchor.addEventListener('click', function (e) {
      const targetId = this.getAttribute('href');
      if (targetId === '#') return;
      const targetEl = document.querySelector(targetId);
      if (targetEl) {
        e.preventDefault();
        targetEl.scrollIntoView({ behavior: 'smooth' });
      }
    });
  });

  // 4. Subtle Card Tilt Effect
  const cards = document.querySelectorAll('.feature-card, .mockup-frame, .download-card');
  cards.forEach(card => {
    card.addEventListener('mousemove', (e) => {
      const rect = card.getBoundingClientRect();
      const x = e.clientX - rect.left;
      const y = e.clientY - rect.top;
      card.style.setProperty('--mouse-x', `${x}px`);
      card.style.setProperty('--mouse-y', `${y}px`);
    });
  });
});
