function escapeHtml(value) {
    const element = document.createElement('div');
    element.textContent = value == null ? '' : String(value);
    return element.innerHTML;
}

function number(value) {
    return Number(value || 0).toLocaleString('pt-BR');
}

// --- Event schedule helpers (read-only rendering of /api/events/schedule) ---
function parseTimeToMinutes(timeStr) {
    if (!timeStr || !timeStr.includes(':')) return 0;
    const parts = timeStr.split(':');
    const h = parseInt(parts[0], 10) || 0;
    const m = parseInt(parts[1], 10) || 0;
    return ((h % 24) * 60 + (m % 60));
}

function formatMinutesToTime(totalMin) {
    const normalized = ((Math.floor(totalMin) % 1440) + 1440) % 1440;
    const h = Math.floor(normalized / 60);
    const m = normalized % 60;
    return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}`;
}

function computeDailySchedule(startTimeStr, intervalMin) {
    const interval = Number(intervalMin);
    const initialTime = startTimeStr || '00:00';
    if (!interval || interval <= 0 || interval >= 1440) {
        return [initialTime];
    }
    const startMin = parseTimeToMinutes(initialTime);
    const timesSet = new Set();
    let current = startMin;
    const maxOccurrences = Math.min(144, Math.floor(1440 / Math.max(10, interval)) + 2);
    for (let i = 0; i < maxOccurrences; i++) {
        const formatted = formatMinutesToTime(current);
        if (timesSet.has(formatted)) break;
        timesSet.add(formatted);
        current = (current + interval) % 1440;
    }
    const result = Array.from(timesSet);
    result.sort((a, b) => parseTimeToMinutes(a) - parseTimeToMinutes(b));
    return result.length > 0 ? result : [initialTime];
}

function computeEventStatus(event, now) {
    const startTime = event.startTimeStr || (event.startOffsetMin !== undefined ? formatMinutesToTime(event.startOffsetMin) : '00:00');
    const interval = Number(event.startIntervalMin) || 120;
    const duration = Number(event.durationMin) || 15;
    const scheduledTimes = computeDailySchedule(startTime, interval);

    const currentMinuteOfDay = now.getHours() * 60 + now.getMinutes() + now.getSeconds() / 60;
    const occurrenceMinutes = scheduledTimes.map(t => parseTimeToMinutes(t)).sort((a, b) => a - b);

    let isOpen = false;
    let remainingOpenSec = 0;
    let nextOccurrenceMin = -1;

    for (const t of occurrenceMinutes) {
        const windowStart = t;
        const windowEnd = t + duration;
        if (windowEnd <= 1440) {
            if (currentMinuteOfDay >= windowStart && currentMinuteOfDay < windowEnd) {
                isOpen = true;
                remainingOpenSec = Math.max(0, Math.floor((windowEnd - currentMinuteOfDay) * 60));
                break;
            }
        } else {
            const overflowEnd = windowEnd - 1440;
            if (currentMinuteOfDay >= windowStart || currentMinuteOfDay < overflowEnd) {
                isOpen = true;
                const diff = currentMinuteOfDay >= windowStart
                    ? (windowEnd - currentMinuteOfDay)
                    : (overflowEnd - currentMinuteOfDay);
                remainingOpenSec = Math.max(0, Math.floor(diff * 60));
                break;
            }
        }
    }

    if (isOpen) {
        const m = Math.floor(remainingOpenSec / 60);
        const s = remainingOpenSec % 60;
        return {
            isOpen: true,
            isImminent: false,
            statusLabel: 'ABERTO AGORA',
            countdownFormatted: `Fecha em ${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`,
            scheduledTimes,
            nextOccurrenceTime: 'Portões Abertos'
        };
    }

    for (const t of occurrenceMinutes) {
        if (t > currentMinuteOfDay) {
            nextOccurrenceMin = t;
            break;
        }
    }

    let diffMinutes = 0;
    if (nextOccurrenceMin !== -1) {
        diffMinutes = nextOccurrenceMin - currentMinuteOfDay;
    } else {
        const firstOccurTomorrow = occurrenceMinutes[0] !== undefined ? occurrenceMinutes[0] : parseTimeToMinutes(startTime);
        diffMinutes = (1440 - currentMinuteOfDay) + firstOccurTomorrow;
        nextOccurrenceMin = firstOccurTomorrow;
    }

    const remainingSec = Math.max(0, Math.floor(diffMinutes * 60));
    const h = Math.floor(remainingSec / 3600);
    const m = Math.floor((remainingSec % 3600) / 60);
    const s = remainingSec % 60;

    let countdownFormatted = '';
    if (h > 0) {
        countdownFormatted = `${String(h).padStart(2, '0')}h ${String(m).padStart(2, '0')}m ${String(s).padStart(2, '0')}s`;
    } else {
        countdownFormatted = `${String(m).padStart(2, '0')}m ${String(s).padStart(2, '0')}s`;
    }

    const isImminent = remainingSec <= 300;
    return {
        isOpen: false,
        isImminent,
        statusLabel: isImminent ? 'INICIANDO!' : `Em ${h > 0 ? h + 'h ' : ''}${m}m`,
        countdownFormatted,
        scheduledTimes,
        nextOccurrenceTime: formatMinutesToTime(nextOccurrenceMin)
    };
}

function renderCharacters(characters) {
    const grid = document.getElementById('charactersGrid');
    const message = document.getElementById('accountMessage');
    document.getElementById('summaryCharacters').textContent = `${characters.length} CRIADOS`;

    if (!characters.length) {
        message.textContent = 'Nenhum personagem criado nesta conta.';
        return;
    }

    message.hidden = true;
    grid.innerHTML = characters.map(character => `
        <article class="character-card">
            <div class="character-header">
                <h3 class="character-name">${escapeHtml(character.name)}</h3>
                <span class="character-class-badge">${escapeHtml(character.className || 'Classe não informada')}</span>
            </div>
            <p class="character-guild">Guild: <strong>${escapeHtml(character.guild || 'Sem guild')}</strong><br>
                <span class="${character.status === 'Online' ? 'online-dot' : 'offline-dot'}">&#9679; ${character.status || 'Offline'}</span></p>
            <div class="char-stats-grid">
                <div class="char-stat-item"><span class="char-stat-lbl">Level</span><strong class="char-stat-val">${number(character.level)}</strong></div>
                <div class="char-stat-item"><span class="char-stat-lbl">Resets</span><strong class="char-stat-val">${number(character.resets)}</strong></div>
                <div class="char-stat-item"><span class="char-stat-lbl">Master Level</span><strong class="char-stat-val">${number(character.masterLevel)}</strong></div>
                <div class="char-stat-item"><span class="char-stat-lbl">PK Kills</span><strong class="char-stat-val">${number(character.kills)}</strong></div>
            </div>
        </article>
    `).join('');

    const totals = characters.reduce((result, character) => ({
        resets: result.resets + Number(character.resets || 0),
        kills: result.kills + Number(character.kills || 0),
        level: result.level + Number(character.level || 0)
    }), { resets: 0, kills: 0, level: 0 });
    document.getElementById('statsContent').innerHTML = `
        <div class="stat-overview-card"><span>LEVELS SOMADOS</span><strong>${number(totals.level)}</strong></div>
        <div class="stat-overview-card"><span>RESETS SOMADOS</span><strong>${number(totals.resets)}</strong></div>
        <div class="stat-overview-card"><span>PK KILLS</span><strong>${number(totals.kills)}</strong></div>
    `;
}

async function loadAccount() {
    const response = await fetch('/api/me');
    if (response.status === 401) {
        window.location.href = 'index.html';
        return;
    }
    const data = await response.json();
    if (!response.ok || !data.success) throw new Error(data.message || 'Erro ao carregar painel da conta');

    const account = data.account;
    document.getElementById('accountLogin').textContent = account.loginName;
    document.getElementById('accountEmail').textContent = account.email;
    document.getElementById('accountInitial').textContent = account.loginName.charAt(0).toUpperCase();
    document.getElementById('summaryLogin').textContent = account.loginName.toUpperCase();
    document.getElementById('summaryEmail').textContent = account.email;
    renderCharacters(account.characters || []);
}

document.querySelectorAll('.panel-tab').forEach(tab => {
    tab.addEventListener('click', async () => {
        document.querySelectorAll('.panel-tab').forEach(item => item.classList.remove('active'));
        document.querySelectorAll('.panel-content').forEach(panel => panel.classList.add('hidden-panel'));
        tab.classList.add('active');
        const panelId = `${tab.dataset.panel}Panel`;
        document.getElementById(panelId).classList.remove('hidden-panel');

        // Load eventos.html content when Events tab is clicked
        if (tab.dataset.panel === 'events') {
            await loadEventsPanel();
        }
    });
});

async function loadEventsPanel() {
    const eventsContent = document.getElementById('eventsContent');

    // Check if already loaded
    if (eventsContent.dataset.loaded === 'true') {
        return;
    }

    // Load eventos.css for the event card styles (read-only rendering).
    if (!document.querySelector('link[href*="eventos.css"]')) {
        const link = document.createElement('link');
        link.rel = 'stylesheet';
        link.href = '/css/eventos.css';
        document.head.appendChild(link);
    }

    try {
        // Read-only: consume the same public schedule used by the home page.
        const response = await fetch('/api/events/schedule');
        if (!response.ok) {
            throw new Error(`Erro ao carregar eventos: ${response.status}`);
        }
        const data = await response.json();
        const events = (data.success && Array.isArray(data.events)) ? data.events : [];

        if (events.length === 0) {
            eventsContent.innerHTML = `
                <div class="events-empty">
                    <h3>&#128197; Nenhum evento configurado</h3>
                    <p>Os eventos do servidor aparecerão aqui assim que forem configurados pela equipe.</p>
                </div>
            `;
            eventsContent.dataset.loaded = 'true';
            return;
        }

        const now = new Date();
        const cards = events.map(ev => {
            const status = computeEventStatus(ev, now);
            const startTimeStr = ev.startTimeStr || formatMinutesToTime(ev.startOffsetMin || 0);
            const isOpen = status.isOpen;
            const isImminent = status.isImminent;

            let cardClass = 'event-card';
            if (isOpen) cardClass += ' event-card-open';
            else if (isImminent) cardClass += ' event-card-imminent';

            let statusPillHtml = '';
            if (isOpen) {
                statusPillHtml = `<span class="event-status-pill open"><span class="status-pulse-dot"></span> ABERTO AGORA</span>`;
            } else if (isImminent) {
                statusPillHtml = `<span class="event-status-pill imminent"><span class="status-pulse-dot" style="background:#f59e0b"></span> ${status.countdownFormatted}</span>`;
            } else {
                statusPillHtml = `<span class="event-status-pill upcoming">${status.countdownFormatted}</span>`;
            }

            return `
                <div class="${cardClass}">
                    <div class="event-card-header mb-3">
                        <div style="display:flex; align-items:center; gap:12px;">
                            <div class="event-icon-badge" style="background:${ev.colorTheme}15; border-color:${ev.colorTheme}40; color:${ev.colorTheme};">
                                ${ev.icon}
                            </div>
                            <div>
                                <h3 class="event-info-title">${escapeHtml(ev.name)}</h3>
                                <div class="event-info-category" style="background:${ev.category === 'invasions' ? 'rgba(239,68,68,0.15)' : 'rgba(56,189,248,0.15)'}; border:1px solid ${ev.category === 'invasions' ? 'rgba(239,68,68,0.3)' : 'rgba(56,189,248,0.3)'}; color:${ev.category === 'invasions' ? '#fca5a5' : '#7dd3fc'};">
                                    ${ev.category === 'invasions' ? '🐲 Invasão de Monstros' : '⚔️ Evento Clássico'}
                                </div>
                            </div>
                        </div>
                        ${statusPillHtml}
                    </div>
                    <div class="event-specs-grid mb-3">
                        <div class="event-spec-item">
                            <span class="event-spec-label">Horário Inicial</span>
                            <span class="event-spec-val" style="font-family:monospace; color:#fef08a;">${startTimeStr}</span>
                        </div>
                        <div class="event-spec-item">
                            <span class="event-spec-label">Intervalo</span>
                            <span class="event-spec-val" style="font-family:monospace; color:#fed7aa;">A cada ${ev.startIntervalMin}m</span>
                        </div>
                        <div class="event-spec-item">
                            <span class="event-spec-label">Duração Portão</span>
                            <span class="event-spec-val" style="color:#6ee7b7;">${ev.durationMin} min</span>
                        </div>
                        <div class="event-spec-item">
                            <span class="event-spec-label">Localização</span>
                            <span class="event-spec-val" style="font-size:12px; color:#cbd5e1;" title="${escapeHtml(ev.location)}">${escapeHtml(ev.location)}</span>
                        </div>
                    </div>
                    <div style="background:rgba(0,0,0,0.4); border:1px solid rgba(212,175,55,0.15); border-radius:6px; padding:8px;">
                        <span class="event-spec-label" style="display:block; color:#d4af37; font-weight:bold; margin-bottom:2px;">🎁 Drops &amp; Premiações</span>
                        <p style="font-size:12px; color:#fde047; margin:0; line-height:1.3;">${escapeHtml(ev.rewardTag)}</p>
                    </div>
                </div>
            `;
        }).join('');

        eventsContent.innerHTML = `<div class="events-cards-grid">${cards}</div>`;
        eventsContent.dataset.loaded = 'true';
    } catch (error) {
        eventsContent.innerHTML = `
            <div class="events-error">
                <h3>&#9888;&#65039; Erro ao carregar eventos</h3>
                <p>Não foi possível carregar os eventos do servidor.</p>
                <p>${escapeHtml(error.message)}</p>
                <button class="btn-retry" onclick="loadEventsPanel()">Tentar Novamente</button>
            </div>
        `;
    }
}

document.getElementById('logoutBtn').addEventListener('click', async () => {
    await fetch('/api/logout', { method: 'POST' });
    window.location.href = 'index.html';
});

loadAccount()
    .then(() => loadEventsPanel())
    .catch(error => {
        document.getElementById('accountMessage').textContent = error.message;
    });
