// Create floating background particles
(function createParticles() {
    const container = document.getElementById('particles');
    if (!container) return;
    for (let i = 0; i < 30; i++) {
        const p = document.createElement('div');
        p.className = 'particle';
        const size = Math.random() * 6 + 3;
        p.style.width = size + 'px';
        p.style.height = size + 'px';
        p.style.left = Math.random() * 100 + '%';
        p.style.animationDuration = (Math.random() * 15 + 15) + 's';
        p.style.animationDelay = (Math.random() * 20) + 's';
        container.appendChild(p);
    }
})();

// Load weather forecast from API
async function loadWeather() {
    const grid = document.getElementById('weatherGrid');
    grid.innerHTML = '<div class="loading-spinner"><div class="spinner"></div><p>Loading forecast...</p></div>';

    try {
        const res = await fetch('/api/weatherforecast');
        if (!res.ok) throw new Error('API request failed');
        const data = await res.json();

        const days = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

        grid.innerHTML = data.map((item, i) => {
            const date = new Date(item.date);
            const dayName = days[date.getDay()];
            const dateStr = date.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
            const tempClass = item.temperatureC < 0 ? 'temp-cold'
                : item.temperatureC < 15 ? 'temp-cool'
                : item.temperatureC < 25 ? 'temp-mild'
                : item.temperatureC < 35 ? 'temp-warm'
                : 'temp-hot';

            return `
                <div class="weather-card ${tempClass}" style="animation-delay: ${i * 0.08}s">
                    <div class="weather-icon">${item.icon}</div>
                    <div class="weather-day">${dayName}</div>
                    <div class="weather-summary">${dateStr}</div>
                    <div class="weather-temp">${item.temperatureC}&deg;C</div>
                    <div class="weather-temp-f">${item.temperatureF}&deg;F</div>
                    <div class="weather-summary">${item.summary}</div>
                </div>
            `;
        }).join('');
    } catch (err) {
        grid.innerHTML = `<div class="loading-spinner"><p>Failed to load forecast. <button class="btn btn-outline" onclick="loadWeather()">Retry</button></p></div>`;
    }
}

// Load app info from API
async function loadInfo() {
    const grid = document.getElementById('infoGrid');

    try {
        const res = await fetch('/api/info');
        if (!res.ok) throw new Error('API request failed');
        const data = await res.json();

        const items = [
            { label: 'Application', value: data.appName, icon: '\u{1F4E6}' },
            { label: 'Framework', value: data.framework, icon: '\u{26A1}' },
            { label: 'Host Machine', value: data.host, icon: '\u{1F5A5}\uFE0F' },
            { label: 'Environment', value: data.environment, icon: '\u{1F30D}' },
            { label: 'Server Time (UTC)', value: new Date(data.time).toLocaleString(), icon: '\u{1F552}' },
        ];

        grid.innerHTML = items.map((item, i) => `
            <div class="info-card" style="animation-delay: ${i * 0.1}s">
                <div class="info-label">${item.icon} ${item.label}</div>
                <div class="info-value">${item.value}</div>
            </div>
        `).join('');
    } catch (err) {
        grid.innerHTML = `<div class="loading-spinner"><p>Failed to load info.</p></div>`;
    }
}

async function loadCurrentUser() {
    const target = document.getElementById('authResult');
    target.innerHTML = '<div class="loading-spinner"><div class="spinner"></div><p>Loading user profile...</p></div>';

    try {
        const res = await fetch('/api/me');
        const contentType = res.headers.get('content-type') || '';

        if (!res.ok || !contentType.includes('application/json')) {
            target.innerHTML = '<div class="info-card"><div class="info-label">Result</div><div class="info-value">Not authenticated. Use Sign In first.</div></div>';
            return;
        }

        const data = await res.json();
        target.innerHTML = `
            <div class="info-card">
                <div class="info-label">Authenticated User</div>
                <div class="info-value">${data.name || 'Unknown'}</div>
            </div>
            <div class="info-card">
                <div class="info-label">Roles</div>
                <div class="info-value">${(data.roles || []).join(', ') || 'None'}</div>
            </div>
        `;
    } catch (err) {
        target.innerHTML = '<div class="info-card"><div class="info-label">Error</div><div class="info-value">Unable to call /api/me.</div></div>';
    }
}

async function loadReaderAccess() {
    const target = document.getElementById('authResult');
    target.innerHTML = '<div class="loading-spinner"><div class="spinner"></div><p>Checking App.Reader/App.Admin access...</p></div>';

    try {
        const res = await fetch('/api/reader');
        const contentType = res.headers.get('content-type') || '';

        if (res.status === 403) {
            target.innerHTML = '<div class="info-card"><div class="info-label">Authorization</div><div class="info-value">Signed in, but missing App.Reader/App.Admin role.</div></div>';
            return;
        }

        if (!res.ok || !contentType.includes('application/json')) {
            target.innerHTML = '<div class="info-card"><div class="info-label">Authorization</div><div class="info-value">Not authenticated. Use Sign In first.</div></div>';
            return;
        }

        const data = await res.json();
        target.innerHTML = `
            <div class="info-card">
                <div class="info-label">Authorization</div>
                <div class="info-value">${data.message}</div>
            </div>
        `;
    } catch (err) {
        target.innerHTML = '<div class="info-card"><div class="info-label">Error</div><div class="info-value">Unable to call /api/reader.</div></div>';
    }
}

async function loadAdminAccess() {
    const target = document.getElementById('authResult');
    target.innerHTML = '<div class="loading-spinner"><div class="spinner"></div><p>Checking App.Admin access...</p></div>';

    try {
        const res = await fetch('/api/admin');
        const contentType = res.headers.get('content-type') || '';

        if (res.status === 403) {
            target.innerHTML = '<div class="info-card"><div class="info-label">Authorization</div><div class="info-value">Signed in, but missing App.Admin role.</div></div>';
            return;
        }

        if (!res.ok || !contentType.includes('application/json')) {
            target.innerHTML = '<div class="info-card"><div class="info-label">Authorization</div><div class="info-value">Not authenticated. Use Sign In first.</div></div>';
            return;
        }

        const data = await res.json();
        target.innerHTML = `
            <div class="info-card">
                <div class="info-label">Authorization</div>
                <div class="info-value">${data.message}</div>
            </div>
        `;
    } catch (err) {
        target.innerHTML = '<div class="info-card"><div class="info-label">Error</div><div class="info-value">Unable to call /api/admin.</div></div>';
    }
}

// Navbar scroll effect
window.addEventListener('scroll', () => {
    const nav = document.querySelector('.navbar');
    nav.style.borderBottomColor = window.scrollY > 50 ? 'rgba(99,102,241,0.3)' : '';
});

// Initialize
document.addEventListener('DOMContentLoaded', () => {
    loadWeather();
    loadInfo();
    loadCurrentUser();
});
