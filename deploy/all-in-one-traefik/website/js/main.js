/**
 * MU Online Website - Main JavaScript
 */

// Toggle password visibility
function togglePassword(inputId) {
    const input = document.getElementById(inputId);
    const type = input.type === 'password' ? 'text' : 'password';
    input.type = type;
}

// Password strength checker
function checkPasswordStrength(password) {
    let strength = 0;
    const bar = document.querySelector('.strength-bar');
    const text = document.querySelector('.strength-text');

    if (password.length >= 6) strength++;
    if (password.length >= 10) strength++;
    if (/[a-z]/.test(password) && /[A-Z]/.test(password)) strength++;
    if (/\d/.test(password)) strength++;
    if (/[^a-zA-Z0-9]/.test(password)) strength++;

    bar.className = 'strength-bar';

    if (password.length === 0) {
        text.textContent = '';
    } else if (strength <= 2) {
        bar.classList.add('weak');
        text.textContent = 'Fraca';
        text.style.color = '#ff4444';
    } else if (strength <= 3) {
        bar.classList.add('medium');
        text.textContent = 'Média';
        text.style.color = '#ffaa00';
    } else {
        bar.classList.add('strong');
        text.textContent = 'Forte';
        text.style.color = '#00ff88';
    }
}

// Validate login name (alphanumeric only, max 10 chars)
function validateLoginName(loginName) {
    const regex = /^[a-zA-Z0-9]+$/;
    return regex.test(loginName) && loginName.length >= 3 && loginName.length <= 10;
}

// Validate email
function validateEmail(email) {
    const regex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    return regex.test(email);
}

// Account login and dashboard
const loginOverlay = document.getElementById('loginOverlay');

function openLogin() {
    loginOverlay.hidden = false;
    document.getElementById('loginUser').focus();
}

function closeLogin() {
    loginOverlay.hidden = true;
    document.getElementById('loginError').hidden = true;
}

document.getElementById('loginOpenBtn').addEventListener('click', openLogin);
document.getElementById('loginCloseBtn').addEventListener('click', closeLogin);
loginOverlay.addEventListener('click', event => {
    if (event.target === loginOverlay) closeLogin();
});

document.getElementById('loginForm').addEventListener('submit', async event => {
    event.preventDefault();
    const error = document.getElementById('loginError');
    const submit = document.getElementById('loginSubmitBtn');
    error.hidden = true;
    submit.disabled = true;

    try {
        const response = await fetch('/api/login', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                loginName: document.getElementById('loginUser').value.trim(),
                password: document.getElementById('loginPassword').value
            })
        });
        const data = await response.json();
        if (!response.ok || !data.success) throw new Error(data.message || 'Não foi possível entrar');
        closeLogin();
        window.location.href = 'account.html';
    } catch (loginError) {
        error.textContent = loginError.message;
        error.hidden = false;
    } finally {
        submit.disabled = false;
    }
});

// Show error message
function showError(message) {
    const form = document.getElementById('registerForm');
    const errorDiv = document.getElementById('errorMessage');
    const errorText = document.getElementById('errorText');

    form.style.display = 'none';
    errorDiv.style.display = 'block';
    errorText.textContent = message;
}

// Hide error message
function hideError() {
    const form = document.getElementById('registerForm');
    const errorDiv = document.getElementById('errorMessage');

    errorDiv.style.display = 'none';
    form.style.display = 'flex';
}

// Show success message
function showSuccess(loginName) {
    const form = document.getElementById('registerForm');
    const successDiv = document.getElementById('successMessage');
    const successLogin = document.getElementById('successLogin');

    form.style.display = 'none';
    successDiv.style.display = 'block';
    successLogin.textContent = loginName;
}

// Set loading state
function setLoading(loading) {
    const submitBtn = document.getElementById('submitBtn');
    const btnText = submitBtn.querySelector('.btn-text');
    const btnLoading = submitBtn.querySelector('.btn-loading');

    submitBtn.disabled = loading;
    btnText.style.display = loading ? 'none' : 'block';
    btnLoading.style.display = loading ? 'flex' : 'none';
}

// Register account via API
async function registerAccount(data) {
    const response = await fetch('/api/register', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
        },
        body: JSON.stringify(data)
    });

    const result = await response.json();

    if (!response.ok) {
        throw new Error(result.message || 'Erro ao criar conta');
    }

    return result;
}

// Form submission handler
document.getElementById('registerForm').addEventListener('submit', async function(e) {
    e.preventDefault();

    const loginName = document.getElementById('loginName').value.trim();
    const password = document.getElementById('password').value;
    const confirmPassword = document.getElementById('confirmPassword').value;
    const email = document.getElementById('email').value.trim();
    const securityCode = document.getElementById('securityCode').value.trim();
    const terms = document.getElementById('terms').checked;

    // Validation
    if (!validateLoginName(loginName)) {
        showError('Login deve ter entre 3 e 10 caracteres alfanuméricos.');
        return;
    }

    if (password.length < 6) {
        showError('Senha deve ter pelo menos 6 caracteres.');
        return;
    }

    if (password !== confirmPassword) {
        showError('As senhas não coincidem.');
        return;
    }

    if (!validateEmail(email)) {
        showError('E-mail inválido.');
        return;
    }

    if (securityCode.length < 4) {
        showError('Código de segurança deve ter pelo menos 4 caracteres.');
        return;
    }

    if (!terms) {
        showError('Você deve aceitar os Termos de Uso.');
        return;
    }

    setLoading(true);

    try {
        await registerAccount({
            loginName,
            password,
            email,
            securityCode
        });

        showSuccess(loginName);
    } catch (error) {
        showError(error.message);
    } finally {
        setLoading(false);
    }
});

// Password strength listener
document.getElementById('password').addEventListener('input', function(e) {
    checkPasswordStrength(e.target.value);
});

// Login name validation listener
document.getElementById('loginName').addEventListener('input', function(e) {
    const value = e.target.value;
    // Remove non-alphanumeric characters
    e.target.value = value.replace(/[^a-zA-Z0-9]/g, '');
});

// Smooth scroll for navigation links
document.querySelectorAll('.nav-link').forEach(link => {
    link.addEventListener('click', function(e) {
        e.preventDefault();
        const targetId = this.getAttribute('href');
        const target = document.querySelector(targetId);
        if (target) {
            target.scrollIntoView({ behavior: 'smooth', block: 'start' });
        }

        // Update active state
        document.querySelectorAll('.nav-link').forEach(l => l.classList.remove('active'));
        this.classList.add('active');
    });
});

// Header scroll effect
window.addEventListener('scroll', function() {
    const header = document.querySelector('.header');
    if (window.scrollY > 50) {
        header.style.background = 'rgba(15, 15, 26, 0.98)';
    } else {
        header.style.background = 'rgba(15, 15, 26, 0.95)';
    }
});

// Class card hover effect
document.querySelectorAll('.class-card').forEach(card => {
    card.addEventListener('mouseenter', function() {
        this.style.borderColor = 'var(--primary-color)';
    });
    card.addEventListener('mouseleave', function() {
        this.style.borderColor = 'var(--border-color)';
    });
});

// Ranking System with Auto-Refresh
const RANKING_REFRESH_INTERVAL = 10 * 60 * 1000; // 10 minutes

// Load level ranking from API
async function loadLevelRanking() {
    const rankingBody = document.getElementById('levelRankingBody');
    
    rankingBody.innerHTML = `
        <tr class="loading-row">
            <td colspan="4">
                <div class="ranking-loading">
                    <svg class="spinner" viewBox="0 0 24 24">
                        <circle cx="12" cy="12" r="10" stroke="currentColor" stroke-width="3" fill="none" stroke-dasharray="31.4 31.4"></circle>
                    </svg>
                    <span>Carregando ranking...</span>
                </div>
            </td>
        </tr>
    `;

    try {
        const response = await fetch('/api/ranking');
        const data = await response.json();

        if (!data.success || !data.ranking || data.ranking.length === 0) {
            rankingBody.innerHTML = `
                <tr>
                    <td colspan="4">
                        <div class="ranking-empty">
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"></path>
                                <circle cx="9" cy="7" r="4"></circle>
                            </svg>
                            <p>Nenhum personagem encontrado</p>
                            <p>Seja o primeiro a jogar!</p>
                        </div>
                    </td>
                </tr>
            `;
            return;
        }

        const rankIcons = ['🥇', '🥈', '🥉'];
        const rankColors = ['#ffd700', '#c0c0c0', '#cd7f32'];

        rankingBody.innerHTML = data.ranking.map((player, index) => {
            const isTop3 = index < 3;
            const rankClass = isTop3 ? `rank-${index + 1}` : 'rank-other';
            const rankDisplay = isTop3 ? rankIcons[index] : player.position;
            
            const hasGuild = player.guildName && player.guildName !== '-';
            const guildDisplay = hasGuild
                ? `<span class="guild-name"><span class="guild-icon">⚔️</span><span class="guild-text">${escapeHtml(player.guildName)}</span></span>`
                : `<span class="guild-name"><span class="guild-text no-guild">Sem guild</span></span>`;

            const levelPercent = Math.min((player.level / 400) * 100, 100);
            const levelColor = isTop3 ? rankColors[index] : 'var(--primary-color)';

            return `
                <tr class="ranking-row ${isTop3 ? 'top-player' : ''}" style="animation-delay: ${index * 0.1}s">
                    <td class="rank-col">
                        <div class="rank-cell">
                            <span class="rank-badge ${rankClass}">${rankDisplay}</span>
                        </div>
                    </td>
                    <td class="name-col">
                        <div class="character-name">
                            <div class="character-avatar" style="background: ${isTop3 ? `linear-gradient(135deg, ${rankColors[index]} 0%, #fff 100%)` : 'var(--gradient-gold)'}">
                                ${isTop3 ? rankIcons[index] : '👤'}
                            </div>
                            <div class="character-info">
                                <span class="name">${escapeHtml(player.characterName)}</span>
                            </div>
                        </div>
                    </td>
                    <td class="guild-col">
                        ${guildDisplay}
                    </td>
                    <td class="level-col">
                        <div class="level-container">
                            <span class="level-badge" style="border-color: ${levelColor}; color: ${levelColor};">Lv. ${player.level}</span>
                            <div class="level-bar-container">
                                <div class="level-bar" style="width: ${levelPercent}%; background: ${levelColor};"></div>
                            </div>
                        </div>
                    </td>
                </tr>
            `;
        }).join('');

    } catch (error) {
        console.error('Error loading level ranking:', error);
        rankingBody.innerHTML = `
            <tr>
                <td colspan="4">
                    <div class="ranking-error">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <circle cx="12" cy="12" r="10"></circle>
                            <line x1="12" y1="8" x2="12" y2="12"></line>
                            <line x1="12" y1="16" x2="12.01" y2="16"></line>
                        </svg>
                        <p>Erro ao carregar ranking</p>
                        <button class="btn btn-secondary btn-reload" onclick="loadLevelRanking()">Tentar Novamente</button>
                    </div>
                </td>
            </tr>
        `;
    }
}

// Load deaths ranking from API
async function loadDeathsRanking() {
    const rankingBody = document.getElementById('deathsRankingBody');
    
    rankingBody.innerHTML = `
        <tr class="loading-row">
            <td colspan="4">
                <div class="ranking-loading">
                    <svg class="spinner" viewBox="0 0 24 24">
                        <circle cx="12" cy="12" r="10" stroke="currentColor" stroke-width="3" fill="none" stroke-dasharray="31.4 31.4"></circle>
                    </svg>
                    <span>Carregando ranking...</span>
                </div>
            </td>
        </tr>
    `;

    try {
        const response = await fetch('/api/ranking/deaths');
        const data = await response.json();

        if (!data.success || !data.ranking || data.ranking.length === 0) {
            rankingBody.innerHTML = `
                <tr>
                    <td colspan="4">
                        <div class="ranking-empty">
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"></path>
                                <circle cx="9" cy="7" r="4"></circle>
                            </svg>
                            <p>Nenhum personagem encontrado</p>
                            <p>Seja o primeiro a jogar!</p>
                        </div>
                    </td>
                </tr>
            `;
            return;
        }

        const rankIcons = ['💀', '☠️', '⚰️'];
        const rankColors = ['#ff4444', '#ff6666', '#ff8888'];

        rankingBody.innerHTML = data.ranking.map((player, index) => {
            const isTop3 = index < 3;
            const rankClass = isTop3 ? `rank-death-${index + 1}` : 'rank-other';
            const rankDisplay = isTop3 ? rankIcons[index] : player.position;
            
            const hasGuild = player.guildName && player.guildName !== '-';
            const guildDisplay = hasGuild
                ? `<span class="guild-name"><span class="guild-icon">⚔️</span><span class="guild-text">${escapeHtml(player.guildName)}</span></span>`
                : `<span class="guild-name"><span class="guild-text no-guild">Sem guild</span></span>`;

            const kills = player.kills || 0;
            const killsPercent = Math.min((kills / 100) * 100, 100);
            const killsColor = isTop3 ? rankColors[index] : 'var(--accent-color)';

            return `
                <tr class="ranking-row ${isTop3 ? 'top-player death-player' : ''}" style="animation-delay: ${index * 0.1}s">
                    <td class="rank-col">
                        <div class="rank-cell">
                            <span class="rank-badge ${rankClass}">${rankDisplay}</span>
                        </div>
                    </td>
                    <td class="name-col">
                        <div class="character-name">
                            <div class="character-avatar death-avatar" style="background: ${isTop3 ? `linear-gradient(135deg, ${rankColors[index]} 0%, #fff 100%)` : 'var(--accent-color)'}">
                                ${isTop3 ? rankIcons[index] : '👤'}
                            </div>
                            <div class="character-info">
                                <span class="name">${escapeHtml(player.characterName)}</span>
                            </div>
                        </div>
                    </td>
                    <td class="guild-col">
                        ${guildDisplay}
                    </td>
                    <td class="kills-col">
                        <div class="kills-container">
                            <span class="kills-badge" style="border-color: ${killsColor}; color: ${killsColor};">${kills} kills</span>
                            <div class="kills-bar-container">
                                <div class="kills-bar" style="width: ${killsPercent}%; background: ${killsColor};"></div>
                            </div>
                        </div>
                    </td>
                </tr>
            `;
        }).join('');

    } catch (error) {
        console.error('Error loading deaths ranking:', error);
        rankingBody.innerHTML = `
            <tr>
                <td colspan="4">
                    <div class="ranking-error">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <circle cx="12" cy="12" r="10"></circle>
                            <line x1="12" y1="8" x2="12" y2="12"></line>
                            <line x1="12" y1="16" x2="12.01" y2="16"></line>
                        </svg>
                        <p>Erro ao carregar ranking de mortes</p>
                        <button class="btn btn-secondary btn-reload" onclick="loadDeathsRanking()">Tentar Novamente</button>
                    </div>
                </td>
            </tr>
        `;
    }
}

// Update last refresh time
function updateLastRefreshTime() {
    const lastUpdateEl = document.getElementById('lastUpdate');
    if (lastUpdateEl) {
        const now = new Date();
        const timeStr = now.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' });
        lastUpdateEl.textContent = `Última atualização: ${timeStr}`;
    }
}

// Load all rankings
async function loadAllRankings() {
    await Promise.all([loadLevelRanking(), loadDeathsRanking()]);
    updateLastRefreshTime();
}

// Auto-refresh rankings every 10 minutes
function startRankingAutoRefresh() {
    setInterval(loadAllRankings, RANKING_REFRESH_INTERVAL);
}

// Tab switching for rankings
function initRankingTabs() {
    const tabs = document.querySelectorAll('.ranking-tab');
    const contents = document.querySelectorAll('.ranking-content');

    tabs.forEach(tab => {
        tab.addEventListener('click', () => {
            const targetId = tab.dataset.tab;

            // Update active tab
            tabs.forEach(t => t.classList.remove('active'));
            tab.classList.add('active');

            // Update active content
            contents.forEach(c => c.classList.remove('active'));
            document.getElementById(`${targetId}Ranking`).classList.add('active');

            // Load ranking if not loaded yet
            if (targetId === 'level') {
                loadLevelRanking();
            } else {
                loadDeathsRanking();
            }
        });
    });
}

// Escape HTML to prevent XSS
function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

// Initialize
document.addEventListener('DOMContentLoaded', function() {
    console.log('MU Online Website loaded');
    loadAllRankings();
    startRankingAutoRefresh();
    initRankingTabs();
});
